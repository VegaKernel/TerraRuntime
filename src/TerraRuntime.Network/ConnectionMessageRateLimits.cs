namespace TerraRuntime.Network;

public readonly record struct ConnectionMessageRateRule(
    byte MessageId,
    ConnectionRateBudgetOptions Budget);

/// <summary>
/// Immutable lookup of optional fixed-window budgets for individual Terraria message ids.
/// Thresholds are intentionally supplied by policy configuration rather than guessed in the protocol layer.
/// </summary>
public sealed class ConnectionMessageRateLimits
{
    private readonly ConnectionRateBudgetOptions?[] _budgets;

    public static ConnectionMessageRateLimits None { get; } = new();

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

    private static void ValidateBudget(ConnectionRateBudgetOptions budget, string parameterName)
    {
        if (budget.Window <= TimeSpan.Zero || budget.MaxFrames is <= 0 || budget.MaxBytes is <= 0)
        {
            throw new ArgumentException("Message rate budgets must use valid positive limits and window values.", parameterName);
        }
    }
}
