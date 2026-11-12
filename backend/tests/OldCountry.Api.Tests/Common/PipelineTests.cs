using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OldCountry.Api.Common.Decorators;
using OldCountry.Api.Common.Messaging;
using OldCountry.Api.Features.Tracks;
using OldCountry.Api.Features.Tracks.AddTrack;
using OldCountry.Api.Features.Tracks.GetTrack;
using OldCountry.Api.Persistence;
using OldCountry.Domain.Common;
using OldCountry.Domain.Errors;

namespace OldCountry.Api.Tests.Common;

/// <summary>The explicit registrations build the decorator chains in the documented order.</summary>
public sealed class PipelineTests
{
    private static ServiceProvider Build(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<OldCountry.Api.Common.Library.ArtistDirectory>();
        // Never connects: the decorators only need an instance to be constructed.
        services.AddDbContext<OldCountryDbContext>(options => options.UseNpgsql("Host=unused"));
        register(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Fact]
    public void Commands_run_Logging_then_Transaction_then_the_handler()
    {
        using var provider = Build(TracksFeature.AddServices);
        using var scope = provider.CreateScope();

        var outer = scope.ServiceProvider.GetRequiredService<ICommandHandler<AddTrackCommand, AddTrackResult>>()
            .ShouldBeOfType<LoggingDecorator.CommandHandler<AddTrackCommand, AddTrackResult>>();
        var transaction = outer.Inner.ShouldBeOfType<TransactionDecorator.CommandHandler<AddTrackCommand, AddTrackResult>>();
        transaction.Inner.ShouldBeOfType<AddTrackHandler>();
    }

    [Fact]
    public void Queries_run_Logging_then_the_handler()
    {
        using var provider = Build(TracksFeature.AddServices);
        using var scope = provider.CreateScope();

        var outer = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetTrackQuery, GetTrackResult>>()
            .ShouldBeOfType<LoggingDecorator.QueryHandler<GetTrackQuery, GetTrackResult>>();
        outer.Inner.ShouldBeOfType<GetTrackHandler>();
    }

    [Fact]
    public async Task A_refusal_is_logged_as_refused_with_its_code_not_as_a_failure()
    {
        var sink = new LogSink();
        using var provider = Build(services =>
        {
            services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Debug).AddProvider(sink));
            services.AddQuery<GetTrackQuery, GetTrackResult, AlwaysNotFound>();
        });
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetTrackQuery, GetTrackResult>>()
            .Handle(new GetTrackQuery(TrackId.New()), TestContext.Current.CancellationToken);

        result.Value.ShouldBeOfType<NotFoundError>();
        var entry = sink.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.EventId.Id.ShouldBe(101);
        entry.Message.ShouldContain("GetTrackQuery refused with track.not_found");
    }

    internal sealed class AlwaysNotFound : IQueryHandler<GetTrackQuery, GetTrackResult>
    {
        public Task<GetTrackResult> Handle(GetTrackQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<GetTrackResult>(new NotFoundError("track", query.Id.ToString()));
    }

    private sealed record LogEntry(string Category, LogLevel Level, EventId EventId, string Message);

    private sealed class LogSink : ILoggerProvider
    {
        private readonly List<LogEntry> _entries = [];

        public IReadOnlyList<LogEntry> Entries
        {
            get { lock (_entries) return [.. _entries.Where(e => e.Category == LoggingDecorator.LoggerCategory)]; }
        }

        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

        public void Dispose() { }

        private sealed class Logger(LogSink sink, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (sink._entries) sink._entries.Add(new LogEntry(category, logLevel, eventId, formatter(state, exception)));
            }
        }
    }
}
