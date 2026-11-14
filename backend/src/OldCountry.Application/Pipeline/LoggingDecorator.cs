using System.Diagnostics;
using System.Runtime.CompilerServices;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Application.Common.Telemetry;
using OldCountry.Domain.Errors;

namespace OldCountry.Application.Pipeline;

/// <summary>
/// The outermost decorator: one trace span, one log line and one duration measurement for each use case, with its result.
/// An expected refusal (a union result that holds an <see cref="IError"/>) is correct behavior of the API.
/// Thus the decorator logs it at Information level with its code, not as a failure.
/// </summary>
internal static partial class LoggingDecorator
{
    public const string ActivitySourceName = "OldCountry.Application";
    public const string LoggerCategory = "OldCountry.Application.UseCases";

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    internal sealed class CommandHandler<TCommand, TResult>(ICommandHandler<TCommand, TResult> inner, ILoggerFactory loggers, AppMetrics metrics)
        : ICommandHandler<TCommand, TResult> where TCommand : ICommand<TResult>
    {
        internal ICommandHandler<TCommand, TResult> Inner => inner;

        public Task<TResult> Handle(TCommand command, CancellationToken cancellationToken) =>
            Run(typeof(TCommand).Name, () => inner.Handle(command, cancellationToken), loggers, metrics);
    }

    internal sealed class QueryHandler<TQuery, TResult>(IQueryHandler<TQuery, TResult> inner, ILoggerFactory loggers, AppMetrics metrics)
        : IQueryHandler<TQuery, TResult> where TQuery : IQuery<TResult>
    {
        internal IQueryHandler<TQuery, TResult> Inner => inner;

        public Task<TResult> Handle(TQuery query, CancellationToken cancellationToken) =>
            Run(typeof(TQuery).Name, () => inner.Handle(query, cancellationToken), loggers, metrics);
    }

    private static async Task<TResult> Run<TResult>(string useCase, Func<Task<TResult>> next, ILoggerFactory loggers, AppMetrics metrics)
    {
        var logger = loggers.CreateLogger(LoggerCategory);
        using var activity = ActivitySource.StartActivity(useCase);
        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = await next();
            var elapsed = Stopwatch.GetElapsedTime(started);
            if (result is IUnion { Value: IError error })
            {
                activity?.SetTag("oldcountry.error.code", error.Code);
                Refused(logger, useCase, error.Code, elapsed.TotalMilliseconds);
                metrics.UseCaseEnded(useCase, "refused", error.Code, elapsed);
            }
            else
            {
                Handled(logger, useCase, elapsed.TotalMilliseconds);
                metrics.UseCaseEnded(useCase, "handled", null, elapsed);
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Unset, "cancelled");
            metrics.UseCaseEnded(useCase, "cancelled", null, Stopwatch.GetElapsedTime(started));
            throw;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            var elapsed = Stopwatch.GetElapsedTime(started);
            Failed(logger, exception, useCase, elapsed.TotalMilliseconds);
            metrics.UseCaseEnded(useCase, "failed", null, elapsed);
            throw;
        }
    }

    [LoggerMessage(EventId = 100, Level = LogLevel.Debug, Message = "{UseCase} handled in {ElapsedMs:0.0} ms")]
    private static partial void Handled(ILogger logger, string useCase, double elapsedMs);

    [LoggerMessage(EventId = 101, Level = LogLevel.Information, Message = "{UseCase} refused with {ErrorCode} after {ElapsedMs:0.0} ms")]
    private static partial void Refused(ILogger logger, string useCase, string errorCode, double elapsedMs);

    [LoggerMessage(EventId = 102, Level = LogLevel.Error, Message = "{UseCase} failed after {ElapsedMs:0.0} ms")]
    private static partial void Failed(ILogger logger, Exception exception, string useCase, double elapsedMs);
}
