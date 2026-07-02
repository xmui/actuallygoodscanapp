using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ScanApp.App.Infrastructure;
using ScanApp.Core.Imaging;
using ScanApp.Core.Models;
using ScanApp.Services.Editing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ScanApp.App.ViewModels;

/// <summary>
/// A single page in the session. Edits are non-destructive: a pristine original is retained and the
/// working preview is produced by the debounced, cancellable <see cref="EditPipeline"/>. Every edit
/// records into the app-wide <see cref="UndoService"/> (slider drags coalesce into one step).
/// </summary>
public sealed class PageViewModel : ObservableObject, IDisposable
{
    private readonly Action<PageViewModel> _onDelete;
    private readonly UndoService? _undo;
    private readonly Dispatcher _dispatcher = Application.Current.Dispatcher;
    private readonly Image<Rgba32> _original;
    private readonly EditPipeline _pipeline;

    private BitmapSource _thumbnail;
    private BitmapSource _preview;
    private int _number;

    private NormalizedRect _cropRect = PageEdits.Defaults.Crop;
    private bool _isCropActive = PageEdits.Defaults.CropActive;
    private double _rotationDegrees;
    private int _brightness;
    private int _contrast;
    private bool _removeDust;
    private bool _autoLevel = PageEdits.Defaults.AutoLevel;
    private bool _documentCleanup;
    private bool _blackAndWhite;

    public PageViewModel(ScannedPage page, Action<PageViewModel> onDelete, UndoService? undo = null, PageEdits? initialEdits = null)
    {
        Page = page;
        _onDelete = onDelete;
        _undo = undo;
        _original = page.Image.Clone();
        _thumbnail = WpfImage.ToThumbnail(page.Image);
        _preview = WpfImage.ToBitmapSource(page.Image);
        _pipeline = new EditPipeline(_original, OnRendered);

        RotateLeftCommand = new RelayCommand(() => RotateWithUndo(-1));
        RotateRightCommand = new RelayCommand(() => RotateWithUndo(1));
        Rotate180Command = new RelayCommand(() => RotateWithUndo(2));
        DeleteCommand = new RelayCommand(() => _onDelete(this));
        AutoCropBoxCommand = new RelayCommand(SeedCropFromContent);
        ClearCropCommand = new RelayCommand(() => ChangeEdits(GetEdits() with { Crop = NormalizedRect.Full, CropActive = false }, "Clear crop"));
        ResetEditsCommand = new RelayCommand(() => ChangeEdits(new PageEdits(), "Reset edits"));

        if (initialEdits is not null)
        {
            ApplyEdits(initialEdits);
        }
        else
        {
            _pipeline.Request(GetEdits()); // bake in the on-by-default auto-levels
        }
    }

    public ScannedPage Page { get; }
    public double ImagePixelWidth => _original.Width;
    public double ImagePixelHeight => _original.Height;

    public BitmapSource Thumbnail { get => _thumbnail; private set => SetProperty(ref _thumbnail, value); }
    public BitmapSource Preview { get => _preview; private set => SetProperty(ref _preview, value); }
    public int Number { get => _number; set => SetProperty(ref _number, value); }

    // ---- Edit state (each setter records undo + requests a debounced re-render) ----
    public NormalizedRect CropRect
    {
        get => _cropRect;
        set => EditSetter(ref _cropRect, value, nameof(CropRect), rerender: false);
    }

    public bool IsCropActive
    {
        get => _isCropActive;
        set => EditSetter(ref _isCropActive, value, nameof(IsCropActive), rerender: false);
    }

    public double RotationDegrees
    {
        get => _rotationDegrees;
        set => EditSetter(ref _rotationDegrees, value, nameof(RotationDegrees));
    }

    public int Brightness
    {
        get => _brightness;
        set => EditSetter(ref _brightness, value, nameof(Brightness));
    }

    public int Contrast
    {
        get => _contrast;
        set => EditSetter(ref _contrast, value, nameof(Contrast));
    }

    public bool RemoveDust
    {
        get => _removeDust;
        set => EditSetter(ref _removeDust, value, nameof(RemoveDust));
    }

    public bool AutoLevel
    {
        get => _autoLevel;
        set => EditSetter(ref _autoLevel, value, nameof(AutoLevel));
    }

    public bool DocumentCleanup
    {
        get => _documentCleanup;
        set => EditSetter(ref _documentCleanup, value, nameof(DocumentCleanup));
    }

    public bool BlackAndWhite
    {
        get => _blackAndWhite;
        set => EditSetter(ref _blackAndWhite, value, nameof(BlackAndWhite));
    }

    public ICommand RotateLeftCommand { get; }
    public ICommand RotateRightCommand { get; }
    public ICommand Rotate180Command { get; }
    public ICommand DeleteCommand { get; }
    public ICommand AutoCropBoxCommand { get; }
    public ICommand ClearCropCommand { get; }
    public ICommand ResetEditsCommand { get; }

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

    /// <summary>Applies a full edit state without recording undo (used by undo/redo/projects/apply-all).</summary>
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
        RaiseAllEditProperties();
        _pipeline.Request(e);
    }

    public Image<Rgba32> CloneOriginal() => _original.Clone();

    /// <summary>Final export render (edits + crop), from the pristine original via the canonical chain.</summary>
    public Image<Rgba32> RenderForExport() => EditRenderer.RenderForExport(_original, GetEdits());

    // ---- internals ----

    /// <summary>Shared edit-property setter: record undo (coalesced), notify, re-render.</summary>
    private void EditSetter<T>(ref T field, T value, string name, bool rerender = true)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        var before = GetEdits();
        field = value;
        var after = GetEdits();
        OnPropertyChanged(name);

        _undo?.Push(new UndoableAction(
            $"Change {name}",
            Undo: () => _dispatcher.Invoke(() => ApplyEdits(before)),
            Redo: () => _dispatcher.Invoke(() => ApplyEdits(after)),
            CoalesceKey: $"{Page.Id}:{name}"));

        if (rerender)
        {
            _pipeline.Request(after);
        }
    }

    /// <summary>Applies a whole new edit state as one undoable step (reset / clear crop / auto box).</summary>
    private void ChangeEdits(PageEdits after, string label)
    {
        var before = GetEdits();
        _undo?.Push(new UndoableAction(
            label,
            Undo: () => _dispatcher.Invoke(() => ApplyEdits(before)),
            Redo: () => _dispatcher.Invoke(() => ApplyEdits(after))));
        ApplyEdits(after);
    }

    private void RotateWithUndo(int quarterTurns)
    {
        Rotate(quarterTurns);
        _undo?.Push(new UndoableAction(
            "Rotate 90°",
            Undo: () => _dispatcher.Invoke(() => Rotate(-quarterTurns)),
            Redo: () => _dispatcher.Invoke(() => Rotate(quarterTurns))));
    }

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
        _pipeline.Request(GetEdits()); // re-render edits on the rotated original
        Thumbnail = WpfImage.ToThumbnail(Page.Image);
        Preview = WpfImage.ToBitmapSource(Page.Image);
    }

    private void SeedCropFromContent()
    {
        var rect = AutoCropProcessor.DetectContentRect(Page.Image);
        var crop = rect is { } r
            ? NormalizedRect.FromPixels(r, Page.Width, Page.Height)
            : NormalizedRect.Full;
        ChangeEdits(GetEdits() with { Crop = crop, CropActive = true }, "Auto crop box");
    }

    /// <summary>Pipeline completion (thread-pool thread): convert, then hand off to the UI thread.</summary>
    private void OnRendered(Image<Rgba32> rendered, PageEdits _)
    {
        var thumb = WpfImage.ToThumbnail(rendered);
        var preview = WpfImage.ToBitmapSource(rendered);
        _dispatcher.BeginInvoke(() =>
        {
            Page.ReplaceImage(rendered);
            Thumbnail = thumb;
            Preview = preview;
        });
    }

    private void RaiseAllEditProperties()
    {
        foreach (var name in new[]
        {
            nameof(CropRect), nameof(IsCropActive), nameof(RotationDegrees), nameof(Brightness),
            nameof(Contrast), nameof(RemoveDust), nameof(AutoLevel), nameof(DocumentCleanup), nameof(BlackAndWhite)
        })
        {
            OnPropertyChanged(name);
        }
    }

    public void Dispose()
    {
        _pipeline.Dispose();
        Page.Dispose();
        _original.Dispose();
    }
}
