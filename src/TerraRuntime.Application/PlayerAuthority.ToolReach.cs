using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal readonly record struct RuntimePlayerToolReach1458(int TileRangeX, int TileRangeY, int TileBoost)
{
    internal bool Contains(in RuntimePlayerToolStagePose1458 pose, int tileX, int tileY)
    {
        if (TileRangeX is < 0 or > 100 || TileRangeY is < 0 or > 100 ||
            TileBoost is < -20 or > 100 || pose.Width <= 0 || pose.Height <= 0 ||
            !float.IsFinite(pose.Player.PositionX) || !float.IsFinite(pose.Player.PositionY)) return false;
        // TileReachCheckSettings.Simple: cap global range BEFORE adding item boost.
        int x = Math.Min(TileRangeX, 20) + TileBoost;
        int y = Math.Min(TileRangeY, 20) + TileBoost;
        int lowX = (int)(pose.Player.PositionX / 16f) - x;
        int lowY = (int)(pose.Player.PositionY / 16f) - y;
        int highX = (int)Math.Ceiling((pose.Player.PositionX + pose.Width) / 16f) - 1 + x;
        int highY = (int)Math.Ceiling((pose.Player.PositionY + pose.Height) / 16f) - 1 + y;
        return tileX >= lowX && tileX <= highX && tileY >= lowY && tileY <= highY;
    }
}

// An authority admission witness, not reconstructed client cursor history.
internal sealed record RuntimePlayerToolRelease1458(ConnectionHandle Connection,
    RuntimePlayerToolStagePose1458 Pose, RuntimePlayerToolReach1458 Reach,
    RuntimeToolBuildingCapture1458 Building, ToolDefinition1458 Tool, byte Slot, long Update)
{
    internal bool IsCurrent => Building.IsCurrent && !Building.NoBuilding;
}

internal sealed partial class PlayerAuthority
{
    internal bool TryCaptureToolReach(RuntimePlayerToolActorCapture1458 capture,
        out RuntimePlayerToolReach1458 reach)
    {
        reach = default;
        if (!capture.IsCurrent || !membership.TryGet(capture.Connection, out var member) ||
            member.RemotePhaseSnapshot != capture.Actor.Player || member.RemotePhaseItem != capture.Actor.ItemPhase ||
            member.RemotePhaseInputRevision != capture.Actor.InputRevision ||
            capture.Phase.ToolUse is not { Reach: { } owned } ||
            capture.World.JourneyMode is not false) return false;
        reach = owned;
        return true;
    }

    private bool TryResolveToolReach(in RuntimePlayerUpdateWorld1458 world,
        in ToolDefinition1458 tool, ReadOnlySpan<PlayerEquipmentCommitRequest> equipment,
        out RuntimePlayerToolReach1458 reach)
    {
        reach = default;
        // No represented owner of Journey FarPlacementRange exists. Unknown is
        // not disabled. Range accessories likewise stay outside this projection.
        if (world.JourneyMode is not false) return false;
        foreach (var item in equipment)
        {
            int index = item.SlotId - VanillaPlayerItemSlotCatalog.ArmorStart;
            if (item.Stack > 0 && index is >= 3 and <= 9 &&
                item.ItemNetId is 2215 or 3061 or 5126) return false;
        }
        // The proven whole-phase equipment census has already excluded active
        // inherited/loadout range writers. No Artisan Loaf expansion is applied.
        reach = new(5, 3, tool.TileBoost);
        return true;
    }
}
