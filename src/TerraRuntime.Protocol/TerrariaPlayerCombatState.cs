namespace TerraRuntime.Protocol;

/// <summary>Protocol-neutral packet-117 death-reason projection. Sentinel -1 means the optional source is absent.</summary>
public readonly record struct TerrariaPlayerDeathReasonState(
    short SourcePlayer,
    short SourceNpc,
    short SourceProjectileLocalIndex,
    short SourceOther,
    short SourceProjectileType,
    short SourceItemType,
    short SourceItemPrefix,
    string? CustomReason)
{
    public bool HasPlayer => SourcePlayer >= 0;
    public bool HasNpc => SourceNpc >= 0;
    public bool HasProjectile => SourceProjectileLocalIndex >= 0;
    public bool HasOther => SourceOther >= 0;
}

/// <summary>TerrariaServer 1.4.5.8 packet 117 / PlayerHurtV2.</summary>
public readonly record struct TerrariaPlayerHurtState(
    byte TargetPlayer,
    TerrariaPlayerDeathReasonState Reason,
    short Damage,
    byte HitDirectionWire,
    byte Flags,
    sbyte CooldownCounter)
{
    public int HitDirection => HitDirectionWire - 1;
    public bool Critical => (Flags & 0x01) != 0;
    public bool Pvp => (Flags & 0x02) != 0;
    public bool IsStructurallyValid => HitDirection is >= -1 and <= 1;
}

public enum TerrariaPlayerHurtDecodeResult : byte
{
    Decoded = 0,
    WrongMessageId = 1,
    InvalidPayload = 2,
    InvalidState = 3
}

public enum TerrariaPlayerHurtEncodeResult : byte
{
    Encoded = 0,
    InvalidState = 1,
    Failed = 2
}
