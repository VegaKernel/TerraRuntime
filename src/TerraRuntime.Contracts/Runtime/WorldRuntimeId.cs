namespace TerraRuntime.Contracts.Runtime;

/// <summary>
/// Stable identity of one logical runtime-world instance.
/// A cloned or independently created sandbox receives a different runtime ID even when it originates from the same .wld/template.
/// </summary>
public readonly record struct WorldRuntimeId
{
    public WorldRuntimeId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "World runtime identity cannot be empty.");
        }

        Value = value;
    }

    public Guid Value { get; }

    public bool IsAssigned => Value != Guid.Empty;

    public static WorldRuntimeId CreateNew() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}
