using OldCountry.Domain.Errors;
using OldCountry.Domain.YouTube;

namespace OldCountry.Domain.Tests.YouTube;

public sealed class YouTubeVideoIdTests
{
    [Theory]
    [InlineData("dQw4w9WgXcQ")]
    [InlineData("a-b_c-d_e-f")]
    public void YouTubeVideoId_AcceptsElevenUrlSafeCharacters(string value) =>
        YouTubeVideoId.Create(value).Value.ShouldBe(new YouTubeVideoId(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tooShort")]
    [InlineData("twelve-chars")]
    [InlineData("has space!!")]
    public void YouTubeVideoId_RejectsAnythingElse(string? value) =>
        YouTubeVideoId.Create(value).Value.ShouldBeOfType<ValidationError>().Errors.ShouldContainKey("youTubeVideoId");

    [Fact]
    public void YouTubeVideoIds_AreEqualByValue() =>
        new YouTubeVideoId("dQw4w9WgXcQ").ShouldBe(new YouTubeVideoId("dQw4w9WgXcQ"));
}
