using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OldCountry.Domain.Downloads;

namespace OldCountry.Infrastructure.Persistence.Configurations;

internal sealed class DownloadJobConfiguration : IEntityTypeConfiguration<DownloadJob>
{
    public void Configure(EntityTypeBuilder<DownloadJob> builder)
    {
        builder.HasKey(job => job.Id);
        builder.Property(job => job.Id).ValueGeneratedNever();
        builder.Property(job => job.VideoId).HasColumnName("youtube_video_id");
        builder.Property(job => job.ErrorCode).HasMaxLength(64);
        builder.Property(job => job.Error).HasMaxLength(2000);
        builder.Ignore(job => job.IsActive);

        // The lookup of the worker: the oldest job that it can claim.
        builder.HasIndex(job => new { job.State, job.QueuedAt });
        // A video is queued or running a maximum of one time. A new request joins the existing job.
        builder.HasIndex(job => job.VideoId).IsUnique().HasFilter("state IN ('Queued', 'Running')");
    }
}
