namespace TerraRuntime.Protocol;

/// <summary>
/// Protocol-library-neutral authoritative projection for Terraria packet 23 / NpcUpdate.
/// Position is the already-resolved wire sync-anchor position.
/// Gameplay type stays separate from NetId so the protocol adapter can validate negative variant ids.
/// </summary>
public readonly record struct TerrariaNpcUpdateState(
    byte NpcSlot,
    byte Generation,
    int NpcType,
    float PositionX,
    float PositionY,
    float VelocityX,
    float VelocityY,
    ushort Target,
    int DirectionX,
    int DirectionY,
    int SpriteDirection,
    float Ai0,
    float Ai1,
    float Ai2,
    float Ai3,
    short NpcNetId,
    int Life,
    int LifeMax,
    bool SpawnNeedsSyncing)
{
    // NPC.AI style 82 / ProjectileKey in protocol 326: this AI field is an opaque bit carrier.
    private const int MoonLordLeechBlobType = 401;
    public bool IsValid =>
        Generation != 0 &&
        NpcType > 0 &&
        float.IsFinite(PositionX) &&
        float.IsFinite(PositionY) &&
        float.IsFinite(VelocityX) &&
        float.IsFinite(VelocityY) &&
        Target != ushort.MaxValue &&
        DirectionX is >= -1 and <= 1 &&
        DirectionY is >= -1 and <= 1 &&
        SpriteDirection is -1 or 1 &&
        float.IsFinite(Ai0) && float.IsFinite(Ai2) && float.IsFinite(Ai3) &&
        (NpcType == MoonLordLeechBlobType
            ? ((BitConverter.SingleToUInt32Bits(Ai1) >> 8) & 0x3ff) <= TerrariaProjectileKeyState.MaximumProjectileIndex
            : float.IsFinite(Ai1)) &&
        LifeMax > 0 &&
        Life >= 0 &&
        Life <= LifeMax;
}
