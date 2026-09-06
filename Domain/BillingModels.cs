namespace Trophy.Catalogue.Domain;

public sealed record TrophyCreditPack(string Code, int Credits, long AmountPence)
{
    public static IReadOnlyList<TrophyCreditPack> All { get; } = [new("single", 1, 750), new("club", 10, 6000), new("collection", 50, 22500), new("complete", 250, 62500)];
    public static TrophyCreditPack Find(string code, int? credits = null)
    {
        var pack = All.FirstOrDefault(x => x.Code == code)
            ?? throw new BillingException("invalid_pack", "Choose an available trophy credit pack.");
        if (credits is null || credits == pack.Credits) return pack;
        if (code != "complete" || credits < 250)
            throw new BillingException("invalid_quantity", "Choose at least 250 trophies for the £2.50 volume rate.", 400);
        return pack with { Credits = credits.Value, AmountPence = credits.Value * 250L };
    }
}

public sealed record BillingBalance(string ClubId, bool Unlimited, long Available, long Reserved, long Used, bool OnHold, string? CustomerId);
public sealed record BillingQuote(string PackCode, int Credits, long AmountPence, string Currency, string? UpgradeFrom);
public sealed record BillingPurchase(string Id, string ClubId, string PackCode, int Credits, long AmountPence, string State, string? UpgradeFrom, string? CheckoutId, string? CheckoutUrl, string? PaymentId, string? RequestId = null);
public sealed record BillingCheckoutInput(string PackCode, string RequestId, string? UpgradeFrom = null, int? Credits = null);
public sealed record DurableBillableJob(string Id, string ClubId, string TrophyId, string Kind, string State, string Message, DateTimeOffset DueAt, DateTimeOffset UpdatedAt, int EvidenceCount);
public sealed class BillingException(string code, string message, int statusCode = 409) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}
