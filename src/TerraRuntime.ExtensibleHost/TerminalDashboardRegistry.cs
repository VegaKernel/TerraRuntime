using TerraRuntime.HostContracts.TerminalUI;

namespace TerraRuntime.ExtensibleHost;

internal sealed class TerminalDashboardRegistry :
    ITerraRuntimeTerminalDashboardRegistry,
    ITerraRuntimeTerminalDashboardSource
{
    private const int MaximumDashboards = 32;

    private readonly object gate = new();
    private readonly Dictionary<string, ITerraRuntimeTerminalDashboardProvider> providers =
        new(StringComparer.OrdinalIgnoreCase);

    public bool TryRegister(ITerraRuntimeTerminalDashboardProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        string id = NormalizeId(provider.Id);
        if (string.IsNullOrWhiteSpace(provider.Title))
            throw new ArgumentException("Terminal dashboard title must not be empty.", nameof(provider));

        lock (gate)
        {
            if (providers.Count >= MaximumDashboards || providers.ContainsKey(id))
                return false;

            providers.Add(id, provider);
            return true;
        }
    }

    public bool TryUnregister(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        lock (gate)
            return providers.Remove(id.Trim());
    }

    public ReadOnlyMemory<ITerraRuntimeTerminalDashboardProvider> CaptureDashboards()
    {
        lock (gate)
        {
            if (providers.Count == 0)
                return ReadOnlyMemory<ITerraRuntimeTerminalDashboardProvider>.Empty;

            ITerraRuntimeTerminalDashboardProvider[] snapshot = providers.Values.ToArray();
            Array.Sort(
                snapshot,
                static (left, right) =>
                {
                    int title = StringComparer.OrdinalIgnoreCase.Compare(left.Title, right.Title);
                    return title != 0
                        ? title
                        : StringComparer.OrdinalIgnoreCase.Compare(left.Id, right.Id);
                });
            return snapshot.AsMemory();
        }
    }

    public Scope CreateScope() => new(this);

    internal void Clear()
    {
        lock (gate)
            providers.Clear();
    }

    private static string NormalizeId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        string normalized = id.Trim();
        if (normalized.Length > 64)
            throw new ArgumentOutOfRangeException(nameof(id), "Terminal dashboard id cannot exceed 64 characters.");

        for (int index = 0; index < normalized.Length; index++)
        {
            char value = normalized[index];
            if (!(char.IsAsciiLetterOrDigit(value) || value is '.' or '-' or '_'))
            {
                throw new ArgumentException(
                    "Terminal dashboard id may contain only ASCII letters, digits, '.', '-' and '_'.",
                    nameof(id));
            }
        }

        return normalized;
    }

    internal sealed class Scope : ITerraRuntimeTerminalDashboardRegistry, IDisposable
    {
        private readonly TerminalDashboardRegistry owner;
        private readonly object gate = new();
        private readonly List<string> registrations = [];
        private bool disposed;

        public Scope(TerminalDashboardRegistry owner) => this.owner = owner;

        public bool TryRegister(ITerraRuntimeTerminalDashboardProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (!owner.TryRegister(provider))
                    return false;

                registrations.Add(NormalizeId(provider.Id));
                return true;
            }
        }

        public bool TryUnregister(string id)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            string normalized = NormalizeId(id);
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                bool removed = owner.TryUnregister(normalized);
                if (removed)
                    registrations.RemoveAll(candidate =>
                        string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase));
                return removed;
            }
        }

        public void Dispose()
        {
            string[] snapshot;
            lock (gate)
            {
                if (disposed)
                    return;

                disposed = true;
                snapshot = registrations.ToArray();
                registrations.Clear();
            }

            for (int index = snapshot.Length - 1; index >= 0; index--)
                owner.TryUnregister(snapshot[index]);
        }
    }
}
