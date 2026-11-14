namespace OldCountry.Application.Downloads.ProcessNextDownload;

/// <summary>One job was processed, or the queue had no job to take.</summary>
public readonly union ProcessNextDownloadResult(DownloadProcessed, NoDownloadQueued);
