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
        builder.Property(artist => artist.NormalizedName).HasMaxLength(Artist.NameMaxLength);
        builder.HasIndex(artist => artist.NormalizedName).IsUnique();
    }
}
