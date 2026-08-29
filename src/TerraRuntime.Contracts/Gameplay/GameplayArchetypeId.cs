namespace TerraRuntime.Contracts.Gameplay;

/// <summary>
/// Stable host/runtime identity for a server-defined NPC or projectile archetype. This identity is never a
/// Terraria wire/content ID: official clients still receive a separately validated vanilla presentation type.
/// </summary>
public readonly record struct GameplayArchetypeId : IComparable<GameplayArchetypeId>
{
    public const int MaxLength = 128;

    private readonly string? value;

    public GameplayArchetypeId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, MaxLength);

        foreach (char character in value)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
                throw new ArgumentException("Gameplay archetype IDs cannot contain whitespace or control characters.", nameof(value));
        }

        this.value = value;
    }

    public string Value => value ?? string.Empty;

    public bool IsAssigned => value is not null;

    public int CompareTo(GameplayArchetypeId other) => StringComparer.Ordinal.Compare(Value, other.Value);

    public override string ToString() => Value;
}
