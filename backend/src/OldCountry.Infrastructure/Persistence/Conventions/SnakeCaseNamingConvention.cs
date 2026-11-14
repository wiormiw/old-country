using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace OldCountry.Infrastructure.Persistence.Conventions;

/// <summary>
/// Postgres names in snake_case: <c>Track.YouTubeVideoId</c> becomes <c>tracks.you_tube_video_id</c>.
/// Complex-type columns (<c>LocalFile.Quality.Codec</c>) become <c>local_file_quality_codec</c>.
/// Keys, foreign keys and indexes follow. The convention runs last, so it sees all names that the configurations set.
/// It comes from akashic_records_be_dotnet, with an extension for complex types.
/// </summary>
internal sealed class SnakeCaseNamingConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entity in modelBuilder.Metadata.GetEntityTypes())
        {
            if (entity.GetTableName() is { } table) entity.Builder.ToTable(ToSnakeCase(table));
            foreach (var property in entity.GetProperties())
                property.Builder.HasColumnName(ToSnakeCase(property.GetColumnName()));
            foreach (var complex in entity.GetComplexProperties())
                RenameComplex(complex, ToSnakeCase(complex.Name));
        }

        // Constraint names come from the table and column names, so they come second.
        foreach (var entity in modelBuilder.Metadata.GetEntityTypes())
        {
            foreach (var key in entity.GetDeclaredKeys())
                key.Builder.HasName(key.GetDefaultName()?.ToLowerInvariant());
            foreach (var foreignKey in entity.GetDeclaredForeignKeys())
                foreignKey.Builder.HasConstraintName(foreignKey.GetDefaultName()?.ToLowerInvariant());
            foreach (var index in entity.GetDeclaredIndexes())
                index.Builder.HasDatabaseName((index.Name ?? index.GetDefaultDatabaseName())?.ToLowerInvariant());
        }
    }

    /// <summary>Complex-type columns start with their path, so two value objects of the same type never collide.</summary>
    private static void RenameComplex(IConventionComplexProperty complex, string prefix)
    {
        // A type mapped to JSON is one column (its configuration sets the name). Its properties are
        // JSON keys, not columns, so there is nothing to rename.
        if (complex.ComplexType.IsMappedToJson()) return;

        foreach (var property in complex.ComplexType.GetProperties())
            property.Builder.HasColumnName($"{prefix}_{ToSnakeCase(property.Name)}");
        foreach (var nested in complex.ComplexType.GetComplexProperties())
            RenameComplex(nested, $"{prefix}_{ToSnakeCase(nested.Name)}");
    }

    internal static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];
            if (char.IsUpper(current))
            {
                // A word starts at a capital letter after a lower-case letter or a digit,
                // or at the last capital of an acronym ("HTTPServer" → http_server).
                var previous = i > 0 ? name[i - 1] : '\0';
                var next = i + 1 < name.Length ? name[i + 1] : '\0';
                if (i > 0 && previous != '_' && (char.IsLower(previous) || char.IsDigit(previous) || (char.IsUpper(previous) && char.IsLower(next))))
                    builder.Append('_');
                builder.Append(char.ToLowerInvariant(current));
            }
            else
            {
                builder.Append(current);
            }
        }
        return builder.ToString();
    }
}
