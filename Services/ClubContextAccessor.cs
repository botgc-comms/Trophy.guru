namespace Trophy.Catalogue.Services;

public sealed class ClubContextAccessor
{
    private readonly AsyncLocal<string?> current = new();

    public string? CurrentClubId => current.Value;

    public string RequireClubId() => current.Value
        ?? throw new InvalidOperationException("A club context is required for this operation.");

    public IDisposable Push(string clubId)
    {
        var previous = current.Value;
        current.Value = clubId;
        return new RestoreScope(() => current.Value = previous);
    }

    private sealed class RestoreScope(Action restore) : IDisposable
    {
        private Action? restoreAction = restore;
        public void Dispose() => Interlocked.Exchange(ref restoreAction, null)?.Invoke();
    }
}
