namespace TerraRuntime.Contracts.Runtime;

/// <summary>
/// Identifies one live activation of a runtime world.
/// Restarting the same logical <see cref="WorldRuntimeId"/> creates a new session ID so stale cross-world handles can be rejected.
/// </summary>
public readonly record struct WorldSessionId
{
    public WorldSessionId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "World session identity cannot be empty.");
        }

        Value = value;
    }

    public Guid Value { get; }

    public bool IsAssigned => Value != Guid.Empty;

    public static WorldSessionId CreateNew() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}
