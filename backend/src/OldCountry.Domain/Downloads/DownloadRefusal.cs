namespace OldCountry.Domain.Downloads;

/// <summary>The reason that YouTube did not give the audio of a video.</summary>
public enum DownloadRefusal
{
    /// <summary>Only Music Premium members can play it. It works again with Premium cookies.</summary>
    PremiumOnly,
    /// <summary>The uploader made it private.</summary>
    Private,
    /// <summary>The video was removed, or it never existed.</summary>
    Unavailable,
    /// <summary>The video needs an account that is signed in and has a verified age.</summary>
    AgeRestricted,
    /// <summary>The video is not available in the country of the server.</summary>
    RegionBlocked,
    /// <summary>All other failures. A later attempt can work.</summary>
    Failed,
}
