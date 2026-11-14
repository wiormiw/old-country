using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OldCountry.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261112194135_LinkPlaylistsToYouTube : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "you_tube_list_id",
            table: "playlists",
            type: "character varying(80)",
            maxLength: 80,
            nullable: true);

        migrationBuilder.AddColumn<List<string>>(
            name: "you_tube_videos_taken",
            table: "playlists",
            type: "text[]",
            nullable: false,
            defaultValueSql: "'{}'");

        migrationBuilder.CreateIndex(
            name: "ix_playlists_you_tube_list_id",
            table: "playlists",
            column: "you_tube_list_id",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_playlists_you_tube_list_id",
            table: "playlists");

        migrationBuilder.DropColumn(
            name: "you_tube_list_id",
            table: "playlists");

        migrationBuilder.DropColumn(
            name: "you_tube_videos_taken",
            table: "playlists");
    }
}
