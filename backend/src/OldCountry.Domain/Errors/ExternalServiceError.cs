namespace OldCountry.Domain.Errors;

/// <summary>A service outside this server (YouTube, a helper program) failed or refused.</summary>
public sealed record ExternalServiceError(string Code, string Message) : IError;
