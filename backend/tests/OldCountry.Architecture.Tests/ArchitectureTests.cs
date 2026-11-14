using System.Reflection;
using System.Text.RegularExpressions;
using OldCountry.Api.Http;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Domain.Tracks;

namespace OldCountry.Architecture.Tests;

/// <summary>
/// The architecture, checked in the compiled IL and in the source files.
/// The layers point inward: Api → Infrastructure → Application → Domain.
/// The feature slices in Application do not use each other.
/// </summary>
public sealed partial class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(TrackId).Assembly;
    private static readonly Assembly Application = typeof(OldCountry.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(OldCountry.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly Api = typeof(ProblemResults).Assembly;
    private static readonly Assembly[] Source = [Domain, Application, Infrastructure, Api];

    /// <summary>The top-level folders of Application that are not features. Announcements may use the response types of other features.</summary>
    private static readonly string[] NotFeatures = ["Abstractions", "Pipeline", "Events", "Realtime", "Common", "Announcements"];

    private static void Holds(NetArchTest.Rules.TestResult result, string rule) =>
        result.IsSuccessful.ShouldBeTrue($"{rule}: {string.Join(", ", result.FailingTypeNames ?? [])}");

    private static IEnumerable<string> ReferenceNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(name => name.Name ?? "");

    [Fact]
    public void Domain_is_plain_csharp() => Holds(
        Types.InAssembly(Domain).ShouldNot().HaveDependencyOnAny(
            "OldCountry.Application", "OldCountry.Infrastructure", "OldCountry.Api", "Microsoft.EntityFrameworkCore",
            "Microsoft.AspNetCore", "Microsoft.Extensions", "Npgsql", "System.Text.Json").GetResult(),
        "Domain uses only the BCL");

    [Fact]
    public void Domain_references_no_assemblies_of_ours_or_any_package() =>
        ReferenceNames(Domain)
            .Where(name => !name.StartsWith("System", StringComparison.Ordinal) && name != "netstandard")
            .ShouldBeEmpty();

    [Fact]
    public void Application_does_not_use_the_outer_layers_or_their_libraries()
    {
        Holds(
            Types.InAssembly(Application).ShouldNot().HaveDependencyOnAny(
                "OldCountry.Infrastructure", "OldCountry.Api", "Microsoft.AspNetCore", "TagLib").GetResult(),
            "Application knows no HTTP, no adapters and no tag library");
        ReferenceNames(Application).ShouldNotContain(name =>
            name == "OldCountry.Infrastructure" || name == "OldCountry.Api" || name == "TagLibSharp" || name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    [Fact]
    public void Application_uses_postgres_only_in_queries() => Holds(
        Types.InAssembly(Application).ShouldNot().HaveDependencyOnAny(
            "Npgsql.NpgsqlConnection", "Npgsql.NpgsqlDataSource", "Npgsql.NpgsqlCommand",
            "Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure").GetResult(),
        "connections, data sources and UseNpgsql belong to Infrastructure; Application may only use EF query functions such as ILike");

    [Fact]
    public void Infrastructure_does_not_use_the_api()
    {
        Holds(Types.InAssembly(Infrastructure).ShouldNot().HaveDependencyOn("OldCountry.Api").GetResult(), "Infrastructure does not know the host");
        ReferenceNames(Infrastructure).ShouldNotContain("OldCountry.Api");
    }

    [Fact]
    public void Feature_slices_never_reference_another_feature()
    {
        const string Root = "OldCountry.Application";
        var features = Types.InAssembly(Application).GetTypes()
            .Select(type => type.Namespace ?? "")
            .Where(ns => ns.StartsWith(Root + ".", StringComparison.Ordinal))
            .Select(ns => ns[(Root.Length + 1)..].Split('.')[0])
            .Where(top => !NotFeatures.Contains(top))
            .Distinct()
            .ToList();
        features.ShouldBe(["Downloads", "Playlists", "Tracks", "YouTubeMusic"], ignoreOrder: true);

        foreach (var feature in features)
        {
            var others = features.Where(other => other != feature).Select(other => $"{Root}.{other}").ToArray();
            Holds(
                Types.InAssembly(Application).That().ResideInNamespaceStartingWith($"{Root}.{feature}")
                    .ShouldNot().HaveDependencyOnAny(others).GetResult(),
                $"feature '{feature}' shares only through Domain, Common or Abstractions");
        }
    }

    [Fact]
    public void Common_has_only_topic_folders() =>
        Application.GetTypes()
            .Where(type => type.Namespace == "OldCountry.Application.Common" && !type.IsNested)
            .Select(type => type.FullName)
            .ShouldBeEmpty("put a shared type in Common/<Topic>/, for example Common/YouTube");

    [Theory]
    [InlineData(typeof(ICommandHandler<,>))]
    [InlineData(typeof(IQueryHandler<,>))]
    public void Handlers_are_internal_sealed_and_named_after_their_use_case(Type handlerInterface)
    {
        // The pipeline decorators also implement the interfaces. They are not use cases.
        var handlers = Types.InAssembly(Application).That().ImplementInterface(handlerInterface)
            .And().DoNotResideInNamespace("OldCountry.Application.Pipeline");
        handlers.GetTypes().ShouldNotBeEmpty();

        Holds(handlers.Should().BeSealed().And().NotBePublic().And().HaveNameEndingWith("Handler").GetResult(),
            "handlers are details of their slice");
    }

    [Fact]
    public void There_is_no_repository_or_unit_of_work_layer() => Holds(
        Types.InAssemblies(Source).ShouldNot().HaveNameEndingWith("Repository").And().NotHaveNameEndingWith("UnitOfWork").GetResult(),
        "use cases query IAppDbContext directly; the context is the unit of work");

    [Fact]
    public void Logging_goes_through_source_generated_LoggerMessage_methods_only() => Holds(
        Types.InAssemblies(Source).ShouldNot().HaveDependencyOn("Microsoft.Extensions.Logging.LoggerExtensions").GetResult(),
        "no logger.LogInformation(...) calls; declare a [LoggerMessage] partial method instead");

    [Theory]
    [InlineData("MediatR")]
    [InlineData("AutoMapper")]
    [InlineData("Scrutor")]
    public void Banned_libraries_are_not_referenced(string library)
    {
        foreach (var assembly in Source) ReferenceNames(assembly).ShouldNotContain(library);
    }

    [Fact]
    public void Each_source_file_holds_one_top_level_type()
    {
        var crowded = Directory.EnumerateFiles(SourceFolder(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => TopLevelType().Count(File.ReadAllText(path)) > 1)
            .Select(path => Path.GetRelativePath(SourceFolder(), path));
        crowded.ShouldBeEmpty("move each extra type to a file with its own name");
    }

    /// <summary>A type declaration at column 0. File-scoped namespaces put top-level types there. <c>file</c> types do not count.</summary>
    [GeneratedRegex(@"^(?:public |internal )?(?:sealed |static |abstract |partial |readonly )*(?:class|record|struct|interface|enum|union) ", RegexOptions.Multiline)]
    private static partial Regex TopLevelType();

    /// <summary>The <c>src</c> folder, found from the folder of the test assembly.</summary>
    private static string SourceFolder()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "OldCountry.slnx"))) return Path.Combine(folder.FullName, "src");
        }
        throw new InvalidOperationException("OldCountry.slnx not found above " + AppContext.BaseDirectory);
    }
}
