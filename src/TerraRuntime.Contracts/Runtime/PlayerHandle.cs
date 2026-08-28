namespace TerraRuntime.Contracts.Runtime;

/// <summary>
/// Identifies one logical occupation of a reusable Terraria player slot.
/// Zero is reserved for an unassigned/default generation.
/// </summary>
public readonly record struct PlayerSessionGeneration
{
    public PlayerSessionGeneration(ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    public ulong Value { get; }

    public bool IsAssigned => Value != 0;

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Generation-safe identity for one player session. The slot may be reused after this handle expires.
/// </summary>
public readonly record struct PlayerHandle(
    PlayerSlotId Slot,
    PlayerSessionGeneration Generation)
{
    public bool IsAssigned => Generation.IsAssigned;

    public override string ToString() => $"{Slot}/generation:{Generation}";
}

/// <summary>
/// Binds a transport source to one exact occupation of a reusable player slot.
/// </summary>
public readonly record struct ConnectionHandle(
    GameCommandSourceId Source,
    PlayerHandle Player)
{
    public bool IsAssigned => !Source.IsSystem && Player.IsAssigned;

    public override string ToString() => $"{Source}/{Player}";
}
