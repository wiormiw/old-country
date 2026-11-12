using OldCountry.Api.Common.Decorators;
using OldCountry.Api.Persistence;

namespace OldCountry.Api.Common.Messaging;

/// <summary>
/// Explicit DI for use cases: no assembly scanning and no mediator. Each registration spells out
/// its decorator chain with <c>new</c>, so the pipeline order is visible here and checked by the
/// compiler. Endpoints inject <see cref="ICommandHandler{TCommand,TResult}"/> or
/// <see cref="IQueryHandler{TQuery,TResult}"/> directly.
/// </summary>
internal static class HandlerRegistration
{
    /// <summary>Command pipeline: Logging → Transaction → handler.</summary>
    public static IServiceCollection AddCommand<TCommand, TResult, THandler>(this IServiceCollection services)
        where TCommand : ICommand<TResult>
        where THandler : class, ICommandHandler<TCommand, TResult>
    {
        services.AddScoped<THandler>();
        services.AddScoped<ICommandHandler<TCommand, TResult>>(provider =>
            new LoggingDecorator.CommandHandler<TCommand, TResult>(
                new TransactionDecorator.CommandHandler<TCommand, TResult>(
                    provider.GetRequiredService<THandler>(),
                    provider.GetRequiredService<OldCountryDbContext>()),
                provider.GetRequiredService<ILoggerFactory>()));
        return services;
    }

    /// <summary>Query pipeline: Logging → handler.</summary>
    public static IServiceCollection AddQuery<TQuery, TResult, THandler>(this IServiceCollection services)
        where TQuery : IQuery<TResult>
        where THandler : class, IQueryHandler<TQuery, TResult>
    {
        services.AddScoped<THandler>();
        services.AddScoped<IQueryHandler<TQuery, TResult>>(provider =>
            new LoggingDecorator.QueryHandler<TQuery, TResult>(
                provider.GetRequiredService<THandler>(),
                provider.GetRequiredService<ILoggerFactory>()));
        return services;
    }
}
