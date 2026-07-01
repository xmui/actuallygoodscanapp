using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using ScanApp.App.Infrastructure;
using ScanApp.App.Scanning;
using ScanApp.Core.Export;
using ScanApp.Core.Imaging;
using ScanApp.Core.Models;
using ScanApp.Core.Naming;
using ScanApp.Core.Projects;
using ScanApp.Core.Settings;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ScanApp.App.ViewModels;

/// <summary>Root view model: drives the scan session, editing and saving.</summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly ScannerHub _hub = new();
    private readonly SettingsStore _store;
    private readonly AppSettings _settings;
    private readonly Dispatcher _dispatcher = Application.Current.Dispatcher;

    private ScanProfile _activeProfile;
    private ScannerDevice? _selectedDevice;
    private PageViewModel? _selectedPage;
    private CancellationTokenSource? _scanCts;
    private bool _isScanning;
    private bool _modeOverridden;
    private bool _noRealDevices;
    private string _statusText = "Ready.";
    private BitmapSource? _scanPreview;
    private bool _isScanPreviewVisible;
    private double _zoom = 1.0;

    public MainViewModel()
    {
        _store = new SettingsStore(AppContext.BaseDirectory);
        _settings = _store.Load();
        if (string.IsNullOrWhiteSpace(_settings.OutputDirectory))
        {
            _settings.OutputDirectory = _store.DefaultOutputDirectory;
        }
        _activeProfile = _settings.FlatbedProfile;
        ThemeManager.Apply(_settings);

        Pages.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasPages));
            RaiseCommandStates();
        };

        SetFlatbedCommand = new RelayCommand(() => SetModeManually(ScanMode.BulkFlatbed));
        SetSheetfedCommand = new RelayCommand(() => SetModeManually(ScanMode.Sheetfed));
        RefreshDevicesCommand = new RelayCommand(RefreshDevices);
        UseDemoCommand = new RelayCommand(UseDemo);
        ScanCommand = new RelayCommand(async () => await ScanAsync(), () => !IsScanning && SelectedDevice is not null);
        CancelScanCommand = new RelayCommand(() => _scanCts?.Cancel(), () => IsScanning);
        ClearAllCommand = new RelayCommand(ClearAll, () => HasPages && !IsScanning);
        SaveImagesCommand = new RelayCommand(SaveImages, () => HasPages && !IsScanning);
        SavePdfCommand = new RelayCommand(SavePdf, () => HasPages && !IsScanning);
        ChooseFolderCommand = new RelayCommand(ChooseFolder);
        OpenOutputFolderCommand = new RelayCommand(OpenOutputFolder);
        OpenImagesCommand = new RelayCommand(OpenImages);
        SetAccentCommand = new RelayCommand(p => SetAccent(p as string));
        ApplyEditsToAllCommand = new RelayCommand(ApplyEditsToAll, () => SelectedPage is not null && Pages.Count > 1);
        NextPageCommand = new RelayCommand(() => MoveSelection(1));
        PrevPageCommand = new RelayCommand(() => MoveSelection(-1));
        DeleteSelectedCommand = new RelayCommand(() => { if (SelectedPage is not null) RemovePage(SelectedPage); }, () => SelectedPage is not null);
        RotateSelectedRightCommand = new RelayCommand(() => SelectedPage?.RotateRightCommand.Execute(null));
        RotateSelectedLeftCommand = new RelayCommand(() => SelectedPage?.RotateLeftCommand.Execute(null));
        BatchRotateRightCommand = new RelayCommand(() => BatchRotate(1), () => SelectedPages.Count > 0);
        BatchDeleteCommand = new RelayCommand(BatchDelete, () => SelectedPages.Count > 0);
        SaveProjectCommand = new RelayCommand(SaveProject, () => HasPages);
        OpenProjectCommand = new RelayCommand(OpenProject);
        OpenRecentProjectCommand = new RelayCommand(p => OpenProjectFolder(p as string));
        ZoomInCommand = new RelayCommand(() => Zoom = Math.Min(8, Zoom + 0.25));
        ZoomOutCommand = new RelayCommand(() => Zoom = Math.Max(1, Zoom - 0.25));
        ZoomFitCommand = new RelayCommand(() => Zoom = 1.0);

        foreach (var p in _settings.RecentProjects)
        {
            RecentProjects.Add(p);
        }
        UpdateTemplateFromBuilder(); // keep the composed template in sync with the builder fields

        RefreshDevices();
        TryRestoreSession();
    }

    // ---- Mode ----
    public ScanMode CurrentMode => _activeProfile.Mode;
    public bool IsFlatbed => _activeProfile.Mode == ScanMode.BulkFlatbed;
    public bool IsSheetfed => _activeProfile.Mode == ScanMode.Sheetfed;
    public string ModeTitle => IsFlatbed ? "Bulk Flatbed" : "Sheetfed (ADF)";

    // ---- Devices ----
    public ObservableCollection<ScannerDevice> Devices { get; } = new();

    public bool NoRealDevices
    {
        get => _noRealDevices;
        private set => SetProperty(ref _noRealDevices, value);
    }

    public ScannerDevice? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
            {
                _settings.LastDeviceId = value?.Id;
                Save();
                RaiseCommandStates();
                if (value is not null && !_modeOverridden)
                {
                    AutoSelectMode(value);
                }
            }
        }
    }

    // ---- Pages ----
    public ObservableCollection<PageViewModel> Pages { get; } = new();
    public bool HasPages => Pages.Count > 0;

    public PageViewModel? SelectedPage
    {
        get => _selectedPage;
        set
        {
            if (SetProperty(ref _selectedPage, value))
            {
                (ApplyEditsToAllCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (DeleteSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set { if (SetProperty(ref _isScanning, value)) RaiseCommandStates(); }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    // ---- Progressive scan preview ----
    public BitmapSource? ScanPreviewImage
    {
        get => _scanPreview;
        private set => SetProperty(ref _scanPreview, value);
    }

    public bool IsScanPreviewVisible
    {
        get => _isScanPreviewVisible;
        private set => SetProperty(ref _isScanPreviewVisible, value);
    }

    // ---- Preview zoom ----
    public double Zoom
    {
        get => _zoom;
        set => SetProperty(ref _zoom, Math.Clamp(value, 1, 8));
    }

    // ---- Multi-select ----
    public ObservableCollection<PageViewModel> SelectedPages { get; } = new();

    public void SetSelectedPages(IEnumerable<PageViewModel> pages)
    {
        SelectedPages.Clear();
        foreach (var p in pages)
        {
            SelectedPages.Add(p);
        }
        (BatchRotateRightCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (BatchDeleteCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    // ---- Recent projects ----
    public ObservableCollection<string> RecentProjects { get; } = new();

    // ---- Friendly file-name builder ----
    public string FileNamePrefix
    {
        get => _settings.FileNamePrefix;
        set { _settings.FileNamePrefix = value; UpdateTemplateFromBuilder(); OnPropertyChanged(); }
    }

    public bool IncludeDateInName
    {
        get => _settings.IncludeDateInName;
        set { _settings.IncludeDateInName = value; UpdateTemplateFromBuilder(); OnPropertyChanged(); }
    }

    public bool IncludeCounterInName
    {
        get => _settings.IncludeCounterInName;
        set { _settings.IncludeCounterInName = value; UpdateTemplateFromBuilder(); OnPropertyChanged(); }
    }

    public int[] CounterDigitOptions { get; } = { 1, 2, 3, 4, 5 };

    public int CounterDigits
    {
        get => _settings.CounterDigits;
        set { _settings.CounterDigits = value; UpdateTemplateFromBuilder(); OnPropertyChanged(); }
    }

    public string FileNamePreview =>
        FileNameTemplate.Expand(_settings.FileNameTemplate, 1, DateTime.Now) + ImageFormat.Extension();

    // ---- Theme ----
    public bool ThemeIsLight
    {
        get => _settings.Theme == ThemeMode.Light;
        set
        {
            _settings.Theme = value ? ThemeMode.Light : ThemeMode.Dark;
            ThemeManager.Apply(_settings);
            OnPropertyChanged();
            Save();
        }
    }

    public IReadOnlyList<AccentSwatch> AccentSwatches { get; } = ThemeManager.Swatches;

    // ---- Scan options (bound to the active profile) ----
    public int[] DpiOptions { get; } = { 150, 200, 300, 400, 600 };

    public int Dpi
    {
        get => _activeProfile.Dpi;
        set { _activeProfile.Dpi = value; OnPropertyChanged(); Save(); }
    }

    public bool ColorScan
    {
        get => _activeProfile.Color;
        set { _activeProfile.Color = value; OnPropertyChanged(); Save(); }
    }

    public bool Duplex
    {
        get => _activeProfile.Duplex;
        set { _activeProfile.Duplex = value; OnPropertyChanged(); Save(); }
    }

    public bool AutoStraighten
    {
        get => _activeProfile.SoftwareDeskew;
        set { _activeProfile.SoftwareDeskew = value; OnPropertyChanged(); Save(); }
    }

    public bool SplitMultiplePhotos
    {
        get => _activeProfile.SplitMultiplePhotos;
        set { _activeProfile.SplitMultiplePhotos = value; OnPropertyChanged(); Save(); }
    }

    public CropMode[] CropModes { get; } = Enum.GetValues<CropMode>();

    public CropMode SelectedCropMode
    {
        get => _activeProfile.CropMode;
        set { _activeProfile.CropMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsFixedSize)); Save(); }
    }

    public bool IsFixedSize => _activeProfile.CropMode == CropMode.FixedSize;

    public FixedSizePreset[] FixedSizes { get; } = Enum.GetValues<FixedSizePreset>();

    public FixedSizePreset SelectedFixedSize
    {
        get => _activeProfile.FixedSize;
        set { _activeProfile.FixedSize = value; OnPropertyChanged(); Save(); }
    }

    // ---- Output options ----
    public OutputFormat[] ImageFormats { get; } = Enum.GetValues<OutputFormat>();

    public OutputFormat ImageFormat
    {
        get => _settings.ImageFormat;
        set { _settings.ImageFormat = value; OnPropertyChanged(); OnPropertyChanged(nameof(FileNamePreview)); Save(); }
    }

    public string OutputDirectory
    {
        get => _settings.OutputDirectory;
        set { _settings.OutputDirectory = value; OnPropertyChanged(); Save(); }
    }

    public string FileNameTemplateText
    {
        get => _settings.FileNameTemplate;
        set { _settings.FileNameTemplate = value; OnPropertyChanged(); Save(); }
    }

    // ---- Commands ----
    public ICommand SetFlatbedCommand { get; }
    public ICommand SetSheetfedCommand { get; }
    public ICommand RefreshDevicesCommand { get; }
    public ICommand UseDemoCommand { get; }
    public ICommand ScanCommand { get; }
    public ICommand CancelScanCommand { get; }
    public ICommand ClearAllCommand { get; }
    public ICommand SaveImagesCommand { get; }
    public ICommand SavePdfCommand { get; }
    public ICommand ChooseFolderCommand { get; }
    public ICommand OpenOutputFolderCommand { get; }
    public ICommand OpenImagesCommand { get; }
    public ICommand SetAccentCommand { get; }
    public ICommand ApplyEditsToAllCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand PrevPageCommand { get; }
    public ICommand DeleteSelectedCommand { get; }
    public ICommand RotateSelectedRightCommand { get; }
    public ICommand RotateSelectedLeftCommand { get; }
    public ICommand BatchRotateRightCommand { get; }
    public ICommand BatchDeleteCommand { get; }
    public ICommand SaveProjectCommand { get; }
    public ICommand OpenProjectCommand { get; }
    public ICommand OpenRecentProjectCommand { get; }
    public ICommand ZoomInCommand { get; }
    public ICommand ZoomOutCommand { get; }
    public ICommand ZoomFitCommand { get; }

    private void SetModeManually(ScanMode mode)
    {
        _modeOverridden = true;
        ApplyMode(mode);
    }

    private void ApplyMode(ScanMode mode)
    {
        _activeProfile = _settings.ProfileFor(mode);
        foreach (var name in new[]
        {
            nameof(CurrentMode), nameof(IsFlatbed), nameof(IsSheetfed), nameof(ModeTitle), nameof(Dpi),
            nameof(ColorScan), nameof(Duplex), nameof(AutoStraighten), nameof(SplitMultiplePhotos),
            nameof(SelectedCropMode), nameof(IsFixedSize), nameof(SelectedFixedSize)
        })
        {
            OnPropertyChanged(name);
        }
    }

    /// <summary>Picks the mode that fits the device: ADF/sheetfed scanners → Sheetfed, else Flatbed.</summary>
    private void AutoSelectMode(ScannerDevice device)
    {
        var hinted = InferModeFromName(device.Name);
        if (hinted is { } m)
        {
            ApplyMode(m);
            return;
        }

        if (device.Backend == "Demo")
        {
            ApplyMode(device.Id.Contains("adf", StringComparison.OrdinalIgnoreCase) ? ScanMode.Sheetfed : ScanMode.BulkFlatbed);
            return;
        }

        // Fall back to a capability probe off the UI thread.
        Task.Run(() =>
        {
            ScanMode mode = ScanMode.BulkFlatbed;
            try
            {
                var caps = _hub.QueryCapabilities(device);
                mode = caps.SupportsFeeder ? ScanMode.Sheetfed : ScanMode.BulkFlatbed;
            }
            catch
            {
                // keep default
            }
            _dispatcher.Invoke(() => { if (!_modeOverridden) ApplyMode(mode); });
        });
    }

    private static ScanMode? InferModeFromName(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("ff-680") || n.Contains("fastfoto") || n.Contains("adf") || n.Contains("sheet") || n.Contains("feeder"))
        {
            return ScanMode.Sheetfed;
        }
        if (n.Contains("opticbook") || n.Contains("a300") || n.Contains("flatbed") || n.Contains("perfection"))
        {
            return ScanMode.BulkFlatbed;
        }
        return null;
    }

    private void RefreshDevices()
    {
        Devices.Clear();
        var real = _hub.GetRealDevices();
        foreach (var d in real)
        {
            Devices.Add(d);
        }

        NoRealDevices = real.Count == 0;
        SelectedDevice = Devices.FirstOrDefault(d => d.Id == _settings.LastDeviceId) ?? Devices.FirstOrDefault();

        StatusText = NoRealDevices
            ? "No scanner detected. Connect one and press refresh, or use the demo scanner."
            : $"{ModeTitle} ready. Place your {(IsFlatbed ? "item on the glass" : "pages in the feeder")} and press Scan.";
    }

    private void UseDemo()
    {
        foreach (var d in _hub.GetDemoDevices())
        {
            Devices.Add(d);
        }
        NoRealDevices = false;
        SelectedDevice = Devices.LastOrDefault();
        StatusText = "Demo scanner added. Press Scan to try the workflow with no hardware.";
    }

    private async Task ScanAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        IsScanning = true;
        StatusText = "Scanning…";
        _scanCts = new CancellationTokenSource();
        var profile = _activeProfile.Clone();
        int before = Pages.Count;

        try
        {
            await _hub.ScanAsync(SelectedDevice, profile, raw => ProcessRawPage(raw, profile), OnScanPreview, _scanCts.Token);
            int added = Pages.Count - before;
            StatusText = added > 0
                ? $"Added {added} page{(added == 1 ? "" : "s")}. {Pages.Count} total."
                : "No pages scanned.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Scan failed: {ex.Message}";
        }
        finally
        {
            _scanCts.Dispose();
            _scanCts = null;
            IsScanning = false;
            IsScanPreviewVisible = false;
            ScanPreviewImage = null;
        }
    }

    /// <summary>Receives partial frames from the scanner (background thread) for the live preview.</summary>
    private void OnScanPreview(Image<Rgba32> frame)
    {
        try
        {
            var bmp = WpfImage.ToBitmapSource(frame); // frozen; safe to marshal
            _dispatcher.Invoke(() =>
            {
                ScanPreviewImage = bmp;
                IsScanPreviewVisible = true;
            });
        }
        finally
        {
            frame.Dispose(); // we own the frame
        }
    }

    /// <summary>Runs the post-processing pipeline (off the UI thread) then adds pages on the UI thread.</summary>
    private void ProcessRawPage(RawScan raw, ScanProfile profile)
    {
        var finished = PageProcessor.Process(raw.Image, profile, raw.DriverDid);
        foreach (var page in finished)
        {
            page.Dpi = raw.Dpi;
            var captured = page;
            _dispatcher.Invoke(() => AddPage(captured));
        }
    }

    private void AddPage(ScannedPage page)
    {
        var vm = new PageViewModel(page, RemovePage) { Number = Pages.Count + 1 };
        Pages.Add(vm);
        SelectedPage = vm;
        IsScanPreviewVisible = false; // finished page replaces the live preview
    }

    private void RemovePage(PageViewModel page)
    {
        int idx = Pages.IndexOf(page);
        Pages.Remove(page);
        page.Dispose();
        Renumber();
        SelectedPage = Pages.Count == 0 ? null : Pages[Math.Min(idx, Pages.Count - 1)];
    }

    private void Renumber()
    {
        for (int i = 0; i < Pages.Count; i++)
        {
            Pages[i].Number = i + 1;
        }
    }

    private void ApplyEditsToAll()
    {
        if (SelectedPage is null)
        {
            return;
        }
        var edits = SelectedPage.GetEdits();
        foreach (var p in Pages)
        {
            if (!ReferenceEquals(p, SelectedPage))
            {
                p.ApplyEdits(edits);
            }
        }
        StatusText = $"Applied edits to all {Pages.Count} pages.";
    }

    private void UpdateTemplateFromBuilder()
    {
        var parts = new List<string> { string.IsNullOrWhiteSpace(FileNamePrefix) ? "Scan" : FileNamePrefix.Trim() };
        if (IncludeDateInName)
        {
            parts.Add("{date}");
        }
        if (IncludeCounterInName)
        {
            int digits = Math.Clamp(CounterDigits, 1, 6);
            parts.Add("{counter:" + new string('0', digits) + "}");
        }
        else if (!IncludeDateInName)
        {
            parts.Add("{counter}"); // always keep something unique
        }
        _settings.FileNameTemplate = string.Join("_", parts);
        OnPropertyChanged(nameof(FileNamePreview));
        Save();
    }

    private void BatchRotate(int quarterTurns)
    {
        foreach (var p in SelectedPages.ToList())
        {
            for (int i = 0; i < ((quarterTurns % 4 + 4) % 4); i++)
            {
                p.RotateRightCommand.Execute(null);
            }
        }
        StatusText = $"Rotated {SelectedPages.Count} pages.";
    }

    private void BatchDelete()
    {
        foreach (var p in SelectedPages.ToList())
        {
            RemovePage(p);
        }
        SelectedPages.Clear();
        StatusText = "Deleted selected pages.";
    }

    // ---- Projects ----
    private void SaveProject()
    {
        if (!HasPages)
        {
            return;
        }
        var dialog = new OpenFolderDialog { Title = "Choose an (empty) folder for this project" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }
        try
        {
            var inputs = Pages.Select(p => new ProjectPageInput(p.CloneOriginal(), p.Page.Dpi, p.GetEdits())).ToList();
            try
            {
                ProjectStore.Save(dialog.FolderName, inputs);
            }
            finally
            {
                foreach (var i in inputs) i.Original.Dispose();
            }
            AddToRecent(dialog.FolderName);
            StatusText = $"Project saved to {dialog.FolderName}";
        }
        catch (Exception ex)
        {
            StatusText = $"Save project failed: {ex.Message}";
        }
    }

    private void OpenProject()
    {
        var dialog = new OpenFolderDialog { Title = "Open a project folder" };
        if (dialog.ShowDialog() == true)
        {
            OpenProjectFolder(dialog.FolderName);
        }
    }

    private void OpenProjectFolder(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !ProjectStore.IsProject(dir))
        {
            StatusText = "That folder isn't a project.";
            return;
        }
        try
        {
            var data = ProjectStore.Load(dir);
            LoadPages(data);
            AddToRecent(dir);
            StatusText = $"Opened project ({Pages.Count} pages) from {dir}";
        }
        catch (Exception ex)
        {
            StatusText = $"Open project failed: {ex.Message}";
        }
    }

    private void LoadPages(List<ProjectPageData> data)
    {
        ClearAll();
        foreach (var d in data)
        {
            var page = new ScannedPage(d.Image, d.Dpi);
            var vm = new PageViewModel(page, RemovePage, d.Edits) { Number = Pages.Count + 1 };
            Pages.Add(vm);
        }
        SelectedPage = Pages.FirstOrDefault();
    }

    private void AddToRecent(string dir)
    {
        RecentProjects.Remove(dir);
        RecentProjects.Insert(0, dir);
        while (RecentProjects.Count > 8)
        {
            RecentProjects.RemoveAt(RecentProjects.Count - 1);
        }
        _settings.RecentProjects = RecentProjects.ToList();
        Save();
    }

    private string SessionDir => Path.Combine(_store.DataDirectory, "Session");

    /// <summary>Persists the current pages+edits so the next launch restores them automatically.</summary>
    public void SaveSession()
    {
        try
        {
            if (!HasPages)
            {
                if (Directory.Exists(SessionDir)) Directory.Delete(SessionDir, recursive: true);
                return;
            }
            var inputs = Pages.Select(p => new ProjectPageInput(p.CloneOriginal(), p.Page.Dpi, p.GetEdits())).ToList();
            try
            {
                ProjectStore.Save(SessionDir, inputs);
            }
            finally
            {
                foreach (var i in inputs) i.Original.Dispose();
            }
        }
        catch
        {
            // auto-save is best-effort
        }
    }

    private void TryRestoreSession()
    {
        try
        {
            if (ProjectStore.IsProject(SessionDir))
            {
                LoadPages(ProjectStore.Load(SessionDir));
                if (HasPages)
                {
                    StatusText = $"Restored last session ({Pages.Count} pages).";
                }
            }
        }
        catch
        {
            // ignore a corrupt session
        }
    }

    private void MoveSelection(int delta)
    {
        if (Pages.Count == 0)
        {
            return;
        }
        int idx = SelectedPage is null ? 0 : Pages.IndexOf(SelectedPage);
        idx = Math.Clamp(idx + delta, 0, Pages.Count - 1);
        SelectedPage = Pages[idx];
    }

    private void ClearAll()
    {
        foreach (var p in Pages)
        {
            p.Dispose();
        }
        Pages.Clear();
        SelectedPage = null;
        StatusText = "Cleared.";
    }

    /// <summary>Imports existing image files as pages so prior scans can be cropped/edited/saved.</summary>
    private void OpenImages()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open images",
            Multiselect = true,
            Filter = "Images|*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.bmp|All files|*.*"
        };
        if (dialog.ShowDialog() == true)
        {
            ImportPaths(dialog.FileNames);
        }
    }

    private static readonly HashSet<string> ImportableExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp", ".webp", ".gif" };

    /// <summary>Imports image files (used by Open images and by drag-&-drop onto the window).</summary>
    public void ImportPaths(IEnumerable<string> paths)
    {
        int added = 0;
        foreach (var path in paths)
        {
            if (!ImportableExtensions.Contains(Path.GetExtension(path)))
            {
                continue;
            }
            try
            {
                var image = Image.Load<Rgba32>(path);
                int dpi = (int)Math.Round(image.Metadata.HorizontalResolution);
                if (dpi <= 0) dpi = 300;
                AddPage(new ScannedPage(image, dpi));
                added++;
            }
            catch (Exception ex)
            {
                StatusText = $"Couldn't open {Path.GetFileName(path)}: {ex.Message}";
            }
        }

        if (added > 0)
        {
            StatusText = $"Imported {added} image{(added == 1 ? "" : "s")}.";
        }
    }

    /// <summary>Moves a page to a new index (drag-to-reorder in the filmstrip).</summary>
    public void MovePage(PageViewModel page, int newIndex)
    {
        int oldIndex = Pages.IndexOf(page);
        if (oldIndex < 0)
        {
            return;
        }
        newIndex = Math.Clamp(newIndex, 0, Pages.Count - 1);
        if (newIndex == oldIndex)
        {
            return;
        }
        Pages.Move(oldIndex, newIndex);
        Renumber();
        SelectedPage = page;
    }

    private void SaveImages()
    {
        try
        {
            string dir = EnsureOutputDir();
            var now = DateTime.Now;
            string ext = ImageFormat.Extension();
            int counter = 1;
            foreach (var p in Pages)
            {
                string baseName = FileNameTemplate.Expand(FileNameTemplateText, counter, now);
                string path = FileNameTemplate.ResolveUniquePath(dir, baseName, ext, File.Exists);
                // Apply the per-page crop non-destructively at export time.
                using var export = new ScannedPage(p.RenderForExport(), p.Page.Dpi);
                ImageExporter.Save(export, path, ImageFormat, _settings.JpegQuality);
                counter++;
            }
            StatusText = $"Saved {Pages.Count} image{(Pages.Count == 1 ? "" : "s")} to {dir}";
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
        }
    }

    private void SavePdf()
    {
        try
        {
            string dir = EnsureOutputDir();
            string baseName = FileNameTemplate.Expand(FileNameTemplateText, 1, DateTime.Now);
            string path = FileNameTemplate.ResolveUniquePath(dir, baseName, ".pdf", File.Exists);
            var exportPages = Pages.Select(p => new ScannedPage(p.RenderForExport(), p.Page.Dpi)).ToList();
            try
            {
                PdfExporter.Save(exportPages, path, _settings.JpegQuality);
            }
            finally
            {
                foreach (var ep in exportPages) ep.Dispose();
            }
            StatusText = $"Saved PDF: {path}";
        }
        catch (Exception ex)
        {
            StatusText = $"PDF save failed: {ex.Message}";
        }
    }

    private string EnsureOutputDir()
    {
        string dir = string.IsNullOrWhiteSpace(OutputDirectory) ? _store.DefaultOutputDirectory : OutputDirectory;
        Directory.CreateDirectory(dir);
        return dir;
    }

    private void ChooseFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose where to save scans",
            InitialDirectory = Directory.Exists(OutputDirectory) ? OutputDirectory : _store.DefaultOutputDirectory
        };
        if (dialog.ShowDialog() == true)
        {
            OutputDirectory = dialog.FolderName;
        }
    }

    private void OpenOutputFolder()
    {
        try
        {
            string dir = EnsureOutputDir();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusText = $"Could not open folder: {ex.Message}";
        }
    }

    private void SetAccent(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return;
        }
        _settings.AccentColor = hex;
        ThemeManager.Apply(_settings);
        Save();
    }

    private void RaiseCommandStates()
    {
        (ScanCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CancelScanCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ClearAllCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SaveImagesCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SavePdfCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ApplyEditsToAllCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DeleteSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SaveProjectCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void Save()
    {
        try
        {
            _store.Save(_settings);
        }
        catch
        {
            // settings are best-effort; never block the UI on a save failure
        }
    }

    public void Dispose()
    {
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        foreach (var p in Pages)
        {
            p.Dispose();
        }
        _hub.Dispose();
    }
}
