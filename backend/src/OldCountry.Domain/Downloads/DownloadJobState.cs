namespace OldCountry.Domain.Downloads;

/// <summary>The stage of a download job. The database keeps the name, so do not rename the values.</summary>
public enum DownloadJobState
{
    Queued,
    Running,
    Completed,
    Failed,
}
