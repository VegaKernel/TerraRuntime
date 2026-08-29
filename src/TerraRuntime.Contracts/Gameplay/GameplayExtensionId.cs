namespace TerraRuntime.Contracts.Gameplay;

/// <summary>
/// Stable host/runtime identity for a gameplay extension registration. The value is intentionally opaque to
/// TerraRuntime: hosts own naming policy while the runtime requires a bounded, deterministic, whitespace-free ID.
/// </summary>
public readonly record struct GameplayExtensionId : IComparable<GameplayExtensionId>
{
    public const int MaxLength = 128;

    private readonly string? value;

    public GameplayExtensionId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, MaxLength);

        foreach (char character in value)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
            {
                throw new ArgumentException("Gameplay extension IDs cannot contain whitespace or control characters.", nameof(value));
            }
        }

        this.value = value;
    }

    public string Value => value ?? string.Empty;

    public bool IsAssigned => value is not null;

    public int CompareTo(GameplayExtensionId other) => StringComparer.Ordinal.Compare(Value, other.Value);

    public override string ToString() => Value;
}
