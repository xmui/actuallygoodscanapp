Actually Good Scan — portable build (SELF-CONTAINED, no .NET needed)
===================================================================

HOW TO RUN
  1. Unzip this whole folder anywhere (or onto a USB stick).
  2. Double-click  ActuallyGoodScan.exe
  3. First launch: Windows SmartScreen may warn because the app isn't signed.
     Click "More info"  ->  "Run anyway".  (It's a normal unsigned app.)

  NOTHING to install — the .NET runtime is bundled inside this folder.
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
  - Plug in / power on the scanner, then click the refresh (button) next to
    the device list in the app.
  - TWAIN entries are preferred. If a scanner only appears under WIA, that works
    too (the app falls back automatically).
  - Bulk Flatbed = Plustek (place item on glass, press Scan, swap, repeat).
    Sheetfed (ADF) = Epson FF-680W (load the stack, press Scan once).
  - Press Enter or F5 to scan, Esc to cancel.

THERE IS NO INSTALLER
  To "uninstall", just delete this folder.
