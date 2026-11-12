using System.Reflection;
using OldCountry.Api.Common.Messaging;
using OldCountry.Api.Persistence;
using OldCountry.Domain.Common;

namespace OldCountry.Architecture.Tests;

/// <summary>
/// The hybrid architecture, checked in compiled IL: a clean Domain at the centre, feature slices
/// around it that never reach into each other, and the project's no-library rules.
/// </summary>
public sealed class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(TrackId).Assembly;
    private static readonly Assembly Api = typeof(OldCountryDbContext).Assembly;
    private const string FeaturesNamespace = "OldCountry.Api.Features";

    private static void Holds(NetArchTest.Rules.TestResult result, string rule) =>
        result.IsSuccessful.ShouldBeTrue($"{rule}: {string.Join(", ", result.FailingTypeNames ?? [])}");

    [Fact]
    public void Domain_is_plain_csharp() => Holds(
        Types.InAssembly(Domain).ShouldNot().HaveDependencyOnAny(
            "OldCountry.Api", "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Microsoft.Extensions",
            "Npgsql", "System.Text.Json").GetResult(),
        "Domain depends on nothing but the BCL");

    [Fact]
    public void Domain_references_no_assemblies_of_ours_or_any_package() =>
        Domain.GetReferencedAssemblies().Select(name => name.Name)
            .Where(name => name is not null && !name.StartsWith("System", StringComparison.Ordinal) && name != "netstandard")
            .ShouldBeEmpty();

    [Fact]
    public void Feature_slices_never_reference_another_feature()
    {
        var features = Types.InAssembly(Api).That().ResideInNamespaceStartingWith(FeaturesNamespace).GetTypes()
            .Select(type => type.Namespace!)
            .Where(ns => ns.Length > FeaturesNamespace.Length)
            .Select(ns => ns[(FeaturesNamespace.Length + 1)..].Split('.')[0])
            .Distinct()
            .ToList();
        features.ShouldNotBeEmpty();

        foreach (var feature in features)
        {
            var others = features.Where(other => other != feature).Select(other => $"{FeaturesNamespace}.{other}").ToArray();
            if (others.Length == 0) continue;
            Holds(
                Types.InAssembly(Api).That().ResideInNamespaceStartingWith($"{FeaturesNamespace}.{feature}")
                    .ShouldNot().HaveDependencyOnAny(others).GetResult(),
                $"feature '{feature}' shares only through Domain or Common");
        }
    }

    [Theory]
    [InlineData(typeof(ICommandHandler<,>))]
    [InlineData(typeof(IQueryHandler<,>))]
    public void Handlers_are_internal_sealed_and_named_after_their_use_case(Type handlerInterface)
    {
        var handlers = Types.InAssembly(Api).That().ResideInNamespaceStartingWith(FeaturesNamespace).And().ImplementInterface(handlerInterface);
        handlers.GetTypes().ShouldNotBeEmpty();

        Holds(handlers.Should().BeSealed().And().NotBePublic().And().HaveNameEndingWith("Handler").GetResult(),
            "handlers are implementation details of their slice");
    }

    [Fact]
    public void There_is_no_repository_layer() => Holds(
        Types.InAssemblies([Domain, Api]).ShouldNot().HaveNameEndingWith("Repository").GetResult(),
        "use cases query OldCountryDbContext directly");

    [Fact]
    public void Logging_goes_through_source_generated_LoggerMessage_methods_only() => Holds(
        Types.InAssembly(Api).ShouldNot().HaveDependencyOn("Microsoft.Extensions.Logging.LoggerExtensions").GetResult(),
        "no logger.LogInformation(...) calls; declare a [LoggerMessage] partial method instead");

    [Theory]
    [InlineData("MediatR")]
    [InlineData("AutoMapper")]
    [InlineData("Scrutor")]
    public void Banned_libraries_are_not_referenced(string library) =>
        Api.GetReferencedAssemblies().Select(name => name.Name).ShouldNotContain(library);
}
