namespace OldCountry.Application.Realtime;

/// <summary>The next message of the broadcaster: change notices and news, merged as they arrive.</summary>
public sealed record Update(LibraryChanges Changes, IReadOnlyList<Announcement> News)
{
    public bool IsEmpty => Changes.IsEmpty && News.Count == 0;

    public Update Merge(Update other) => new(Changes.Merge(other.Changes), [.. News, .. other.News]);
}
