namespace TerraRuntime.Protocol;

/// <summary>
/// Protocol-neutral projection of TerrariaServer 1.4.5.8 packet 28 / StrikeNPC.
/// Damage remains signed at the transport boundary because the vanilla server clamps negative wire damage to zero
/// only after the NPC generation check. HitDirectionWire is the raw source byte and maps to semantic -1..254.
/// </summary>
public readonly record struct TerrariaNpcDamageState(
    byte NpcSlot,
    byte Generation,
    short Damage,
    float KnockBack,
    byte HitDirectionWire,
    byte CriticalRaw)
{
    public int HitDirection => HitDirectionWire - 1;
    public bool Critical => CriticalRaw == 1;

    public bool IsStructurallyValid =>
        Generation != 0 &&
        float.IsFinite(KnockBack);
}

public enum TerrariaNpcDamageDecodeResult : byte
{
    Decoded = 0,
    WrongMessageId = 1,
    InvalidPayloadLength = 2,
    InvalidState = 3
}

public enum TerrariaNpcDamageEncodeResult : byte
{
    Encoded = 0,
    InvalidState = 1,
    FrameTooLarge = 2,
    Failed = 3
}
