namespace ScanApp.App.Scanning;

/// <summary>A scanner the app can talk to, abstracted over the underlying driver (TWAIN/WIA/Mock).</summary>
public sealed class ScannerDevice
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Which backend exposed this device, shown to the user for clarity.</summary>
    public required string Backend { get; init; }

    public override string ToString() => Name;
}
