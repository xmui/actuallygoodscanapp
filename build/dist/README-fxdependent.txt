Actually Good Scanning App — portable build (FRAMEWORK-DEPENDENT, tiny)
==============================================================

THIS BUILD NEEDS THE .NET 8 DESKTOP RUNTIME (one-time, free)
  This zip is small because it does NOT bundle the runtime. Install the
  ".NET Desktop Runtime 8.x" (x64) once on this PC, then the app runs:
      https://dotnet.microsoft.com/download/dotnet/8.0
  (Pick "Desktop Runtime", x64. You do NOT need the SDK.)

  Prefer zero install? Use the "selfcontained" zip instead — it bundles the
  runtime and needs nothing.

HOW TO RUN
  1. Install the .NET 8 Desktop Runtime (link above) if you don't have it.
  2. Unzip this whole folder anywhere (or onto a USB stick).
  3. Double-click  ActuallyGoodScan.exe
  4. First launch: Windows SmartScreen may warn because the app isn't signed.
     Click "More info"  ->  "Run anyway".

  This is "portable": your settings and a default "Scans" output folder are
  created right here next to the exe (because of the portable.marker file).

TRY IT WITH NO SCANNER
  Pick the "Demo" device to walk through the whole flow — scan, auto-crop,
  deskew, split multiple photos, preview, and save JPEG / multi-page PDF.

USING YOUR SCANNERS (Epson FF-680W, Plustek A300 Plus)
  - Install the scanner's own driver first if you haven't:
      Epson FF-680W  -> Epson Scan 2 (Epson "Drivers and Utilities" package)
      Plustek A300+  -> Plustek OpticBook driver
    These also install the TWAIN data-source manager the app uses.
  - Plug in / power on the scanner, then click the refresh button next to the
    device list in the app.
  - TWAIN entries are preferred; WIA works as a fallback automatically.
  - Bulk Flatbed = Plustek (place item on glass, press Scan, swap, repeat).
    Sheetfed (ADF) = Epson FF-680W (load the stack, press Scan once).
  - Press Enter or F5 to scan, Esc to cancel.

THERE IS NO INSTALLER
  To "uninstall", just delete this folder.
