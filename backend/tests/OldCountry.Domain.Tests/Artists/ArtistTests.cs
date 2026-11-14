using OldCountry.Domain.Artists;
using OldCountry.Domain.Errors;

namespace OldCountry.Domain.Tests.Artists;

public sealed class ArtistTests
{
    [Fact]
    public void Names_normalize_ignoring_case_and_surrounding_space() =>
        Artist.Normalize("  The Band ").ShouldBe(Artist.Normalize("the band"));

    [Fact]
    public void A_blank_name_breaks_an_invariant() =>
        Should.Throw<DomainException>(() => Artist.Create("   ")).Code.ShouldBe("artist.invalid_name");
}
