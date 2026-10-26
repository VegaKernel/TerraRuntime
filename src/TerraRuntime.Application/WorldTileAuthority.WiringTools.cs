using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    private const int WireItem1458 = 530;
    private const int WiringResourceSlots1458 = 58;
    private const ushort ForbiddenWiringWall1458 = 350;
    private const ushort UnsafeTempleWall1458 = 87;

    private bool TryObserveClientWiringTile(ClientTileManipulationRuntimeCommand command)
    {
        if (command.State.Action is not (5 or 6 or 8 or 9 or 10 or 11 or 12 or 13 or 16 or 17)) return false;
        var result = players.ObserveWiringReport(command.Connection, command.State, out var input);
        // Wire actions never fall through to the legacy unpaid tile writer.
        ValidatedClientManipulations++;
        if (result != ToolReportObservationResult1458.Retained || input is null ||
            input.WiringReads is null && !TryBindWiringBeforeRead(input, command.State.TileX, command.State.TileY))
        {
            RejectedToolReports++; RejectedClientManipulations++;
            if (tiles is { } world) replication?.TryPublishAuthoritativeCorrection(command.Connection.Source,
                world, command.State.TileX, command.State.TileY);
            return true;
        }
        RetainedToolReports++;
        TryCommitObservedWiring(input);
        return true;
    }

    private bool TryBindWiringBeforeRead(RuntimePlayerToolInput1458 input, int x, int y)
    {
        var reads = CreateToolReadPreparation();
        return reads is not null && reads.TryReadBefore(x, y, out _) && input.TryBindWiringReads(reads);
    }

    internal bool TryCommitObservedWiring(RuntimePlayerToolInput1458 input,
        ToolActionPublicationBatch1458? deferred = null)
    {
        WiringPreparation1458? preparation = null;
        try
        {
            if (!TryPrepareWiring(input, out preparation) || preparation is null ||
                !preparation.TrySeal() || !preparation.TryAdoptUnpublished()) return false;
            CommittedToolActions++;
            if (deferred is null) preparation.TryPublishAll();
            else { deferred.AddWiring(preparation); preparation = null; }
            return true;
        }
        finally { preparation?.Dispose(); }
    }

    private bool TryPrepareWiring(RuntimePlayerToolInput1458 input, out WiringPreparation1458? preparation)
    {
        preparation = null;
        if (!input.IsWiring || !input.IsCurrent || input.Actor is not { HasCurrentSourceStage: true } capture ||
            input.WiringReads is not { IsCurrent: true } reads || input.Count != 1 ||
            input.Observations[0].Tile is not { Data: 0, Style: 0 } report ||
            capture.Phase.ToolUse is not { SourceWiring: { } definition, SourceStagePose: { } pose,
                Reach: { } reach, RequiresFreshUse: false, ToolStagePending: false } tools ||
            tools.Clocks.Selected.ItemTime != 0 || tools.Clocks.Selected.Animation <= 0 ||
            definition.Item != capture.Selected.ItemType || capture.Selected.Prefix.Value != 0 ||
            (pose.Player.ControlFlags & 32) == 0 || !reach.Contains(pose, report.TileX, report.TileY) ||
            !players.TryPrepareToolAttempt(capture.Connection, out var player) || player is null || tiles is null)
            return false;
        bool golem = golemDownedBaseline || progression.IsCompleted(VanillaWorldProgressionId.Golem);
        var overlay = new ToolWorldTileOverlay1458(tiles);
        if (!overlay.TryRead(report.TileX, report.TileY, out var before) ||
            !CanUseWiringToolHere(capture.World, before.Wall, report.TileY, golem) ||
            !TryPlanWiringCell(definition, before, out var after, out byte action) ||
            report.Action != action) return false;
        short resourceSlot = -1;
        if (definition.Kind == WiringToolKind1458.Wire)
        {
            for (short slot = 0; slot < WiringResourceSlots1458; slot++)
                if (capture.InventoryBefore[slot] is { Stack: > 0 } item && item.ItemType.Value == WireItem1458)
                { resourceSlot = slot; break; }
            if (resourceSlot < 0) return false;
        }
        else if (definition.Kind == WiringToolKind1458.Actuator)
        {
            resourceSlot = capture.Actor.Player.SelectedItem;
            if (resourceSlot >= WiringResourceSlots1458) return false;
        }
        if (resourceSlot >= 0 && input.WiringInventoryReport is null ||
            !VanillaWiringTool1458.TryPlanTail(definition, tools.Clocks, capture.Selected.Stack, out var clocks) ||
            !TryCreateToolMutationPlan(overlay, out var world) || world is null) return false;
        bool handedOff = false;
        try
        {
            if (!world.TryStageSourceWiringCell(report.TileX, report.TileY, before, after)) return false;
            var candidate = new WiringPreparation1458(this, player, input, reads, world,
                report, before, after, clocks, resourceSlot, golem);
            if (!candidate.IsCurrent) return false;
            preparation = candidate; handedOff = true; return true;
        }
        finally { if (!handedOff) world.Dispose(); }
    }

    private static bool CanUseWiringToolHere(in RuntimePlayerUpdateWorld1458 world,
        ushort wall, int y, bool golem)
    {
        // Player.CanDoWireStuffHere / DungeonGenerationStyles.Temple (1458).
        if (wall == ForbiddenWiringWall1458) return false;
        if (golem) return true;
        if (wall == UnsafeTempleWall1458) return false;
        if (y <= world.WorldSurface || wall is not (92 or 42 or 24)) return true;
        return world.DualDungeonsSeed is false;
    }

    private static bool TryPlanWiringCell(in WiringToolDefinition1458 definition, in WorldTile before,
        out WorldTile after, out byte action)
    {
        after = before; action = definition.PlaceAction;
        var kind = definition.Kind == WiringToolKind1458.Actuator
            ? WorldWiringMutationKind.PlaceActuator : WorldWiringMutationKind.PlaceWire;
        var channel = (WorldWireChannel)definition.Channel;
        var request = new WorldWiringMutationRequest(kind, 0, 0, channel);
        var result = VanillaWorldWiringMutationService.Evaluate(request, before);
        after = result.After;
        return result.Applied;
    }

    internal sealed partial class ToolWorldMutationPlan
    {
        internal bool PreserveLiquidQueueAfterDryWiringFrames()
        {
            if (!IsCurrentOwned || sealedPlan) return false;
            // The source pass has rejected every wet TileFrame visit. A wet
            // read-only torch-support halo must not become a geometry wakeup.
            for (int index = 0; index < mutations.Count; index++)
                mutations[index] = mutations[index] with { PreserveLiquidQueue = true };
            return true;
        }

        internal bool TryStageSourceWiringCell(int x, int y, in WorldTile before, in WorldTile after)
        {
            if (!TryStageSourceCell(WorldTileMutationKind.TransformTile, x, y, before, after)) return false;
            // WorldGen.Place/KillWire and Place/KillActuator do not wake Liquid.
            mutations[^1] = mutations[^1] with { PreserveLiquidQueue = true };
            return true;
        }
    }
}
