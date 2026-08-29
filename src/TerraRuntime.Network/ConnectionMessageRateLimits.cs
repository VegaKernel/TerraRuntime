using TerraRuntime.Protocol;

namespace TerraRuntime.Network;

public readonly record struct ConnectionMessageRateRule(
    byte MessageId,
    ConnectionRateBudgetOptions Budget);

/// <summary>
/// Immutable lookup of optional fixed-window budgets for individual Terraria message ids.
/// Gameplay-sensitive tuning stays in policy configuration; the built-in hard-abuse profile only places
/// deliberately high emergency ceilings on packet classes that otherwise permit cheap flood amplification.
/// </summary>
public sealed class ConnectionMessageRateLimits
{
    private readonly ConnectionRateBudgetOptions?[] _budgets;

    public static ConnectionMessageRateLimits None { get; } = new();

    /// <summary>
    /// Conservative server guardrails for known high-frequency or fan-out-producing inbound packet classes.
    /// Limits are intentionally far above the 60 Hz simulation rate so they reject obvious floods rather than
    /// define gameplay cadence. Deployments can replace this profile with measured policy-specific budgets.
    /// </summary>
    public static ConnectionMessageRateLimits HardAbuse { get; } = new(
        Rule(TerrariaMessageId.PlayerControls, maxFrames: 600, maxBytes: 96 * 1024),
        Rule(TerrariaMessageId.SyncEquipment, maxFrames: 600, maxBytes: 64 * 1024),
        Rule(TerrariaMessageId.PlayerHp, maxFrames: 240, maxBytes: 32 * 1024),
        Rule(TerrariaMessageId.PlayerMana, maxFrames: 240, maxBytes: 32 * 1024),
        Rule(TerrariaMessageId.WorldItemDrop, maxFrames: 240, maxBytes: 64 * 1024),
        Rule(TerrariaMessageId.WorldItemOwner, maxFrames: 240, maxBytes: 32 * 1024),
        Rule(TerrariaMessageId.ProjectileNew, maxFrames: 1_200, maxBytes: 256 * 1024),
        Rule(TerrariaMessageId.ProjectileDestroy, maxFrames: 1_200, maxBytes: 128 * 1024));

    public ConnectionMessageRateLimits(params ConnectionMessageRateRule[] rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _budgets = new ConnectionRateBudgetOptions?[byte.MaxValue + 1];

        for (int i = 0; i < rules.Length; i++)
        {
            ConnectionMessageRateRule rule = rules[i];
            ValidateBudget(rule.Budget, nameof(rules));

            if (_budgets[rule.MessageId].HasValue)
            {
                throw new ArgumentException(
                    $"A rate budget for Terraria message id {rule.MessageId} is already configured.",
                    nameof(rules));
            }

            _budgets[rule.MessageId] = rule.Budget;
        }
    }

    public int Count
    {
        get
        {
            int count = 0;
            for (int i = 0; i < _budgets.Length; i++)
            {
                if (_budgets[i].HasValue)
                    count++;
            }

            return count;
        }
    }

    public bool TryGet(byte messageId, out ConnectionRateBudgetOptions budget)
    {
        ConnectionRateBudgetOptions? configured = _budgets[messageId];
        if (configured.HasValue)
        {
            budget = configured.GetValueOrDefault();
            return true;
        }

        budget = default;
        return false;
    }

    private static ConnectionMessageRateRule Rule(
        TerrariaMessageId messageId,
        int maxFrames,
        long maxBytes) =>
        new(
            (byte)messageId,
            new ConnectionRateBudgetOptions(
                TimeSpan.FromSeconds(1),
                maxFrames,
                maxBytes));

    private static void ValidateBudget(ConnectionRateBudgetOptions budget, string parameterName)
    {
        if (budget.Window <= TimeSpan.Zero || budget.MaxFrames is <= 0 || budget.MaxBytes is <= 0)
        {
            throw new ArgumentException("Message rate budgets must use valid positive limits and window values.", parameterName);
        }
    }
}
