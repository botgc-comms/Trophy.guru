namespace Trophy.Catalogue.Domain;

/// <summary>The advertised annual offer; availability never follows from price alone.</summary>
public sealed record IntegrationOffer(string Code, string Name, string BillingInterval, string Currency, long AmountPence, bool Available, string Status, string? AvailabilityReason)
{
    public const long IntelligentGolfAmountPence = 29900;
    public static IntegrationOffer IntelligentGolf(bool available, string status, string? reason = null) =>
        new("intelligent-golf", "Intelligent Golf integration", "year", "gbp", IntelligentGolfAmountPence, available, status, reason);
}

public sealed record IntegrationCheckoutOrder(string Id, string ClubId, string PriceId, string? CheckoutId, string? CheckoutUrl, long CreatedAt);

/// <summary>Billing status only; purchasing a subscription does not install a website integration.</summary>
public sealed record IntegrationSubscriptionState(bool Exists, bool Current, string Status, DateTimeOffset? PaidThrough);
