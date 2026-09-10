namespace Trophy.Catalogue.Services;

public sealed record AdminRegistration(string Id, string Name, string Email, DateTimeOffset CreatedAt,
    bool EmailVerified, string? ClubId, string? ClubName, string Role);

public sealed partial class AccountStore
{
    public async Task<IReadOnlyList<AdminRegistration>> GetAdminRegistrationsAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return state.Accounts.OrderByDescending(a => a.CreatedAt).Select(a => new AdminRegistration(
                a.Id, a.DisplayName, a.Email, a.CreatedAt, a.EmailVerifiedAt.HasValue, a.ClubId,
                state.Clubs.FirstOrDefault(c => c.Id == a.ClubId)?.Name, a.Role)).ToArray();
        }
        finally { gate.Release(); }
    }
}
