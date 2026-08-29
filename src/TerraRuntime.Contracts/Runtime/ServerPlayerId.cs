namespace TerraRuntime.Contracts.Runtime;

/// <summary>
/// Stable control-plane identity for a runtime-owned player. It is independent from the reusable Terraria wire slot;
/// the current <see cref="PlayerHandle"/> is generation-safe but may change after despawn/recreation.
/// </summary>
public readonly record struct ServerPlayerId : IComparable<ServerPlayerId>
{
    public const int MaxLength = 128;
    private readonly string? value;

    public ServerPlayerId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, MaxLength);
        foreach (char character in value)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
            {
                throw new ArgumentException(
                    "Server player IDs cannot contain whitespace or control characters.",
                    nameof(value));
            }
        }

        this.value = value;
    }

    public string Value => value ?? string.Empty;

    public bool IsAssigned => value is not null;

    public int CompareTo(ServerPlayerId other) =>
        StringComparer.Ordinal.Compare(Value, other.Value);

    public override string ToString() => Value;
}
