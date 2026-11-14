namespace OldCountry.Application.Abstractions.Messaging;

/// <summary>
/// The command must not keep a database transaction open.
/// For example, it waits for a slow external process (a yt-dlp download) between its reads and its writes.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class NoTransactionAttribute : Attribute;
