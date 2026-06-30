# Actually Good Scan

A fast, intuitive Windows scanning app for flatbed and sheetfed scanners. Two modes, live previews
as you scan, automatic crop + straighten, and one-click saving to JPEG/PNG/TIFF or a multi-page PDF.

Built for and tested against:
- **Epson FastFoto FF-680W** (sheetfed; TWAIN + WIA via Epson Scan 2)
- **Plustek OpticBook A300 Plus** (A3 flatbed; TWAIN)

…and works with any TWAIN or WIA scanner.

## Features

- **Two modes**
  - **Bulk Flatbed** – scan items off the glass one at a time; swap and press **Scan** to keep
    accumulating pages. Optional *"split multiple photos on glass"* turns one scan of several photos
    into one cropped page each (FastFoto-style).
  - **Sheetfed (ADF)** – load a stack and scan every page in one pass, with optional duplex.
- **Live previews** – each page appears in the filmstrip the moment it's acquired; click any page
  for a large preview.
- **Hybrid auto-crop & auto-straighten** – uses the scanner driver's own auto border-detection /
  deskew when available, and falls back to built-in image processing when it isn't. Manual rotate
  (90° L/R) and per-page delete are always available.
- **Crop modes** – Auto-detect (default), Fixed size (A4 / Letter / Legal / 4×6 / 5×7), or Manual.
- **One-click saving** – JPEG / PNG / TIFF, or a single **multi-page PDF**. Auto file naming with
  `{date}`, `{time}`, `{counter}` tokens and collision-safe paths.
- **Fast & keyboard-friendly** – press **Enter** or **F5** to scan, **Esc** to cancel.
- **Portable** – run from a folder or USB stick with no installer; settings travel with the app.

## Project layout

```
src/
  ScanApp.Core/        Cross-platform (net8.0) — imaging, export, settings, naming. Unit-tested.
  ScanApp.App/         WPF app (net8.0-windows) — UI + TWAIN/WIA/Demo scanner backends.
  ScanApp.Core.Tests/  xUnit tests for the core pipeline (run on any OS).
build/
  publish-portable.ps1 Produces a self-contained portable build.
```

The imaging/export/naming logic lives in **ScanApp.Core** so it can be developed and unit-tested on
any platform. The Windows-only WPF UI and scanner drivers live in **ScanApp.App**.

## Build & run (Windows)

Requirements: **Windows 10/11**, **.NET 8 SDK** (with the Desktop/WPF workload).

```powershell
# from the repo root
dotnet build ActuallyGoodScanApp.sln -c Release
dotnet run --project src/ScanApp.App -c Release
```

On first launch you'll see the two mode buttons. With no scanner attached, a **Demo** device is
always available so you can try the full workflow (it synthesises skewed/multi-photo sample scans so
auto-crop, deskew and split are visible).

### Portable build

```powershell
pwsh build/publish-portable.ps1
# -> build/portable/ActuallyGoodScan.exe  (copy the whole folder to a USB stick)
```

The script drops a `portable.marker` next to the exe; when present, settings and a default `Scans`
output folder live alongside the app instead of in `%LOCALAPPDATA%`.

> **TWAIN note:** on 64-bit Windows the TWAIN backend needs the x64 `twaindsm.dll` next to the exe.
> NTwain ships it; if it's missing after publish, copy it from the NTwain package's
> `runtimes/win-x64` folder.

## Run the tests (any OS)

The core pipeline is fully testable without Windows or a scanner:

```bash
dotnet test src/ScanApp.Core.Tests
```

Tests cover auto-crop bounding boxes, deskew angle recovery, multi-photo splitting, file-name
templating/collision handling, and JPEG/PNG/TIFF + multi-page PDF export.

## How scanning works

1. A backend (**TWAIN** preferred → **WIA** fallback → **Demo**) acquires each page as a raw image
   and reports what the driver already did (crop/deskew).
2. `PageProcessor` runs only the software steps the driver didn't: content-aware auto-crop, deskew,
   and — in bulk-flatbed split mode — connected-component photo separation.
3. Finished pages stream into the filmstrip for live preview and editing.
4. Save exports the batch as images or a multi-page PDF, sized true-to-scan from the DPI.

## Notes & limitations

- TWAIN/WIA backends must be built and verified on Windows against real hardware. They are written
  defensively: any backend that fails to initialize is skipped, and the app still runs (Demo).
- Not in this version: OCR / searchable PDF, cloud upload, and an MSI/MSIX installer (portable +
  xcopy only). These are candidate follow-ups.
