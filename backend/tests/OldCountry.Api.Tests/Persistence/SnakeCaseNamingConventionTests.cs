using OldCountry.Api.Persistence.Conventions;

namespace OldCountry.Api.Tests.Persistence;

public sealed class SnakeCaseNamingConventionTests
{
    [Theory]
    [InlineData("Tracks", "tracks")]
    [InlineData("ArtistId", "artist_id")]
    [InlineData("SampleRateHz", "sample_rate_hz")]
    [InlineData("HTTPServer", "http_server")]
    [InlineData("Mp3Bitrate", "mp3_bitrate")]
    public void Converts_pascal_case(string name, string expected) =>
        SnakeCaseNamingConvention.ToSnakeCase(name).ShouldBe(expected);
}
