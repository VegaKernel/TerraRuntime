namespace TerraRuntime.Contracts.Runtime;

/// <summary>
/// Identifies one version of authoritative state within a player session.
/// Zero is reserved for an unassigned/default revision.
/// </summary>
public readonly record struct PlayerStateRevision
{
    public PlayerStateRevision(ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    public ulong Value { get; }

    public bool IsAssigned => Value != 0;

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Immutable, protocol-neutral projection of authoritative player simulation state.
/// Connection/network details and inventory are intentionally separate concerns.
/// Vitals remain explicitly optional until their vanilla sync packets have been observed for this session.
/// </summary>
public readonly record struct PlayerStateSnapshot(
    PlayerHandle Player,
    PlayerStateRevision Revision,
    byte Team,
    byte ControlFlags,
    byte MovementFlags,
    byte MiscFlags1,
    byte MiscFlags2,
    byte SelectedItem,
    float PositionX,
    float PositionY,
    float VelocityX,
    float VelocityY,
    ushort MountType,
    float PotionOfReturnOriginalPositionX,
    float PotionOfReturnOriginalPositionY,
    float PotionOfReturnHomePositionX,
    float PotionOfReturnHomePositionY,
    float CameraTargetX,
    float CameraTargetY)
{
    public bool Hostile { get; init; }

    public bool HasHealth { get; init; }

    public short Life { get; init; }

    public short MaxLife { get; init; }

    public bool IsDead { get; init; }

    public bool HasMana { get; init; }

    public short Mana { get; init; }

    public short MaxMana { get; init; }
}
