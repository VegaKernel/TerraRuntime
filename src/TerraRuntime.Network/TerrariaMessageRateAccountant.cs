namespace TerraRuntime.Network;

/// <summary>
/// Per-connection fixed-window accounting for message ids that have an explicitly configured budget.
/// Unconfigured message ids remain unrestricted here and are still covered by the connection-wide accountant.
/// </summary>
public sealed class TerrariaMessageRateAccountant
{
    private readonly TerrariaConnectionRateAccountant?[] _accountants;

    public TerrariaMessageRateAccountant(
        ConnectionMessageRateLimits limits,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(limits);
        _accountants = new TerrariaConnectionRateAccountant?[byte.MaxValue + 1];

        for (int messageId = 0; messageId <= byte.MaxValue; messageId++)
        {
            if (limits.TryGet((byte)messageId, out ConnectionRateBudgetOptions budget))
            {
                _accountants[messageId] = new TerrariaConnectionRateAccountant(budget, timeProvider);
            }
        }
    }

    public ConnectionRateDecision Observe(byte messageId, int frameBytes)
    {
        TerrariaConnectionRateAccountant? accountant = _accountants[messageId];
        return accountant is null
            ? ConnectionRateDecision.Allowed
            : accountant.Observe(frameBytes);
    }

    public ConnectionRateSnapshot GetSnapshot(byte messageId) =>
        _accountants[messageId]?.Snapshot ?? default;
}
