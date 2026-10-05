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
    /// <summary>Mount activation is independent of its type: vanilla mount zero is Rudolph.</summary>
    public bool HasMount { get; init; }

    /// <summary>Owned packet-84 visibility value; null means no retained projection (including default snapshots).</summary>
    public float? Stealth { get; init; }
    /// <summary>Source remote zones; null means imported context is not retained.</summary>
    public PlayerZoneSnapshot1458? Zones { get; init; }
    public float Luck { get; init; }
    public VanillaPlayerLuckComponents1458? LuckComponents { get; init; }
    public int? ItemAnimation { get; init; }
    public float? ItemRotation { get; init; }

    public bool Hostile { get; init; }

    public bool GodMode { get; init; }

    public bool HasHealth { get; init; }

    public short Life { get; init; }

    public short MaxLife { get; init; }

    /// <summary>Owned Player.statLifeMax2 after the player phase; null means its source context is unavailable.</summary>
    public int? DerivedLifeMax { get; init; }

    /// <summary>Retained source statLifeMax provenance, including constructor100; null means unknown import.</summary>
    public int? BaseLifeMax { get; init; }

    public PlayerDebuffSnapshot1458? Debuffs { get; init; }

    public bool IsDead { get; init; }

    public bool HasMana { get; init; }

    public short Mana { get; init; }

    public short MaxMana { get; init; }
}
