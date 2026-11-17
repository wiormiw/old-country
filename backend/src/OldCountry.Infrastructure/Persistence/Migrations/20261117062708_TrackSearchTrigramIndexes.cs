using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OldCountry.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261117062708_TrackSearchTrigramIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterDatabase()
            .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

        migrationBuilder.CreateIndex(
            name: "ix_tracks_title",
            table: "tracks",
            column: "title")
            .Annotation("Npgsql:IndexMethod", "gin")
            .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

        migrationBuilder.CreateIndex(
            name: "ix_artists_name",
            table: "artists",
            column: "name")
            .Annotation("Npgsql:IndexMethod", "gin")
            .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_tracks_title",
            table: "tracks");

        migrationBuilder.DropIndex(
            name: "ix_artists_name",
            table: "artists");

        migrationBuilder.AlterDatabase()
            .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
    }
}
