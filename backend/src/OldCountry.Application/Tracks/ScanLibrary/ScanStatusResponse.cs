namespace OldCountry.Application.Tracks.ScanLibrary;

/// <summary>Whether a scan runs, and the result of the last scan.</summary>
/// <param name="IsRunning">A scan runs now.</param>
/// <param name="IsQueued">A scan waits to start.</param>
/// <param name="LastScan">The last finished scan since the server started, if there is one.</param>
public sealed record ScanStatusResponse(bool IsRunning, bool IsQueued, ScanReport? LastScan);
