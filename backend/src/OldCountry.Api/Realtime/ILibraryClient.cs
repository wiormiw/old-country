using OldCountry.Application.Realtime;

namespace OldCountry.Api.Realtime;

/// <summary>The methods that the server can call on a connected app. They are strongly typed, so a typo is a compile error.</summary>
public interface ILibraryClient
{
    /// <summary>Something changed. Get again the data that shows it.</summary>
    Task Changed(LibraryChanges changes);

    /// <summary>News to show to the user, for example a finished download or new tracks from YouTube.</summary>
    Task Announced(IReadOnlyList<Announcement> news);
}
