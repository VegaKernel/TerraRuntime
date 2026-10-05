namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Player-array facts read by NPC AI, independent of combat-player identity.</summary>
public readonly record struct VanillaNpcRawPlayer1458(
    byte Slot, bool Active, bool Dead, bool Ghost,
    float PositionX, float PositionY, int Width, int Height,
    int Aggro, bool NoAggro, int ItemAnimation)
{
    public static VanillaNpcRawPlayer1458 Constructor(byte slot) =>
        new(slot, false, false, false, 0f, 0f, 20, 42, 0, false, 0);

    public bool IsValid => float.IsFinite(PositionX) && float.IsFinite(PositionY) &&
        Width > 0 && Height > 0 && ItemAnimation >= 0;

    public VanillaNpcTargetCandidate Candidate => new(Slot,
        PositionX + Width * .5f, PositionY + Height * .5f, Aggro, Active, Dead, Ghost, NoAggro)
    {
        HitboxWidth = Width,
        HitboxHeight = Height,
        ItemAnimation = ItemAnimation
    };
}

/// <summary>The no-eligible-player branch of NPC.SetTargetTrackingValues.</summary>
public static class VanillaNpcUnoccupiedTarget1458
{
    public static bool TryRefresh(ushort target, int directionX, int directionY,
        float positionX, float positionY, int width, int height,
        in VanillaNpcRawPlayer1458 raw, out VanillaBlueSlimeTargetRefresh refresh)
    {
        refresh = default;
        ushort selected = target < byte.MaxValue ? target : (ushort)0;
        if (!raw.IsValid || raw.Slot != selected || !float.IsFinite(positionX) ||
            !float.IsFinite(positionY) || width <= 0 || height <= 0 ||
            directionX is < -1 or > 1 || directionY is < -1 or > 1)
            return false;

        // The admitted raw owner has no negative-Aggro or tank-pet authority. Preserve
        // that fence rather than inferring the source oldTarget-dependent facing gate.
        if (raw.Aggro < 0 || raw.NoAggro || raw.Ghost)
            return false;
        if (!raw.Dead)
        {
            directionX = (int)raw.PositionX + raw.Width / 2 < positionX + width / 2 ? -1 : 1;
            directionY = (int)raw.PositionY + raw.Height / 2 < positionY + height / 2 ? -1 : 1;
        }
        refresh = new(true, selected, directionX, directionY, PreserveFacing: raw.Dead);
        return true;
    }
}
