using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OldCountry.Domain.Artists;

namespace OldCountry.Infrastructure.Persistence.Configurations;

internal sealed class ArtistConfiguration : IEntityTypeConfiguration<Artist>
{
    public void Configure(EntityTypeBuilder<Artist> builder)
    {
        builder.HasKey(artist => artist.Id);
        builder.Property(artist => artist.Id).ValueGeneratedNever();
        builder.Property(artist => artist.Name).HasMaxLength(Artist.NameMaxLength);
        // A trigram index makes "contains" search (ILIKE '%text%') fast. It needs the pg_trgm extension.
        builder.HasIndex(artist => artist.Name).HasMethod("gin").HasOperators("gin_trgm_ops");
        builder.Property(artist => artist.NormalizedName).HasMaxLength(Artist.NameMaxLength);
        builder.HasIndex(artist => artist.NormalizedName).IsUnique();
    }
}
