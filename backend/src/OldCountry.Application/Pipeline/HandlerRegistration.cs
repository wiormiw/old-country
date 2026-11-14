using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Abstractions.Persistence;
using OldCountry.Application.Common.Telemetry;

namespace OldCountry.Application.Pipeline;

/// <summary>
/// Explicit DI for the use cases: no assembly scan and no mediator.
/// Each registration writes its decorator chain with <c>new</c>. Thus you can see the order here, and the compiler checks it.
/// Endpoints get <see cref="ICommandHandler{TCommand,TResult}"/> or <see cref="IQueryHandler{TQuery,TResult}"/> directly.
/// </summary>
internal static class HandlerRegistration
{
    /// <summary>The command pipeline: Logging → Transaction → handler.</summary>
    public static IServiceCollection AddCommand<TCommand, TResult, THandler>(this IServiceCollection services)
        where TCommand : ICommand<TResult>
        where THandler : class, ICommandHandler<TCommand, TResult>
    {
        services.AddScoped<THandler>();
        services.AddScoped<ICommandHandler<TCommand, TResult>>(provider =>
            new LoggingDecorator.CommandHandler<TCommand, TResult>(
                new TransactionDecorator.CommandHandler<TCommand, TResult>(
                    provider.GetRequiredService<THandler>(),
                    provider.GetRequiredService<IAppDbContext>()),
                provider.GetRequiredService<ILoggerFactory>(),
                provider.GetRequiredService<AppMetrics>()));
        return services;
    }

    /// <summary>The query pipeline: Logging → handler.</summary>
    public static IServiceCollection AddQuery<TQuery, TResult, THandler>(this IServiceCollection services)
        where TQuery : IQuery<TResult>
        where THandler : class, IQueryHandler<TQuery, TResult>
    {
        services.AddScoped<THandler>();
        services.AddScoped<IQueryHandler<TQuery, TResult>>(provider =>
            new LoggingDecorator.QueryHandler<TQuery, TResult>(
                provider.GetRequiredService<THandler>(),
                provider.GetRequiredService<ILoggerFactory>(),
                provider.GetRequiredService<AppMetrics>()));
        return services;
    }
}
