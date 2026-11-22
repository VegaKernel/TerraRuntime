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
        if (!players.TryGet(command.Connection, out var member) ||
            !editBudget.TryConsume(command.Connection.Player.Slot)) return false;
        var player = member.CaptureSnapshot();
        if (!TryPrepareDisposablePlateKill(command.State.TileX, command.State.TileY, out var plan) || plan is null)
            return false;
        using (plan)
        {
            if (member.CaptureSnapshot() != player || !players.TryGet(command.Connection, out var current) ||
                !ReferenceEquals(member, current) || !TryCommitDisposablePlateKill(plan)) return false;
            replication?.TryPublishCommitted(command.Connection.Source, command.State);
            return true;
        }
    }

    private void ApplyServerDisposablePlate(int x, int y)
    {
        if (wiring is null || x > short.MaxValue || y > short.MaxValue ||
            !TryPrepareDisposablePlateKill(x, y, out var preview) || preview is null) return;
        // A wire pulse can change frame reads, RNG and allocation state. Release
        // preview claims and execute a fresh KillTile after the original impulse.
        preview.Dispose();
        if (!wiring.HitSwitch(x, y)) return;
        replication?.TryPublishHitSwitch(default, new((short)x, (short)y));
        if (!TryPrepareDisposablePlateKill(x, y, out var kill) || kill is null) return;
        using (kill) _ = TryCommitDisposablePlateKill(kill);
    }

    private bool TryPrepareDisposablePlateKill(int x, int y, out ToolWorldMutationPlan? prepared)
    {
        prepared = null;
        if (tiles is null || !TryCaptureToolTreeWorld(out var context) || context is null ||
            !players.TryCaptureToolTreeActors(out var actors) || actors is null) return false;
        var overlay = new ToolWorldTileOverlay1458(tiles);
        if (!TryCreateToolMutationPlan(overlay, out var plan) || plan is null) return false;
        try
        {
            if (!overlay.TryReadSourceSafely(x, y, out var tile) ||
                !tile.IsActive || tile.Type != 135 || tile.FrameY / 18 != 7 ||
                !TryCreateToolSourcePass(overlay, plan.SourceRandom, false, context.HardMode,
                    context.Facts, actors.Actors, plan, out var source) || source is null ||
                !source.TryKillTile(x, y, false, false, false, out var result) || !result.Changed || result.SourceFailed ||
                plan.ShapeEffects.Length != 0 || !plan.TrySeal() || !context.IsCurrent ||
                !actors.IsCurrent || !source.IsCurrentOwned) return false;
            prepared = plan;
            return true;
        }
        finally
        {
            if (prepared is null) plan.Dispose();
        }
    }

    private static bool TryCommitDisposablePlateKill(ToolWorldMutationPlan plan)
    {
        if (!plan.TryAdoptWholeUnpublished(out var publication) || publication is null) return false;
        var end = plan.CapturePublicationOffset();
        if (!publication.TryPublishThrough(in end))
            throw new InvalidOperationException("Accepted disposable plate lost its source publication boundary.");
        return true;
    }
}
