using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    private void ApplyMassWiring(ClientMassWiringRuntimeCommand1458 command)
    {
        ClientManipulationRequests++;
        if (!TryApplyMassWiring(command)) { RejectedClientManipulations++; return; }
        AppliedClientManipulations++;
    }

    private bool TryApplyMassWiring(ClientMassWiringRuntimeCommand1458 command)
    {
        if (tiles is null || !players.TryGet(command.Connection, out _) || !editBudget.TryConsume(command.Connection.Player.Slot) ||
            !players.TryPrepareMassWiringPlayer(command.Connection, out var player) || player is null ||
            player.World.MaxTilesX != tiles.Dimensions.WidthTiles || player.World.MaxTilesY != tiles.Dimensions.HeightTiles)
            return false;
        bool golem = golemDownedBaseline || progression.IsCompleted(VanillaWorldProgressionId.Golem);
        var reads = CreateToolReadPreparation();
        if (reads is null) return false;
        bool CanUse(int x, int y, WorldTile tile) => reads.TryReadBefore(x, y, out var before) && before.Equals(tile) &&
            CanUseWiringToolHere(player.World, tile.Wall, y, golem);
        var request = command.State;
        if (!WorldMassWiring1458.TryPlan(tiles, request.StartX, request.StartY, request.EndX, request.EndY,
            (player.Before.ControlFlags & 64) != 0, request.Mode,
            player.Stock(VanillaWiringToolCatalog1458.WireItem), player.Stock(VanillaWiringToolCatalog1458.ActuatorItem),
            CanUse, out var planned) || planned is null) return false;
        var overlay = new ToolWorldTileOverlay1458(tiles);
        if (!TryCreateToolMutationPlan(overlay, out var world) || world is null) return false;
        using (world)
        {
            foreach (var edit in planned.Edits)
                if (!world.TryStageSourceWiringCell(edit.X, edit.Y, edit.Before, edit.After) ||
                    !world.TryStageSourceTileManipulation(edit.X, edit.Y, edit.Action)) return false;
            var (width, height) = player.Before.HasMount ? VanillaPlayerMountHitbox1458.Resolve(player.Before.MountType) : (20f, 42f);
            int centerX = (int)(player.Before.PositionX + width / 2), centerY = (int)(player.Before.PositionY + height / 2);
            bool Drops(int type, int count)
            {
                while (count > 0)
                {
                    int stack = Math.Min(9999, count); // Item.SetDefaults530/849 maxStack, original DropCache splitting.
                    if (!world.TryStageSourceItemRectangle(new(type), (short)stack, centerX, centerY, 0, 0)) return false;
                    count -= stack;
                }
                return true;
            }
            if (!Drops(VanillaWiringToolCatalog1458.WireItem, planned.WireDrops) ||
                !Drops(VanillaWiringToolCatalog1458.ActuatorItem, planned.ActuatorDrops)) return false;
            var end = world.CapturePublicationOffset();
            if (!world.TrySeal() || !player.TrySeal(planned.WiresConsumed, planned.ActuatorsConsumed) ||
                !reads.IsCurrent || !player.IsCurrent || !world.IsCurrentOwned ||
                (golemDownedBaseline || progression.IsCompleted(VanillaWorldProgressionId.Golem)) != golem) return false;
            if (!world.TryAdoptWholeUnpublished(out var publication) || publication is null) return false;
            if (!player.TryAdoptUnpublished())
                throw new InvalidOperationException("Prepared mass-wiring payment changed during joint adoption.");
            publication.TryPublishThrough(end);
            replication?.TryPublishMassWiringPayments(command.Connection, (short)planned.WiresConsumed, (short)planned.ActuatorsConsumed);
            return true;
        }
    }
}
