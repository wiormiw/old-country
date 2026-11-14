namespace OldCountry.Application.Events;

/// <summary>
/// Runs the handlers of each committed event, one event at a time.
/// Each handler runs in its own DI scope, so it gets a new DbContext.
/// When a handler fails, the dispatcher logs it and continues with the other handlers.
/// </summary>
internal sealed partial class DomainEventDispatcher(
    DomainEventFeed feed,
    DomainEventHandlers handlers,
    IServiceScopeFactory scopes,
    ILogger<DomainEventDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var domainEvent in feed.Reader.ReadAllAsync(stoppingToken))
            {
                foreach (var handle in handlers.For(domainEvent.GetType()))
                {
                    try
                    {
                        await using var scope = scopes.CreateAsyncScope();
                        await handle(scope.ServiceProvider, domainEvent, stoppingToken);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                    {
                        LogHandlerFailed(exception, domainEvent.GetType().Name);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The server stops.
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A {Event} handler failed")]
    private partial void LogHandlerFailed(Exception exception, string @event);
}
