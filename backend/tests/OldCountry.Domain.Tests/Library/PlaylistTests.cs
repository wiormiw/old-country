using OldCountry.Domain.Common;
using OldCountry.Domain.Errors;
using OldCountry.Domain.Library;

namespace OldCountry.Domain.Tests.Library;

public sealed class PlaylistTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);
    private static readonly TrackId A = TrackId.New(), B = TrackId.New(), C = TrackId.New();

    private static Playlist NewPlaylist(params TrackId[] tracks)
    {
        var playlist = Playlist.Create("Road trip", null).Value.ShouldBeOfType<Playlist>();
        playlist.Append(tracks, Now).ShouldBeNull();
        return playlist;
    }

    private static TrackId[] Order(Playlist playlist) => [.. playlist.Entries.Select(entry => entry.TrackId)];

    [Fact]
    public void A_new_playlist_is_empty_with_a_trimmed_name_and_no_blank_description()
    {
        var playlist = Playlist.Create("  Road trip ", "   ").Value.ShouldBeOfType<Playlist>();

        playlist.Name.ShouldBe("Road trip");
        playlist.Description.ShouldBeNull();
        playlist.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void A_blank_name_is_refused() =>
        Playlist.Create(" ", null).Value.ShouldBeOfType<ValidationError>().Errors.ShouldContainKey("name");

    [Fact]
    public void Tracks_append_in_order_and_duplicates_are_kept()
    {
        var playlist = NewPlaylist(A, B, A);

        Order(playlist).ShouldBe([A, B, A]);
        playlist.Entries.ShouldAllBe(entry => entry.AddedAt == Now);
    }

    [Fact]
    public void Appending_past_the_limit_refuses_the_whole_batch()
    {
        var playlist = NewPlaylist(A);

        var error = playlist.Append([.. Enumerable.Repeat(B, Playlist.MaxEntries)], Now);

        error.ShouldNotBeNull().Errors.ShouldContainKey("trackIds");
        Order(playlist).ShouldBe([A]);
    }

    [Fact]
    public void Removing_takes_out_only_the_entry_at_that_position()
    {
        var playlist = NewPlaylist(A, B, A);

        playlist.RemoveAt(2).ShouldBeTrue();

        Order(playlist).ShouldBe([A, B]);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void Removing_a_position_that_does_not_exist_changes_nothing(int position)
    {
        var playlist = NewPlaylist(A, B, C);

        playlist.RemoveAt(position).ShouldBeFalse();

        Order(playlist).ShouldBe([A, B, C]);
    }

    [Fact]
    public void Moving_down_shifts_the_entries_in_between_up() =>
        Moved(from: 0, to: 2).ShouldBe([B, C, A]);

    [Fact]
    public void Moving_up_shifts_the_entries_in_between_down() =>
        Moved(from: 2, to: 0).ShouldBe([C, A, B]);

    [Fact]
    public void Moving_out_of_range_changes_nothing()
    {
        var playlist = NewPlaylist(A, B, C);

        playlist.Move(0, 3).ShouldBeFalse();

        Order(playlist).ShouldBe([A, B, C]);
    }

    [Fact]
    public void Renaming_validates_like_creating()
    {
        var playlist = NewPlaylist();

        playlist.Rename("", null).ShouldNotBeNull();
        playlist.Rename("Night drive", " quiet ones ").ShouldBeNull();

        playlist.Name.ShouldBe("Night drive");
        playlist.Description.ShouldBe("quiet ones");
    }

    private static TrackId[] Moved(int from, int to)
    {
        var playlist = NewPlaylist(A, B, C);
        playlist.Move(from, to).ShouldBeTrue();
        return Order(playlist);
    }
}
