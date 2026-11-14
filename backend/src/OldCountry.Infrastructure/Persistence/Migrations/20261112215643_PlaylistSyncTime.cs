using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OldCountry.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261112215643_PlaylistSyncTime : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "you_tube_synced_at",
            table: "playlists",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "you_tube_synced_at",
            table: "playlists");
    }
}
