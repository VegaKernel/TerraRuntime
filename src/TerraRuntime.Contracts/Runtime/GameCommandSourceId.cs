namespace TerraRuntime.Contracts.Runtime;

/// <summary>
/// Identifies the producer of commands entering the authoritative game loop.
/// Zero is reserved for runtime/system work; positive values identify external sources such as connections.
/// </summary>
public readonly record struct GameCommandSourceId
{
    private GameCommandSourceId(long value)
    {
        Value = value;
    }

    public static GameCommandSourceId System { get; } = new(0);

    public long Value { get; }

    public bool IsSystem => Value == 0;

    public static GameCommandSourceId FromConnection(long connectionId)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(connectionId, 1);
        return new GameCommandSourceId(connectionId);
    }

    public override string ToString() => IsSystem ? "system" : $"connection:{Value}";
}
