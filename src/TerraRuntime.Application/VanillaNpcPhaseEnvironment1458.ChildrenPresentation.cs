using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Application;

internal sealed partial class VanillaNpcPhaseEnvironment1458
{
    internal static bool SupportsChildPresentation(in NpcSnapshot source)
    {
        int count = ChildFrameCount(source.TypeIdentity);
        return count > 0 && source.NetIdentity.Value == source.Type &&
            source.Simulation.FrameIndex is { } row && row >= 0 && row < count &&
            source.Simulation.FrameCounter >= 0d && double.IsFinite(source.Simulation.FrameCounter) &&
            (source.TypeIdentity != VanillaNpcIds.Maggot || source.Simulation.Rotation is { } angle && float.IsFinite(angle));
    }

    // NPC.FindFrame uses a one-pixel frame height on the dedicated server. This component
    // preserves JustHit; the outer UpdateNPC owner retires that flag after the network phase.
    internal static bool TryFinishChildFrame(in NpcStateUpdate input, int primarySlope, int belowSlope,
        out NpcStateUpdate final)
    {
        var state = input;
        final = default;
        int count = ChildFrameCount(new(state.Type));
        if (count == 0 || state.Type != state.NetId || state.Simulation.FrameIndex is not { } row ||
            row < 0 || row >= count || !double.IsFinite(state.Simulation.FrameCounter)) return false;
        var simulation = state.Simulation;
        double counter = simulation.FrameCounter;
        if (state.Type == VanillaNpcIds.MartianTurret.Value)
        {
            counter++;
            if (state.Ai.Ai0 > 0f)
            {
                if (state.Ai.Ai0 >= 20f) counter++;
                if (state.Ai.Ai0 >= 40f) counter++;
            }
            if (counter >= (state.Ai.Ai0 > 0f ? 10d : 15d))
            { counter = 0d; row++; if (row >= 2) row = 0; }
        }
        else if (state.Type == VanillaNpcIds.ScutlixRider.Value)
        {
            if (simulation.DirectionX != 0) simulation = simulation with { SpriteDirection = -simulation.DirectionX };
            row = state.Ai.Ai2 > 0f ? (int)state.Ai.Ai2 - 1 : 2;
            if (row < 0 || row >= count) return false;
        }
        else if (state.Type == VanillaNpcIds.Maggot.Value)
        {
            if (simulation.Rotation is not { } rotation || primarySlope is < 0 or > 4 || belowSlope is < 0 or > 4)
                return false;
            var local = simulation.LocalAi with { Ai0 = -2f };
            if (state.VelocityY == 0f)
            {
                rotation = 0f;
                if (state.VelocityX == 0f) { row = 1; counter = 0d; }
                else if (++counter > 12d) { counter = 0d; row++; if (row > 1) row = 0; }
            }
            else { rotation += simulation.DirectionX * .1f; row = 1; }
            int slope = primarySlope == 0 ? belowSlope : primarySlope;
            if (slope is 1 or 2)
            { rotation = slope == 1 ? .785f : -.785f; local = local with { Ai0 = 0f }; }
            simulation = simulation with { Rotation = rotation, LocalAi = local };
        }
        final = state with { Simulation = simulation with { FrameIndex = row, FrameCounter = counter } };
        return true;
    }

    private static int ChildFrameCount(NpcTypeId type) =>
        type == VanillaNpcIds.ForceBubble ? 1 : type == VanillaNpcIds.ScutlixRider ? 5 :
        type == VanillaNpcIds.MartianTurret || type == VanillaNpcIds.Maggot ? 2 : 0;

    private sealed partial class Capture
    {
        private bool TryFinishChildPresentation(in NpcStateUpdate motion, out NpcStateUpdate final)
        {
            final = default;
            int primary = 0, below = 0;
            if (motion.Type == VanillaNpcIds.Maggot.Value)
            {
                if (!VanillaNpcDefinitionCatalog.TryGet(new(motion.Type), new(motion.NetId), out var definition) ||
                    !definition.TryResolveHitbox(motion.Simulation, out var body)) return false;
                int x = (int)(motion.PositionX + body.Width * .5f) / 16, y = (int)motion.PositionY / 16;
                if (x < 0 || y < 0 || x >= tiles.Dimensions.WidthTiles || y + 1 >= tiles.Dimensions.HeightTiles) return false;
                terrain.FenceBody(x * 16f, y * 16f, 0f, 0f, 16, 32);
                primary = tiles.Get(x, y).SourceSlope;
                if (primary == 0) below = tiles.Get(x, y + 1).SourceSlope;
            }
            if (!IsCurrent || !TryFinishChildFrame(in motion, primary, below, out final)) return false;
            final = final with { Simulation = final.Simulation with { JustHit = false } };
            return true;
        }
    }
}
