using LedgerApi.Contracts.Requests;
using LedgerApi.Entities;
using LedgerApi.Validation;

namespace LedgerApi.Tests.Validation;

public class ChainRequestValidatorTests
{
    private static ChainLegRequest Leg(decimal amount = 10m, string purpose = "Principal", string debit = "1", string credit = "2") =>
        new(debit, credit, amount, purpose);

    private static IReadOnlyList<string> Validate(params ChainLegRequest[] legs) =>
        new ChainRequestValidator().Validate(new ChainRequest("key-1", null, legs));

    private static IReadOnlyList<string> Validate(ChainKind kind, params ChainLegRequest[] legs) =>
        new ChainRequestValidator().Validate(new ChainRequest("key-1", null, legs, ChainKind: kind));

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

    // ---- Disbursement/Collection (decision #53) ----

    private static ChainLegRequest DisbursementLeg(string credit, decimal amount = 10m, string debit = "FUND") =>
        new(debit, credit, amount, "Disbursement");

    private static ChainLegRequest CollectionLeg(string debit, decimal amount = 10m, string credit = "BILLER") =>
        new(debit, credit, amount, "Collection");

    [Fact]
    public void AValidDisbursementChain_HasNoErrors() =>
        Assert.Empty(Validate(ChainKind.Disbursement,
            DisbursementLeg("R1"), DisbursementLeg("R2"), DisbursementLeg("R3")));

    [Fact]
    public void AValidCollectionChain_HasNoErrors() =>
        Assert.Empty(Validate(ChainKind.Collection,
            CollectionLeg("P1"), CollectionLeg("P2"), CollectionLeg("P3")));

    [Theory]
    [InlineData(ChainKind.Disbursement)]
    [InlineData(ChainKind.Collection)]
    public void MultiEntryChain_OneThousandOneLegs_IsRejected(ChainKind kind)
    {
        var legs = Enumerable.Range(0, 1001)
            .Select(i => kind == ChainKind.Disbursement ? DisbursementLeg($"R{i}") : CollectionLeg($"P{i}"))
            .ToArray();

        Assert.Contains("A chain can have at most 1000 legs.", Validate(kind, legs));
    }

    [Theory]
    [InlineData(ChainKind.Disbursement)]
    [InlineData(ChainKind.Collection)]
    public void MultiEntryChain_OneThousandLegs_IsAllowed(ChainKind kind)
    {
        var legs = Enumerable.Range(0, 1000)
            .Select(i => kind == ChainKind.Disbursement ? DisbursementLeg($"R{i}") : CollectionLeg($"P{i}"))
            .ToArray();

        Assert.Empty(Validate(kind, legs));
    }

    [Fact]
    public void DisbursementChain_TenLegs_IsAllowed_EvenThoughPaymentCapsAtTen() =>
        // A Disbursement chain is not bound by Payment's 10-leg cap -- its own cap is 1,000, so 11+ legs
        // (more than Payment would ever allow) must still pass as long as it is under 1,000.
        Assert.Empty(Validate(ChainKind.Disbursement,
            Enumerable.Range(0, 11).Select(i => DisbursementLeg($"R{i}")).ToArray()));

    [Fact]
    public void DisbursementChain_NonDisbursementPurpose_IsRejected() =>
        // Principal/Fee/Tax are all rejected on a Disbursement chain (no Fee/Tax legs in v1, decision #53).
        Assert.Contains("Leg 1: purpose must be Disbursement.", Validate(ChainKind.Disbursement, Leg(purpose: "Principal", debit: "FUND", credit: "R1")));

    [Fact]
    public void CollectionChain_NonCollectionPurpose_IsRejected() =>
        Assert.Contains("Leg 1: purpose must be Collection.", Validate(ChainKind.Collection, Leg(purpose: "Fee", debit: "P1", credit: "BILLER")));

    [Fact]
    public void PaymentChain_DisbursementPurpose_IsRejected() =>
        // The reverse direction: a Disbursement/Collection purpose is not valid on a Payment chain either.
        Assert.Contains("Leg 1: purpose must be Principal, Fee or Tax.", Validate(Leg(purpose: "Disbursement")));

    [Fact]
    public void PaymentChain_CollectionPurpose_IsRejected() =>
        Assert.Contains("Leg 1: purpose must be Principal, Fee or Tax.", Validate(Leg(purpose: "Collection")));

    [Fact]
    public void DisbursementChain_HasNoPrincipalFirstRule() =>
        // No leg in a Disbursement chain is "the" principal -- confirming neither Payment-only rule fires
        // even though every leg here has the same shape a lone Payment leg would.
        Assert.Empty(Validate(ChainKind.Disbursement, DisbursementLeg("R1"), DisbursementLeg("R2")));

    [Fact]
    public void DisbursementChain_WithDifferentDebitAccounts_IsRejected() =>
        Assert.Contains(
            "Leg 2: every leg must share the same debit account ('FUND'); this leg has 'OTHER-FUND'.",
            Validate(ChainKind.Disbursement, DisbursementLeg("R1"), DisbursementLeg("R2", debit: "OTHER-FUND")));

    [Fact]
    public void CollectionChain_WithDifferentCreditAccounts_IsRejected() =>
        Assert.Contains(
            "Leg 2: every leg must share the same credit account ('BILLER'); this leg has 'OTHER-BILLER'.",
            Validate(ChainKind.Collection, CollectionLeg("P1"), CollectionLeg("P2", credit: "OTHER-BILLER")));

    [Fact]
    public void DisbursementChain_WithADuplicateRecipient_IsRejected() =>
        // Decision #53's explicit owner call: paying the same recipient twice in one batch is a caller
        // error, not a legitimate repeat payment.
        Assert.Contains(
            "Leg 3: duplicate recipient account 'R1' -- each leg must have a different recipient.",
            Validate(ChainKind.Disbursement, DisbursementLeg("R1"), DisbursementLeg("R2"), DisbursementLeg("R1")));

    [Fact]
    public void CollectionChain_WithADuplicatePayer_IsRejected() =>
        Assert.Contains(
            "Leg 3: duplicate payer account 'P1' -- each leg must have a different payer.",
            Validate(ChainKind.Collection, CollectionLeg("P1"), CollectionLeg("P2"), CollectionLeg("P1")));
}
