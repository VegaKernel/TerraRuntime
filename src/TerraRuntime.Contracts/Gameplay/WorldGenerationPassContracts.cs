namespace TerraRuntime.Contracts.Gameplay;

/// <summary>Stable, namespaced identity for one world-generation pass.</summary>
public readonly record struct WorldGenerationPassId : IComparable<WorldGenerationPassId>
{
    public const int MaxLength = 128;
    private readonly string? value;

    public WorldGenerationPassId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, MaxLength);
        foreach (char character in value)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
                throw new ArgumentException("World-generation pass IDs cannot contain whitespace or control characters.", nameof(value));
        }

        this.value = value;
    }

    public string Value => value ?? string.Empty;
    public bool IsAssigned => value is not null;
    public int CompareTo(WorldGenerationPassId other) => StringComparer.Ordinal.Compare(Value, other.Value);
    public override string ToString() => Value;
}

public enum WorldGenerationRngMode : byte
{
    IsolatedDeterministic = 0,
    VanillaSharedRng = 1,
    CustomProviderRng = 2
}

/// <summary>
/// Cold-path metadata used to build a deterministic generation plan. RequiredAfter is a hard dependency and must
/// exist. OptionalAfter/OptionalBefore are ordering hints applied only when the referenced pass is present.
/// Arrays are defensively cloned so a host cannot mutate an already staged descriptor behind the runtime's back.
/// </summary>
public sealed class WorldGenerationPassDescriptor
{
    private readonly WorldGenerationPassId[] requiredAfter;
    private readonly WorldGenerationPassId[] optionalAfter;
    private readonly WorldGenerationPassId[] optionalBefore;

    public WorldGenerationPassDescriptor(
        WorldGenerationPassId id,
        WorldGenerationRngMode rngMode = WorldGenerationRngMode.IsolatedDeterministic,
        WorldGenerationPassId[]? requiredAfter = null,
        WorldGenerationPassId[]? optionalAfter = null,
        WorldGenerationPassId[]? optionalBefore = null)
    {
        if (!id.IsAssigned)
            throw new ArgumentException("World-generation pass ID must be assigned.", nameof(id));
        if (!Enum.IsDefined(rngMode))
            throw new ArgumentOutOfRangeException(nameof(rngMode));

        Id = id;
        RngMode = rngMode;
        this.requiredAfter = CloneAndValidate(requiredAfter, nameof(requiredAfter));
        this.optionalAfter = CloneAndValidate(optionalAfter, nameof(optionalAfter));
        this.optionalBefore = CloneAndValidate(optionalBefore, nameof(optionalBefore));
    }

    public WorldGenerationPassId Id { get; }
    public WorldGenerationRngMode RngMode { get; }
    public ReadOnlyMemory<WorldGenerationPassId> RequiredAfter => requiredAfter;
    public ReadOnlyMemory<WorldGenerationPassId> OptionalAfter => optionalAfter;
    public ReadOnlyMemory<WorldGenerationPassId> OptionalBefore => optionalBefore;

    private static WorldGenerationPassId[] CloneAndValidate(WorldGenerationPassId[]? ids, string parameterName)
    {
        if (ids is null || ids.Length == 0)
            return [];

        WorldGenerationPassId[] copy = (WorldGenerationPassId[])ids.Clone();
        foreach (WorldGenerationPassId id in copy)
        {
            if (!id.IsAssigned)
                throw new ArgumentException("World-generation dependencies must use assigned IDs.", parameterName);
        }

        return copy;
    }
}
