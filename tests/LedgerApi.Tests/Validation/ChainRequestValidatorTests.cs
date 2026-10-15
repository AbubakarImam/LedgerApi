using LedgerApi.Contracts.Requests;
using LedgerApi.Validation;

namespace LedgerApi.Tests.Validation;

public class ChainRequestValidatorTests
{
    private static ChainLegRequest Leg(decimal amount = 10m, string purpose = "Principal", string debit = "1", string credit = "2") =>
        new(debit, credit, amount, purpose);

    private static IReadOnlyList<string> Validate(params ChainLegRequest[] legs) =>
        new ChainRequestValidator().Validate(new ChainRequest("key-1", null, legs));

    [Fact]
    public void AValidChain_HasNoErrors() =>
        Assert.Empty(Validate(Leg(), Leg(purpose: "Fee", credit: "3"), Leg(purpose: "tax", credit: "4")));

    [Fact]
    public void NoLegs_IsRejected() => Assert.Contains("A chain needs at least one leg.", Validate());

    [Fact]
    public void ElevenLegs_IsRejected() =>
        Assert.Contains("A chain can have at most 10 legs.",
            Validate(new[] { Leg() }.Concat(Enumerable.Range(0, 10).Select(_ => Leg(purpose: "Fee"))).ToArray()));

    [Fact]
    public void TenLegs_IsAllowed() =>
        Assert.Empty(Validate(new[] { Leg() }.Concat(Enumerable.Range(0, 9).Select(_ => Leg(purpose: "Fee"))).ToArray()));

    [Fact]
    public void TwoPrincipals_IsRejected() => Assert.Contains("A chain can have only one Principal leg.", Validate(Leg(), Leg()));

    [Fact]
    public void FirstLegNotPrincipal_IsRejected() => Assert.Contains("The first leg must be the Principal.", Validate(Leg(purpose: "Fee"), Leg()));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ANonPositiveAmount_IsRejected(decimal amount) =>
        Assert.Contains("Leg 2: amount must be greater than zero.", Validate(Leg(), Leg(amount, "Fee")));

    [Theory]
    [InlineData("Bonus")]
    [InlineData("1")]   // Enum.TryParse would accept a number; names only
    public void AnUnknownPurpose_IsRejected(string purpose) =>
        Assert.Contains("Leg 2: purpose must be Principal, Fee or Tax.", Validate(Leg(), Leg(purpose: purpose)));

    [Fact]
    public void AMissingLeg_IsRejectedRatherThanThrowing() =>
        Assert.Contains("Leg 2 is missing.", Validate(Leg(), null!));
}
