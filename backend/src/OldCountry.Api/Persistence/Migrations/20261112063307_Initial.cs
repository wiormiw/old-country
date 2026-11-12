using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OldCountry.Api.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261112063307_Initial : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "artists",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                normalized_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_artists", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "tracks",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                artist_id = table.Column<Guid>(type: "uuid", nullable: false),
                duration = table.Column<TimeSpan>(type: "interval", nullable: false),
                youtube_video_id = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: true),
                local_file_path = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                local_file_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                local_file_quality_bitrate_kbps = table.Column<int>(type: "integer", nullable: true),
                local_file_quality_codec = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                local_file_quality_sample_rate_hz = table.Column<int>(type: "integer", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_tracks", x => x.id);
                table.ForeignKey(
                    name: "fk_tracks_artists_artist_id",
                    column: x => x.artist_id,
                    principalTable: "artists",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_artists_normalized_name",
            table: "artists",
            column: "normalized_name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_tracks_artist_id",
            table: "tracks",
            column: "artist_id");

        migrationBuilder.CreateIndex(
            name: "ix_tracks_youtube_video_id",
            table: "tracks",
            column: "youtube_video_id",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "tracks");

        migrationBuilder.DropTable(
            name: "artists");
    }
}
