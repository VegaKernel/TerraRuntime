using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeTownNpcSchedule1458
{
    // NPC.FindFrame1458: Yellow Slime shares the town-slime partition (14 total,
    // six extra, no attack frames). This pure pose step preserves JustHit; the
    // resident dispatcher clears it after presentation, at the outer update boundary.
    internal static bool TryFinishSlimeYellowPresentation(in NpcStateUpdate state, out NpcStateUpdate next)
    {
        // Callers can use the same local for the readonly input and output.
        var input = state;
        next = default;
        var simulation = input.Simulation;
        if (input.Type != 683 || input.NetId != 683 || simulation.FrameIndex is not (>= 0 and < 14) ||
            simulation.DirectionX is not (-1 or 1) || !double.IsFinite(simulation.FrameCounter) ||
            simulation.FrameCounter < 0d || !float.IsFinite(input.VelocityX) || !float.IsFinite(input.VelocityY))
            return false;
        int frame = simulation.FrameIndex.Value;
        double counter = simulation.FrameCounter;
        if (input.VelocityY != 0f)
        {
            int first = input.VelocityY < 0f ? 2 : 3;
            int last = input.VelocityY < 0f ? 3 : 6;
            if (frame < first || frame > last) { frame = first; counter = -1d; }
            counter++;
            if (counter >= 4d) { counter = 0d; frame = Math.Min(frame + 1, last); }
        }
        else if (input.Ai.Ai0 == 20f)
        {
            if (input.Ai.Ai1 > 30f && (frame < 8 || frame > 13)) frame = 8;
            if (frame > 0) counter++;
            if (counter >= 12d)
            {
                counter = 0d;
                if (++frame > 13) frame = input.Ai.Ai1 > 30f ? 8 : 0;
            }
        }
        else if (input.Ai.Ai0 == 5f) { frame = 11; counter = 0d; }
        else if (input.Ai.Ai0 is 0f or 1f)
        {
            if (input.VelocityX == 0f) { frame = 0; counter = 0d; }
            else
            {
                counter += Math.Abs(input.VelocityX) * 2f;
                counter++;
                frame = Math.Max(1, frame);
                if (counter > 12d) { frame++; counter = 0d; }
                if (frame >= 8) frame = 1;
            }
        }
        else return false;
        next = input with { Simulation = simulation with {
            FrameIndex = frame, FrameCounter = counter, SpriteDirection = -simulation.DirectionX } };
        return true;
    }
}
