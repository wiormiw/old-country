using OldCountry.Domain.Errors;

namespace OldCountry.Application.Abstractions.YouTube;

/// <summary>yt-dlp failed, did not stop in time, or is not installed. You can show <see cref="Code"/> to clients.</summary>
public sealed class YtDlpException(string code, string message, Exception? inner = null) : Exception(message, inner)
{
    public string Code { get; } = code;

    public ExternalServiceError ToError() => new(Code, Message);
}
