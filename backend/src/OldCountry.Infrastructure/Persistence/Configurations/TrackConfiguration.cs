using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OldCountry.Domain.Artists;
using OldCountry.Domain.Downloads;
using OldCountry.Domain.Tracks;

namespace OldCountry.Infrastructure.Persistence.Configurations;

internal sealed class TrackConfiguration : IEntityTypeConfiguration<Track>
{
    public void Configure(EntityTypeBuilder<Track> builder)
    {
        builder.HasKey(track => track.Id);
        builder.Property(track => track.Id).ValueGeneratedNever();
        builder.Property(track => track.Title).HasMaxLength(Track.TitleMaxLength);
        builder.Ignore(track => track.IsAvailableOffline);

        builder.HasOne<Artist>().WithMany().HasForeignKey(track => track.ArtistId).OnDelete(DeleteBehavior.Restrict);

        // One library entry for each YouTube video. Postgres treats NULL values as distinct,
        // so tracks that are only local do not collide.
        builder.Property(track => track.YouTubeVideoId).HasColumnName("youtube_video_id");
        builder.HasIndex(track => track.YouTubeVideoId).IsUnique();

        // A record with many values, mapped as an optional complex type. Its columns are in the tracks
        // table (local_file_path, local_file_quality_codec, ...). All of them are NULL when it is absent.
        builder.ComplexProperty(track => track.LocalFile, file =>
        {
            file.Property(f => f.Path).HasMaxLength(4096);
            file.ComplexProperty(f => f.Quality);
        });

        // The same for the last refused download: download_problem_reason, _message, _at.
        builder.ComplexProperty(track => track.DownloadProblem, problem =>
            problem.Property(p => p.Message).HasMaxLength(DownloadProblem.MessageMaxLength));
    }
}
