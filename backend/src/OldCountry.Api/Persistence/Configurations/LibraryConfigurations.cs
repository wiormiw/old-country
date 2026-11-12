using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OldCountry.Domain.Library;

namespace OldCountry.Api.Persistence.Configurations;

internal sealed class ArtistConfiguration : IEntityTypeConfiguration<Artist>
{
    public void Configure(EntityTypeBuilder<Artist> builder)
    {
        builder.HasKey(artist => artist.Id);
        builder.Property(artist => artist.Id).ValueGeneratedNever();
        builder.Property(artist => artist.Name).HasMaxLength(Artist.NameMaxLength);
        builder.Property(artist => artist.NormalizedName).HasMaxLength(Artist.NameMaxLength);
        builder.HasIndex(artist => artist.NormalizedName).IsUnique();
    }
}

internal sealed class PlaylistConfiguration : IEntityTypeConfiguration<Playlist>
{
    public void Configure(EntityTypeBuilder<Playlist> builder)
    {
        builder.HasKey(playlist => playlist.Id);
        builder.Property(playlist => playlist.Id).ValueGeneratedNever();
        builder.Property(playlist => playlist.Name).HasMaxLength(Playlist.NameMaxLength);
        builder.Property(playlist => playlist.Description).HasMaxLength(Playlist.DescriptionMaxLength);

        // One library playlist per YouTube playlist: importing it again finds and syncs this one.
        builder.Property(playlist => playlist.YouTubeListId).HasMaxLength(Playlist.YouTubeListIdMaxLength);
        builder.HasIndex(playlist => playlist.YouTubeListId).IsUnique();
        builder.PrimitiveCollection<List<string>>("_youTubeVideosTaken").HasColumnName("you_tube_videos_taken");

        // A collection of records stored as one jsonb array on the playlist row: the order is the
        // array order, and a reorder rewrites one column instead of renumbering rows.
        // EF needs an IList, so it maps the private List<PlaylistEntry> field; the domain keeps
        // exposing it read-only.
        builder.Ignore(playlist => playlist.Entries);
        builder.ComplexCollection<List<PlaylistEntry>, PlaylistEntry>("_entries", entries =>
        {
            entries.ToJson("entries");
            entries.Property(entry => entry.TrackId).HasJsonPropertyName("trackId");
            entries.Property(entry => entry.AddedAt).HasJsonPropertyName("addedAt");
        });
    }
}

internal sealed class TrackConfiguration : IEntityTypeConfiguration<Track>
{
    public void Configure(EntityTypeBuilder<Track> builder)
    {
        builder.HasKey(track => track.Id);
        builder.Property(track => track.Id).ValueGeneratedNever();
        builder.Property(track => track.Title).HasMaxLength(Track.TitleMaxLength);
        builder.Ignore(track => track.IsAvailableOffline);

        builder.HasOne<Artist>().WithMany().HasForeignKey(track => track.ArtistId).OnDelete(DeleteBehavior.Restrict);

        // One library entry per YouTube video. Postgres treats NULLs as distinct, so local-only
        // tracks don't collide.
        builder.Property(track => track.YouTubeVideoId).HasColumnName("youtube_video_id");
        builder.HasIndex(track => track.YouTubeVideoId).IsUnique();

        // A multi-value record mapped as an optional complex type: its columns live on the tracks
        // table (local_file_path, local_file_quality_codec, ...) and are all NULL when absent.
        builder.ComplexProperty(track => track.LocalFile, file =>
        {
            file.Property(f => f.Path).HasMaxLength(4096);
            file.ComplexProperty(f => f.Quality);
        });
    }
}
