using OldCountry.Application.Abstractions.Messaging;

namespace OldCountry.Application.Downloads.ProcessNextDownload;

/// <summary>
/// Takes the next queued download and runs it. The download worker sends this command in a loop.
/// A download can take minutes, so the command does not hold a transaction.
/// </summary>
[NoTransaction]
public sealed record ProcessNextDownloadCommand : ICommand<ProcessNextDownloadResult>;
