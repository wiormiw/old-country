namespace OldCountry.Application.Realtime;

public enum AnnouncementKind
{
    /// <summary>A YouTube download finished. The track now plays from the library.</summary>
    Downloaded,

    /// <summary>A YouTube download stopped without a track (refused or failed).</summary>
    DownloadFailed,

    /// <summary>Tracks arrived in a playlist from its YouTube Music playlist.</summary>
    NewFromYouTube,
}
