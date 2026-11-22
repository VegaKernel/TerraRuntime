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
    // One bounded source-UI afterimage. It is never a clock or action grant.
    internal RuntimePlayerReleasedToolClockAlternative1458? BufferedClockAlternative { get; init; }
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
            capture.World.JourneyMode is null) return false;
        reach = owned;
        return true;
    }

    internal static bool TryResolveToolReach(in RuntimePlayerUpdateWorld1458 world,
        in ToolDefinition1458 tool, ReadOnlySpan<PlayerEquipmentCommitRequest> equipment,
        in VanillaPlayerCombatEquipmentContext context,
        out RuntimePlayerToolReach1458 reach)
    {
        reach = default;
        if (world.JourneyMode is null ||
            !VanillaPlayerCombatEquipmentCatalog.TryCaptureToolAccessories(equipment, in context,
                out var accessories)) return false;
        // TerrariaServer 1.4.5.8 Player.UpdateEquips raises the owner's global
        // tileRangeX/Y once when any tile-range accessory is equipped. ItemCheck
        // then applies the selected tool's tileBoost after Simple's range cap.
        // The proven whole-phase census still owns which equipment is usable.
        // Unowned Journey FarPlacementRange power is not inferred from this item.
        reach = new(accessories.TileRange ? 8 : 5, accessories.TileRange ? 5 : 3, tool.TileBoost);
        return true;
    }

    internal static bool TryResolvePlacementReach(in RuntimePlayerUpdateWorld1458 world,
        in LocalPlacementDefinition1458 placement, ReadOnlySpan<PlayerEquipmentCommitRequest> equipment,
        in VanillaPlayerCombatEquipmentContext context,
        out RuntimePlayerToolReach1458 reach)
    {
        reach = default;
        if (world.JourneyMode is null ||
            !VanillaPlayerCombatEquipmentCatalog.TryCaptureToolAccessories(equipment, in context,
                out var accessories)) return false;
        // Player.PlaceThing_Tiles/Walls passes item.tileBoost + blockRange to
        // TileReachCheckSettings.Simple after UpdateEquips' one-time range bonus.
        // Journey's unowned Far Placement Range power is not inferred.
        reach = new(accessories.TileRange ? 8 : 5, accessories.TileRange ? 5 : 3,
            checked(placement.TileBoost + accessories.BlockRange));
        return true;
    }
}
