namespace ScanApp.Core.Models;

/// <summary>
/// The two top-level scanning workflows the app supports.
/// </summary>
public enum ScanMode
{
    /// <summary>
    /// Bulk flatbed: items are placed on the glass and scanned one at a time. The session
    /// stays open so the user can swap the next item and scan again, accumulating pages.
    /// Optionally one scan can be split into multiple photos.
    /// </summary>
    BulkFlatbed,

    /// <summary>
    /// Sheetfed / ADF: pages are streamed continuously from the automatic document feeder.
    /// </summary>
    Sheetfed
}
