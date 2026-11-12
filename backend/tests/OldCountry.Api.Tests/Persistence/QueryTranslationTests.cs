using Microsoft.EntityFrameworkCore;
using OldCountry.Api.Persistence;
using OldCountry.Domain.Common;
using OldCountry.Domain.Library;

namespace OldCountry.Api.Tests.Persistence;

/// <summary>
/// The value-object queries the slices rely on translate to SQL. ToQueryString needs no
/// database, so these run without Docker.
/// </summary>
public sealed class QueryTranslationTests : IDisposable
{
    private readonly OldCountryDbContext _db = new(new DbContextOptionsBuilder<OldCountryDbContext>().UseNpgsql("Host=unused").Options);

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Known_local_paths_read_the_complex_type_column()
    {
        var sql = _db.Tracks.Where(t => t.LocalFile != null).Select(t => t.LocalFile!.Path).ToQueryString();

        sql.ShouldContain("local_file_path");
        sql.ShouldContain("FROM tracks");
    }

    [Fact]
    public void Duplicate_file_check_filters_on_the_complex_type_column()
    {
        const string path = "/srv/music/a.flac";

        _db.Tracks.Where(t => t.LocalFile!.Path == path).ToQueryString().ShouldContain("t.local_file_path = ");
    }

    [Fact]
    public void A_playlist_loads_its_entries_from_the_jsonb_column()
    {
        var id = PlaylistId.New();

        _db.Playlists.Where(p => p.Id == id).ToQueryString().ShouldContain("p.entries");
    }

    [Fact]
    public void Looking_up_many_tracks_by_strongly_typed_id_translates_to_one_query()
    {
        List<TrackId> ids = [TrackId.New(), TrackId.New()];

        var sql = _db.Tracks.Where(t => ids.Contains(t.Id)).Select(t => t.Id).ToQueryString();

        sql.ShouldContain("t.id = ANY");
    }

    [Fact]
    public void Duplicate_video_check_compares_the_converted_id_column()
    {
        var video = new YouTubeVideoId("dQw4w9WgXcQ");

        _db.Tracks.Where(t => t.YouTubeVideoId == video).ToQueryString().ShouldContain("t.youtube_video_id = ");
    }
}
