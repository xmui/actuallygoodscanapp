using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ScanApp.App.Infrastructure;
using ScanApp.Core.Imaging;
using ScanApp.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ScanApp.App.ViewModels;

/// <summary>
/// A single page in the session: preview/thumbnail plus reversible per-page edits (crop, rotation,
/// dust removal, auto-levels, brightness/contrast). Tonal/geometry edits are recomputed from a
/// retained pristine original so they can always be undone; crop is stored normalized and applied
/// only at export.
/// </summary>
public sealed class PageViewModel : ObservableObject, IDisposable
{
    private readonly Action<PageViewModel> _onDelete;
    private readonly Dispatcher _dispatcher = Application.Current.Dispatcher;
    private readonly Image<Rgba32> _original;

    private BitmapSource _thumbnail;
    private BitmapSource _preview;
    private int _number;

    private NormalizedRect _cropRect = NormalizedRect.Full;
    private bool _isCropActive = PageEdits.Defaults.CropActive;   // on by default
    private double _rotationDegrees;
    private int _brightness;
    private int _contrast;
    private bool _removeDust;
    private bool _autoLevel = PageEdits.Defaults.AutoLevel;        // on by default
    private bool _documentCleanup;
    private bool _blackAndWhite;
    private bool _recomputing;
    private bool _recomputePending;

    /// <summary>Constructs a page and restores a saved edit state (used when opening a project).</summary>
    public PageViewModel(ScannedPage page, Action<PageViewModel> onDelete, PageEdits edits)
        : this(page, onDelete)
    {
        ApplyEdits(edits);
    }

    public PageViewModel(ScannedPage page, Action<PageViewModel> onDelete)
    {
        Page = page;
        _onDelete = onDelete;
        _original = page.Image.Clone();
        _thumbnail = WpfImage.ToThumbnail(page.Image);
        _preview = WpfImage.ToBitmapSource(page.Image);

        RotateLeftCommand = new RelayCommand(() => Rotate(-1));
        RotateRightCommand = new RelayCommand(() => Rotate(1));
        Rotate180Command = new RelayCommand(() => Rotate(2));
        DeleteCommand = new RelayCommand(() => _onDelete(this));
        AutoCropBoxCommand = new RelayCommand(SeedCropFromContent);
        ClearCropCommand = new RelayCommand(ClearCrop);
        ResetEditsCommand = new RelayCommand(ResetEdits);

        // Apply default edits (auto-levels) to the initial preview.
        RecomputeEdits();
    }

    public ScannedPage Page { get; }

    public double ImagePixelWidth => _original.Width;
    public double ImagePixelHeight => _original.Height;

    public BitmapSource Thumbnail
    {
        get => _thumbnail;
        private set => SetProperty(ref _thumbnail, value);
    }

    public BitmapSource Preview
    {
        get => _preview;
        private set => SetProperty(ref _preview, value);
    }

    public int Number
    {
        get => _number;
        set => SetProperty(ref _number, value);
    }

    // ---- Crop (non-destructive) ----
    public NormalizedRect CropRect
    {
        get => _cropRect;
        set => SetProperty(ref _cropRect, value);
    }

    public bool IsCropActive
    {
        get => _isCropActive;
        set => SetProperty(ref _isCropActive, value);
    }

    public NormalizedRect EffectiveCrop => _isCropActive ? _cropRect : NormalizedRect.Full;

    // ---- Geometry / tonal edits (reversible) ----
    public double RotationDegrees
    {
        get => _rotationDegrees;
        set { if (SetProperty(ref _rotationDegrees, value)) RecomputeEdits(); }
    }

    public int Brightness
    {
        get => _brightness;
        set { if (SetProperty(ref _brightness, value)) RecomputeEdits(); }
    }

    public int Contrast
    {
        get => _contrast;
        set { if (SetProperty(ref _contrast, value)) RecomputeEdits(); }
    }

    public bool RemoveDust
    {
        get => _removeDust;
        set { if (SetProperty(ref _removeDust, value)) RecomputeEdits(); }
    }

    public bool AutoLevel
    {
        get => _autoLevel;
        set { if (SetProperty(ref _autoLevel, value)) RecomputeEdits(); }
    }

    public bool DocumentCleanup
    {
        get => _documentCleanup;
        set { if (SetProperty(ref _documentCleanup, value)) RecomputeEdits(); }
    }

    public bool BlackAndWhite
    {
        get => _blackAndWhite;
        set { if (SetProperty(ref _blackAndWhite, value)) RecomputeEdits(); }
    }

    /// <summary>A clone of the pristine original (for saving into a project).</summary>
    public Image<Rgba32> CloneOriginal() => _original.Clone();

    public ICommand RotateLeftCommand { get; }
    public ICommand RotateRightCommand { get; }
    public ICommand Rotate180Command { get; }
    public ICommand DeleteCommand { get; }
    public ICommand AutoCropBoxCommand { get; }
    public ICommand ClearCropCommand { get; }
    public ICommand ResetEditsCommand { get; }

    /// <summary>Snapshot of the current edit state (for apply-to-all, undo, projects).</summary>
    public PageEdits GetEdits() => new()
    {
        Crop = _cropRect,
        CropActive = _isCropActive,
        RotationDegrees = _rotationDegrees,
        Brightness = _brightness,
        Contrast = _contrast,
        RemoveDust = _removeDust,
        AutoLevel = _autoLevel,
        DocumentCleanup = _documentCleanup,
        BlackAndWhite = _blackAndWhite
    };

    /// <summary>Applies an edit state and recomputes the preview.</summary>
    public void ApplyEdits(PageEdits e)
    {
        _cropRect = e.Crop;
        _isCropActive = e.CropActive;
        _rotationDegrees = e.RotationDegrees;
        _brightness = e.Brightness;
        _contrast = e.Contrast;
        _removeDust = e.RemoveDust;
        _autoLevel = e.AutoLevel;
        _documentCleanup = e.DocumentCleanup;
        _blackAndWhite = e.BlackAndWhite;
        foreach (var name in new[]
        {
            nameof(CropRect), nameof(IsCropActive), nameof(RotationDegrees),
            nameof(Brightness), nameof(Contrast), nameof(RemoveDust), nameof(AutoLevel),
            nameof(DocumentCleanup), nameof(BlackAndWhite)
        })
        {
            OnPropertyChanged(name);
        }
        RecomputeEdits();
    }

    /// <summary>Builds the final image for export: current edits with the crop applied.</summary>
    public Image<Rgba32> RenderForExport() => PageProcessor.CropNormalized(Page.Image, EffectiveCrop);

    private void Rotate(int quarterTurns)
    {
        int turns = ((quarterTurns % 4) + 4) % 4;
        if (turns == 0)
        {
            return;
        }
        PageProcessor.Rotate90(Page, turns);
        _original.Mutate(c => c.Rotate(90f * turns));
        OnPropertyChanged(nameof(ImagePixelWidth));
        OnPropertyChanged(nameof(ImagePixelHeight));
        RefreshFromPage();
    }

    private void SeedCropFromContent()
    {
        var rect = AutoCropProcessor.DetectContentRect(Page.Image);
        CropRect = rect is { } r
            ? NormalizedRect.FromPixels(r, Page.Width, Page.Height)
            : NormalizedRect.Full;
        IsCropActive = true;
    }

    private void ClearCrop()
    {
        CropRect = NormalizedRect.Full;
        IsCropActive = false;
    }

    private void ResetEdits()
    {
        _brightness = 0; _contrast = 0; _removeDust = false; _autoLevel = false; _rotationDegrees = 0;
        _documentCleanup = false; _blackAndWhite = false;
        foreach (var name in new[] { nameof(Brightness), nameof(Contrast), nameof(RemoveDust), nameof(AutoLevel), nameof(RotationDegrees), nameof(DocumentCleanup), nameof(BlackAndWhite) })
        {
            OnPropertyChanged(name);
        }
        ClearCrop();
        RecomputeEdits();
    }

    private void RecomputeEdits()
    {
        if (_recomputing)
        {
            _recomputePending = true;
            return;
        }
        _recomputing = true;

        int brightness = _brightness, contrast = _contrast;
        bool dust = _removeDust, auto = _autoLevel, clean = _documentCleanup, bw = _blackAndWhite;
        double rot = _rotationDegrees;

        Task.Run(() =>
        {
            var img = RotationOps.RotateKeepSize(_original, rot); // returns an independent image
            if (dust) DustRemovalProcessor.Remove(img, 60);
            if (auto) Adjustments.AutoLevels(img);
            Adjustments.Apply(img, brightness, contrast);
            if (clean) ScanApp.Core.Imaging.DocumentCleanup.Apply(img, bw);

            var thumb = WpfImage.ToThumbnail(img);
            var preview = WpfImage.ToBitmapSource(img);

            _dispatcher.Invoke(() =>
            {
                Page.ReplaceImage(img);
                Thumbnail = thumb;
                Preview = preview;
                _recomputing = false;
                if (_recomputePending)
                {
                    _recomputePending = false;
                    RecomputeEdits();
                }
            });
        });
    }

    private void RefreshFromPage()
    {
        Thumbnail = WpfImage.ToThumbnail(Page.Image);
        Preview = WpfImage.ToBitmapSource(Page.Image);
    }

    public void Dispose()
    {
        Page.Dispose();
        _original.Dispose();
    }
}
