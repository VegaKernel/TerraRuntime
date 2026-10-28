using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;

namespace TerraRuntime.Application;

/// <summary>Dedicated-server FindFrame state for the explicit nonresident ground-critter cohort.</summary>
internal static class VanillaGroundCritterPresentation1458
{
    internal static bool TryFinish(in NpcStateUpdate physics, out NpcStateUpdate presented)
    {
        presented = default;
        int type = physics.Type;
        if (type is 46 or 303 or 337 or 443 or 540 or >= 646 and <= 652)
        {
            presented = VanillaGroundCritterAccepted1458.FinishPresentation(in physics);
            return true;
        }
        int frames = type switch {
            148 or 149 or 625 => 12,
            299 or 300 or 447 or 538 or 539 or 610 or >= 639 and <= 645 => 6,
            361 or 445 => 13,
            616 or 617 => 14,
            366 or 367 => 4,
            _ => 0
        };
        var state = physics.Simulation;
        if (frames == 0 || state.FrameIndex is not { } frame || frame < 0 || frame >= frames ||
            !double.IsFinite(state.FrameCounter) || state.FrameCounter < 0d ||
            !float.IsFinite(physics.VelocityX) || !float.IsFinite(physics.VelocityY)) return false;
        double counter = state.FrameCounter;
        int sprite = state.SpriteDirection;
        float vx = physics.VelocityX, vy = physics.VelocityY;
        if (vy == 0f && state.DirectionX is -1 or 1) sprite = state.DirectionX;

        switch (type)
        {
            case 148 or 149:
                int offset = state.LocalAi.Ai0 switch { 2f => 3, 3f => 6, 4f => 9, _ => 0 };
                if (vy != 0f) { counter = 0d; frame = offset + 2; }
                else if (vx == 0f) { counter = 0d; frame = offset; }
                else
                {
                    counter += MathF.Abs(vx);
                    frame = offset + (counter < 6d ? 0 : counter < 12d ? 1 : 2);
                    if (counter >= 15d) counter = 0d;
                }
                break;
            case 299 or 538 or 539 or >= 639 and <= 645:
                bool reversing = state.AiStyleOverride?.Value == 3 &&
                    (state.DirectionX < 0 && vx > 0f || state.DirectionX > 0 && vx < 0f);
                if (vy != 0f) { counter = 0d; frame = vy < 0f ? 2 : 4; }
                else if (vx == 0f || reversing) { counter = 0d; frame = 0; }
                else
                {
                    Walk(ref frame, ref counter, MathF.Abs(vx) * .5f, 6d);
                    if (frame >= 5) frame = 1;
                }
                break;
            case 300 or 447 or 610:
                if (vy == 0f && vx == 0f)
                {
                    counter++;
                    if (counter > 8d) { counter = 0d; frame++; }
                    if (frame > 1) frame = 0;
                }
                else
                {
                    Walk(ref frame, ref counter, MathF.Abs(vx) * .5f, 6d);
                    if (frame >= 6) frame = 2;
                }
                break;
            case 361 or 445:
                sprite = state.DirectionX;
                if (state.Wet)
                {
                    counter = 0d;
                    frame = MathF.Abs(vx) > .25f ? 10 : MathF.Abs(vx) > .15f ? 11 : 12;
                }
                else if (vy != 0f) frame = vy > 0f ? 9 : 8;
                else if (vx == 0f)
                {
                    counter++;
                    if (counter > 6d) { counter = 0d; frame++; }
                    if (frame > 5) frame = 0;
                }
                else
                {
                    counter++;
                    frame = counter < 6d ? 0 : counter < 12d ? 6 : counter < 18d ? 8 : 9;
                    if (counter >= 23d) counter = 0d;
                }
                break;
            case 616 or 617:
                sprite = state.DirectionX;
                counter++;
                if (state.Wet)
                {
                    if (counter > (vx == 0f ? 10d : 5d))
                    {
                        counter = 0d;
                        if (++frame > 13) frame = 8;
                    }
                }
                else
                {
                    if (counter > 8d) { counter = 0d; if (++frame > 7) frame = 0; }
                    if (vx == 0f) { counter = 0d; frame = 0; }
                    if (vy != 0f) { counter = 0d; frame = 8; }
                }
                break;
            case 625:
                sprite = state.DirectionX;
                if (state.Wet)
                {
                    frame = Math.Max(6, frame);
                    counter++;
                    if (counter > (vx == 0f ? 8d : 4d))
                    {
                        counter = 0d;
                        if (++frame > 11) frame = 6;
                    }
                }
                else
                {
                    if (frame > 5) frame = 0;
                    counter++;
                    if (counter > 7d) { counter = 0d; if (++frame > 5) frame = 0; }
                    if (vx == 0f) { counter = 0d; frame = 0; }
                }
                break;
            case 366 or 367:
                if (vx == 0f) { counter = 0d; frame = 0; }
                else
                {
                    Walk(ref frame, ref counter, MathF.Abs(vx), 6d);
                    if (frame >= 4) frame = 0;
                }
                break;
        }
        presented = physics with { Simulation = state with {
            FrameIndex = frame, FrameCounter = counter, SpriteDirection = sprite, JustHit = false } };
        return true;
    }

    private static void Walk(ref int frame, ref double counter, float speed, double threshold)
    {
        counter += speed;
        counter++;
        if (counter > threshold) { frame++; counter = 0d; }
    }
}
