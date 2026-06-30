using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using ScanApp.App.Infrastructure;
using ScanApp.App.Scanning;
using ScanApp.Core.Export;
using ScanApp.Core.Imaging;
using ScanApp.Core.Models;
using ScanApp.Core.Naming;
using ScanApp.Core.Settings;

namespace ScanApp.App.ViewModels;

/// <summary>Root view model: drives the start screen, the scan session, and saving.</summary>
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
    private bool _onStartScreen = true;
    private bool _isScanning;
    private string _statusText = "Ready.";

    public MainViewModel()
    {
        _store = new SettingsStore(AppContext.BaseDirectory);
        _settings = _store.Load();
        if (string.IsNullOrWhiteSpace(_settings.OutputDirectory))
        {
            _settings.OutputDirectory = _store.DefaultOutputDirectory;
        }
        _activeProfile = _settings.FlatbedProfile;

        Pages.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasPages));
            RaiseCommandStates();
        };

        StartFlatbedCommand = new RelayCommand(() => StartMode(ScanMode.BulkFlatbed));
        StartSheetfedCommand = new RelayCommand(() => StartMode(ScanMode.Sheetfed));
        BackCommand = new RelayCommand(() => OnStartScreen = true);
        RefreshDevicesCommand = new RelayCommand(RefreshDevices);
        ScanCommand = new RelayCommand(async () => await ScanAsync(), () => !IsScanning && SelectedDevice is not null);
        CancelScanCommand = new RelayCommand(() => _scanCts?.Cancel(), () => IsScanning);
        ClearAllCommand = new RelayCommand(ClearAll, () => HasPages && !IsScanning);
        SaveImagesCommand = new RelayCommand(SaveImages, () => HasPages && !IsScanning);
        SavePdfCommand = new RelayCommand(SavePdf, () => HasPages && !IsScanning);
        ChooseFolderCommand = new RelayCommand(ChooseFolder);
        OpenOutputFolderCommand = new RelayCommand(OpenOutputFolder);
    }

    // ---- Navigation state ----
    public bool OnStartScreen
    {
        get => _onStartScreen;
        set { if (SetProperty(ref _onStartScreen, value)) OnPropertyChanged(nameof(OnScanScreen)); }
    }

    public bool OnScanScreen => !_onStartScreen;

    public ScanMode CurrentMode => _activeProfile.Mode;
    public bool IsFlatbed => _activeProfile.Mode == ScanMode.BulkFlatbed;
    public string ModeTitle => IsFlatbed ? "Bulk Flatbed" : "Sheetfed (ADF)";

    // ---- Devices ----
    public ObservableCollection<ScannerDevice> Devices { get; } = new();

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
            }
        }
    }

    // ---- Pages ----
    public ObservableCollection<PageViewModel> Pages { get; } = new();
    public bool HasPages => Pages.Count > 0;

    public PageViewModel? SelectedPage
    {
        get => _selectedPage;
        set => SetProperty(ref _selectedPage, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

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
        set { _settings.ImageFormat = value; OnPropertyChanged(); Save(); }
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
    public ICommand StartFlatbedCommand { get; }
    public ICommand StartSheetfedCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand RefreshDevicesCommand { get; }
    public ICommand ScanCommand { get; }
    public ICommand CancelScanCommand { get; }
    public ICommand ClearAllCommand { get; }
    public ICommand SaveImagesCommand { get; }
    public ICommand SavePdfCommand { get; }
    public ICommand ChooseFolderCommand { get; }
    public ICommand OpenOutputFolderCommand { get; }

    private void StartMode(ScanMode mode)
    {
        _activeProfile = _settings.ProfileFor(mode);
        OnStartScreen = false;
        RefreshDevices();
        // Refresh all profile-bound properties for the newly active profile.
        foreach (var name in new[]
        {
            nameof(CurrentMode), nameof(IsFlatbed), nameof(ModeTitle), nameof(Dpi), nameof(ColorScan),
            nameof(Duplex), nameof(AutoStraighten), nameof(SplitMultiplePhotos), nameof(SelectedCropMode),
            nameof(IsFixedSize), nameof(SelectedFixedSize)
        })
        {
            OnPropertyChanged(name);
        }
        StatusText = $"{ModeTitle} ready. Place your {(IsFlatbed ? "item on the glass" : "pages in the feeder")} and press Scan.";
    }

    private void RefreshDevices()
    {
        Devices.Clear();
        foreach (var d in _hub.GetDevices())
        {
            Devices.Add(d);
        }

        SelectedDevice = Devices.FirstOrDefault(d => d.Id == _settings.LastDeviceId)
            ?? Devices.FirstOrDefault();

        if (Devices.Count == 0)
        {
            StatusText = "No scanners found. Connect a scanner or use the Demo device.";
        }
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
            await _hub.ScanAsync(SelectedDevice, profile, raw => ProcessRawPage(raw, profile), _scanCts.Token);
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
        }
    }

    /// <summary>Runs the post-processing pipeline (off the UI thread) then adds pages on the UI thread.</summary>
    private void ProcessRawPage(RawScan raw, ScanProfile profile)
    {
        // Called from the scanning background thread; do the heavy imaging here.
        var finished = PageProcessor.Process(raw.Image, profile, raw.DriverDid);
        foreach (var page in finished)
        {
            page.Dpi = raw.Dpi;
            var captured = page;
            _dispatcher.Invoke(() => AddPage(captured));
        }
    }

    private void AddPage(ScanApp.Core.Models.ScannedPage page)
    {
        var vm = new PageViewModel(page, RemovePage) { Number = Pages.Count + 1 };
        Pages.Add(vm);
        SelectedPage = vm; // live preview jumps to the newest page
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
                ImageExporter.Save(p.Page, path, ImageFormat, _settings.JpegQuality);
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
            PdfExporter.Save(Pages.Select(p => p.Page).ToList(), path, _settings.JpegQuality);
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

    private void RaiseCommandStates()
    {
        (ScanCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CancelScanCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ClearAllCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SaveImagesCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SavePdfCommand as RelayCommand)?.RaiseCanExecuteChanged();
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
