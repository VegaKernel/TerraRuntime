namespace TerraRuntime.Protocol;

/// <summary>
/// Protocol-library-neutral authoritative projection for Terraria packet 23 / NpcUpdate.
/// Position is the wire sync-anchor position; ordinary supported NPCs currently use a zero SyncAnchor.
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
        float.IsFinite(Ai0) &&
        float.IsFinite(Ai1) &&
        float.IsFinite(Ai2) &&
        float.IsFinite(Ai3) &&
        LifeMax > 0 &&
        Life >= 0 &&
        Life <= LifeMax;
}
