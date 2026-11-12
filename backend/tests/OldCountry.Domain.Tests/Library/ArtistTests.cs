using OldCountry.Domain.Errors;
using OldCountry.Domain.Library;

namespace OldCountry.Domain.Tests.Library;

public sealed class ArtistTests
{
    [Fact]
    public void Names_normalize_ignoring_case_and_surrounding_space() =>
        Artist.Normalize("  The Band ").ShouldBe(Artist.Normalize("the band"));

    [Fact]
    public void A_blank_name_breaks_an_invariant() =>
        Should.Throw<DomainException>(() => Artist.Create("   ")).Code.ShouldBe("artist.invalid_name");
}
