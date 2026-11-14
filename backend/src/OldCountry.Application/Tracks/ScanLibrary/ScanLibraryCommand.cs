using OldCountry.Application.Abstractions.Messaging;

namespace OldCountry.Application.Tracks.ScanLibrary;

/// <summary>
/// Reads the library folders and adds each new audio file as a track.
/// A background loop runs it. It saves in batches, so it does not use one transaction.
/// </summary>
[NoTransaction]
public sealed record ScanLibraryCommand(ScanTrigger Trigger) : ICommand<ScanLibraryResult>;
