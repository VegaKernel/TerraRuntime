using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    private bool TryObserveClientDisposablePlate(ClientTileManipulationRuntimeCommand command)
    {
        var request = command.State;
        if (tiles is null || request.Action != 0 || request.Data != 0 ||
            request.TileX < 3 || request.TileY < 3 || request.TileX >= tiles.Dimensions.WidthTiles - 3 ||
            request.TileY >= tiles.Dimensions.HeightTiles - 3) return false;
        var plate = tiles.Get(request.TileX, request.TileY);
        if (!plate.IsActive || plate.Type != 135 || plate.FrameY / 18 != 7) return false;
        ValidatedClientManipulations++;
        if (TryApplyClientDisposablePlate(command)) AppliedClientManipulations++;
        else
        {
            RejectedClientManipulations++;
            replication?.TryPublishAuthoritativeCorrection(command.Connection.Source, tiles, request.TileX, request.TileY);
        }
        return true;
    }

    private bool TryApplyClientDisposablePlate(ClientTileManipulationRuntimeCommand command)
    {
        // Collision.SwitchTiles sends59 then17 after a player's style7 contact. The
        // dedicated receiver applies KillTile without a selected-tool/ItemCheck phase.
        // The existing source pass owns drops, recursive frames, RNG and adoption.
        if (tiles is null || !players.TryGet(command.Connection, out var member) ||
            !editBudget.TryConsume(command.Connection.Player.Slot) ||
            !TryCaptureToolTreeWorld(out var context) || context is null ||
            !players.TryCaptureToolTreeActors(out var actors) || actors is null) return false;
        var player = member.CaptureSnapshot();
        var overlay = new ToolWorldTileOverlay1458(tiles);
        if (!TryCreateToolMutationPlan(overlay, out var plan) || plan is null) return false;
        using (plan)
        {
            if (!TryCreateToolSourcePass(overlay, plan.SourceRandom, false, context.HardMode,
                    context.Facts, actors.Actors, plan, out var source) || source is null ||
                !source.TryKillTile(command.State.TileX, command.State.TileY, false, false, false, out _) ||
                plan.ShapeEffects.Length != 0 ||
                !plan.TrySeal() || !context.IsCurrent || !actors.IsCurrent || !source.IsCurrentOwned ||
                member.CaptureSnapshot() != player || !players.TryGet(command.Connection, out var current) ||
                !ReferenceEquals(member, current) ||
                !plan.TryAdoptWholeUnpublished(out var publication) || publication is null) return false;
            var end = plan.CapturePublicationOffset();
            if (!publication.TryPublishThrough(in end))
                throw new InvalidOperationException("Accepted disposable plate lost its source publication boundary.");
            replication?.TryPublishCommitted(command.Connection.Source, command.State);
            return true;
        }
    }
}
