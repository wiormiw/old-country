using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OldCountry.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261112203304_PersistentDownloadQueue : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "download_problem_at",
            table: "tracks",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "download_problem_message",
            table: "tracks",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "download_problem_reason",
            table: "tracks",
            type: "character varying(16)",
            maxLength: 16,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "download_jobs",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                youtube_video_id = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                attempts = table.Column<int>(type: "integer", nullable: false),
                lease_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                queued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                track_id = table.Column<Guid>(type: "uuid", nullable: true),
                error_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_download_jobs", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_download_jobs_state_queued_at",
            table: "download_jobs",
            columns: new[] { "state", "queued_at" });

        migrationBuilder.CreateIndex(
            name: "ix_download_jobs_youtube_video_id",
            table: "download_jobs",
            column: "youtube_video_id",
            unique: true,
            filter: "state IN ('Queued', 'Running')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "download_jobs");

        migrationBuilder.DropColumn(
            name: "download_problem_at",
            table: "tracks");

        migrationBuilder.DropColumn(
            name: "download_problem_message",
            table: "tracks");

        migrationBuilder.DropColumn(
            name: "download_problem_reason",
            table: "tracks");
    }
}
