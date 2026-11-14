using System.Diagnostics;
using System.Runtime.CompilerServices;
using OldCountry.Application.Abstractions.Messaging;
using OldCountry.Domain.Errors;

namespace OldCountry.Application.Pipeline;

/// <summary>
/// The outermost decorator: one trace span and one log line for each use case, with its result.
/// An expected refusal (a union result that holds an <see cref="IError"/>) is correct behavior of the API.
/// Thus the decorator logs it at Information level with its code, not as a failure.
/// </summary>
internal static partial class LoggingDecorator
{
    public const string ActivitySourceName = "OldCountry.Application";
    public const string LoggerCategory = "OldCountry.Application.UseCases";

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    internal sealed class CommandHandler<TCommand, TResult>(ICommandHandler<TCommand, TResult> inner, ILoggerFactory loggers)
        : ICommandHandler<TCommand, TResult> where TCommand : ICommand<TResult>
    {
        internal ICommandHandler<TCommand, TResult> Inner => inner;

        public Task<TResult> Handle(TCommand command, CancellationToken cancellationToken) =>
            Run(typeof(TCommand).Name, () => inner.Handle(command, cancellationToken), loggers);
    }

    internal sealed class QueryHandler<TQuery, TResult>(IQueryHandler<TQuery, TResult> inner, ILoggerFactory loggers)
        : IQueryHandler<TQuery, TResult> where TQuery : IQuery<TResult>
    {
        internal IQueryHandler<TQuery, TResult> Inner => inner;

        public Task<TResult> Handle(TQuery query, CancellationToken cancellationToken) =>
            Run(typeof(TQuery).Name, () => inner.Handle(query, cancellationToken), loggers);
    }

    private static async Task<TResult> Run<TResult>(string useCase, Func<Task<TResult>> next, ILoggerFactory loggers)
    {
        var logger = loggers.CreateLogger(LoggerCategory);
        using var activity = ActivitySource.StartActivity(useCase);
        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = await next();
            var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (result is IUnion { Value: IError error })
            {
                activity?.SetTag("oldcountry.error.code", error.Code);
                Refused(logger, useCase, error.Code, elapsedMs);
            }
            else
            {
                Handled(logger, useCase, elapsedMs);
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Unset, "cancelled");
            throw;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            Failed(logger, exception, useCase, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
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
