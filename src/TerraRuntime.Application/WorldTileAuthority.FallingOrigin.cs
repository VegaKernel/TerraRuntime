using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    private bool fallingOriginInProgress;

    // The maintenance queue is a runtime scheduling policy. Its selected cell
    // still executes the existing ordered source frame/allocation preparation,
    // so NewProjectile's wire preimage precedes its caller postwrites while all
    // live participants adopt before observers.
    private bool TryCommitFallingOrigin(int x, int y, in WorldTile before, out bool fell)
    {
        fell = false;
        if (fallingOriginInProgress || tiles is null || !tiles.Get(x, y).Equals(before) ||
            !TryCaptureToolTreeWorld(out var context) || context is null ||
            !players.TryCaptureToolTreeActors(out var actors) || actors is null) return false;
        var overlay = new ToolWorldTileOverlay1458(tiles);
        if (!TryCreateToolMutationPlan(overlay, out var plan) || plan is null) return false;
        using (plan)
        {
            if (!TryCreateToolSourcePass(overlay, plan.SourceRandom, false, context.HardMode,
                context.Facts, actors.Actors, plan, out var source) || source is null ||
                !source.TryFrameFallingOrigin(x, y, out bool preparedFall) ||
                !players.TryPrepareToolShapeEffects(plan.ShapeEffects, out var shape) || shape is null ||
                !shape.TrySealForWorldAdoption() || !plan.TrySeal() ||
                !context.IsCurrent || !actors.IsCurrent || !source.IsCurrentOwned ||
                !shape.IsCurrent || !plan.IsCurrentOwned) return false;
            var end = plan.CapturePublicationOffset();
            fallingOriginInProgress = true;
            try
            {
                if (!plan.TryAdoptWholeUnpublished(out var publication) || publication is null) return false;
                if (!shape.TryAdoptUnpublished())
                    throw new InvalidOperationException("A falling origin lost prepared player shape ownership.");
                fell = preparedFall;
                System.Runtime.ExceptionServices.ExceptionDispatchInfo? first = null;
                try
                {
                    if (!publication.TryPublishThrough(in end))
                        throw new InvalidOperationException("A falling origin lost its accepted publication order.");
                }
                catch (Exception error) { first = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error); }
                while (true)
                {
                    try { if (!shape.TryPublishNext()) break; }
                    catch (Exception error) { first ??= System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error); }
                }
                first?.Throw();
                return true;
            }
            finally { fallingOriginInProgress = false; }
        }
    }

    internal sealed partial class ToolWorldSourcePass1458
    {
        internal bool TryFrameFallingOrigin(int x, int y, out bool fell)
        {
            fell = false;
            if (clientRole || server is null || !IsCurrentOwned || work.Count != 0 ||
                !Overlay.TryReadSourceSafely(x, y, out var before) || !before.IsActive ||
                !VanillaFallingBlock1458.TryGetProjectile(before.TileType, out _) ||
                !PushSquare(x, y) || !Drain() || !Overlay.TryRead(x, y, out var after)) return Reject();
            fell = !after.IsActive;
            return IsCurrentOwned;
        }
    }
}
