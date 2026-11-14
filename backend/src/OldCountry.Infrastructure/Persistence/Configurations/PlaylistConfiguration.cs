using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OldCountry.Domain.Playlists;

namespace OldCountry.Infrastructure.Persistence.Configurations;

internal sealed class PlaylistConfiguration : IEntityTypeConfiguration<Playlist>
{
    public void Configure(EntityTypeBuilder<Playlist> builder)
    {
        builder.HasKey(playlist => playlist.Id);
        builder.Property(playlist => playlist.Id).ValueGeneratedNever();
        builder.Property(playlist => playlist.Name).HasMaxLength(Playlist.NameMaxLength);
        builder.Property(playlist => playlist.Description).HasMaxLength(Playlist.DescriptionMaxLength);

        // One library playlist for each YouTube playlist. A new import finds and syncs this one.
        builder.Property(playlist => playlist.YouTubeListId).HasMaxLength(Playlist.YouTubeListIdMaxLength);
        builder.HasIndex(playlist => playlist.YouTubeListId).IsUnique();
        builder.PrimitiveCollection<List<string>>("_youTubeVideosTaken").HasColumnName("you_tube_videos_taken");

        // A collection of records, stored as one jsonb array in the playlist row. The order is the order of the array,
        // so a reorder writes one column. It does not number the rows again.
        // EF needs an IList, so it maps the private List<PlaylistEntry> field.
        // The domain continues to show the entries as read-only.
        builder.Ignore(playlist => playlist.Entries);
        builder.ComplexCollection<List<PlaylistEntry>, PlaylistEntry>("_entries", entries =>
        {
            entries.ToJson("entries");
            entries.Property(entry => entry.TrackId).HasJsonPropertyName("trackId");
            entries.Property(entry => entry.AddedAt).HasJsonPropertyName("addedAt");
        });
    }
}
