using OldCountry.Application.Abstractions.Messaging;

namespace OldCountry.Application.Downloads.ListDownloads;

public sealed record ListDownloadsQuery : IQuery<DownloadsResponse>;
