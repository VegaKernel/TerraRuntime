using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;

namespace TerraRuntime.Application;

// NPC.FindFrame's dedicated-server frame height is one. FrameIndex is that source row,
// not a texture coordinate. Keep the distinct timing and airborne rules of this cohort.
internal static class VanillaGroundCombatPresentation1458
{
    internal static bool Supports(in NpcSnapshot npc)
    {
        int count = FrameCount(npc.Type);
        return count > 0 && npc.Simulation.FrameIndex is { } row && row >= 0 && row < count &&
            double.IsFinite(npc.Simulation.FrameCounter) && npc.Simulation.FrameCounter >= 0d &&
            (!IsHumanoid(npc.Type) || npc.Ai.Ai0 is 0f or 1f);
    }

    internal static bool TryFinish(in NpcStateUpdate motion, out NpcStateUpdate final)
    {
        final = default;
        int count = FrameCount(motion.Type);
        if (count == 0 || motion.Simulation.FrameIndex is not { } row ||
            !double.IsFinite(motion.Simulation.FrameCounter)) return false;
        if (motion.Type is 55 or 57 or 465 or 592)
            return VanillaGroundCritterPresentation1458.TryFinish(in motion, out final);
        if (motion.Type == 464)
        { final = VanillaGroundCritterAccepted1458.FinishPresentation(in motion); return true; }

        double timer = motion.Simulation.FrameCounter;
        int sprite = motion.Simulation.SpriteDirection;
        int direction = motion.Simulation.DirectionX;
        float speed = MathF.Abs(motion.VelocityX);
        bool grounded = motion.VelocityY == 0f;
        if (grounded && direction is -1 or 1 && motion.Type is not (508 or 532 or 580 or 582)) sprite = direction;

        if (IsHumanoid(motion.Type))
        {
            if (motion.Ai.Ai0 is not (0f or 1f)) return false;
            if (!grounded)
            { timer = 0d; row = motion.Type is 44 or 77 or 140 or 197 or 489 ? 0 : 1; }
            else if (motion.VelocityX == 0f)
            { timer = 0d; row = motion.Type is 140 or 489 ? 1 : 0; }
            else
            {
                timer += motion.Type == 489 ? speed : speed * 2f;
                timer += motion.Type == 489 ? .5d : 1d;
                if (row < 2) row = 2;
                if (timer > (motion.Type == 489 ? 8d : motion.Type == 534 ? 12d : 6d)) { row++; timer = 0d; }
                if (row >= count) row = 2;
            }
        }
        else if (motion.Type == 199)
        {
            // The crawler's first three rows finish the transformation even in the air.
            if (row < 3)
            {
                timer++;
                if (timer > 6d) { timer = 0d; row++; }
            }
            else if (grounded)
            {
                timer += speed;
                if (timer > 12d) { timer = 0d; if (++row > 7) row = 3; }
            }
        }
        else if (motion.Type == 586)
        {
            if (motion.Simulation.Wet)
            {
                if (motion.VelocityX < 0f) direction = -1;
                else if (motion.VelocityX > 0f) direction = 1;
                sprite = direction;
                timer += MathF.Sqrt(motion.VelocityX * motion.VelocityX + motion.VelocityY * motion.VelocityY);
                timer++;
                if (timer > 60d) { row++; timer = 0d; }
                if (row is < 0 or > 1) row = 0;
            }
            else if (motion.VelocityX == 0f) { row = 1; timer = 0d; }
            else
            {
                timer += speed;
                timer += .5d;
                if (timer > 8d) { row++; timer = 0d; }
                if (row >= count) row = 0;
            }
        }
        else if (motion.Type is >= 524 and <= 527 or 528 or 529)
        {
            bool ghoul = motion.Type <= 527;
            if (!grounded) { row = ghoul ? 1 : count - 1; timer = 0d; }
            else if (motion.VelocityX == 0f) { row = 0; timer = 0d; }
            else
            {
                if (ghoul && row <= 1) row = 2;
                timer += speed;
                timer++;
                if (timer > 9d) { row++; timer = 0d; }
                if (row >= (ghoul ? count : count - 1)) row = ghoul ? 2 : 1;
            }
        }
        else if (motion.Type is 536 or 590 or 691)
        {
            bool reversing = direction == -1 && motion.VelocityX > 0f || direction == 1 && motion.VelocityX < 0f;
            if (!grounded || reversing) { row = 2; timer = 0d; }
            else if (motion.VelocityX == 0f) { row = 0; timer = 0d; }
            else
            {
                timer += speed;
                if (timer < 8d) row = 0;
                else if (timer < 16d) row = 1;
                else if (timer < 24d) row = 2;
                else if (timer < 32d) row = 1;
                else timer = 0d;
            }
        }
        else if (motion.Type == 470)
        {
            int offset = motion.Simulation.LocalAi.Ai0 switch { 2f => 3, 3f => 6, 4f => 9, _ => 0 };
            if (!grounded) { row = offset + 2; timer = 0d; }
            else if (motion.VelocityX == 0f) { row = offset; timer = 0d; }
            else
            {
                timer += speed;
                if (timer < 6d) row = offset;
                else if (timer < 12d) row = offset + 1;
                else if (timer < 15d) row = offset + 2;
                else { row = offset + 2; timer = 0d; }
            }
        }
        else if (motion.Type is 381 or 382 or 385)
        {
            if (!grounded) { row = 1; timer = 0d; }
            else
            {
                if (row < 2) row = 2;
                timer += motion.Type == 385 ? (double)speed * 1.2d : speed * 2f;
                if (timer > 6d) { row++; timer = 0d; }
                if (row >= count) row = 2;
                if (motion.VelocityX == 0f) row = 0;
            }
        }
        else if (motion.Type == 520)
        {
            if (!grounded) { row = 1; timer = 0d; }
            else
            {
                timer += speed;
                if (timer > 6d) { row = speed < .2f ? 0 : row + 1; timer = 0d; }
                if (row >= count) row = 1;
                if (motion.VelocityX == 0f) row = 0;
            }
        }
        else if (motion.Type is >= 449 and <= 452 or 481)
        {
            bool hoplite = motion.Type == 481;
            if (!grounded) { row = hoplite ? 0 : 1; timer = 0d; }
            else if (motion.Ai.Ai2 > 0f)
            {
                float attack = motion.Ai.Ai1;
                row = hoplite
                    ? attack < 22f ? 1 : attack < 28f ? 18 : attack < 34f ? 17 : attack < 40f ? 16 : 15
                    : count - 5 + (attack >= 50f ? 1 : attack >= 43f ? 2 : attack >= 36f ? 3 : attack >= 29f ? 4 : 0);
                timer = 0d;
            }
            else if (motion.VelocityX == 0f) row = hoplite ? 1 : 0;
            else
            {
                if (row < 2) row = 2;
                timer += speed * 2f;
                if (timer > 6d) { row++; timer = 0d; }
                if (row >= 15) row = 2;
            }
        }
        else if (motion.Type == 482)
        {
            if (!grounded) { row = 1; timer = 0d; }
            else if (motion.Ai.Ai2 < 0f)
            {
                timer++;
                if (timer > 3d) { row++; timer = 0d; }
                if (row >= count || row < 11) row = 11;
            }
            else if (motion.VelocityX == 0f) { timer++; row = 0; }
            else
            {
                timer += .2f + speed;
                if (timer > 8d) { row++; timer = 0d; }
                if (row >= count - 10 || row < 2) row = 2;
            }
        }
        else if (motion.Type is 508 or 532 or 580 or 582)
        {
            if (!grounded) row = 1;
            else if (motion.VelocityX == 0f) row = 0;
            else
            {
                if (direction < 0 && motion.VelocityX < 0f || direction > 0 && motion.VelocityX > 0f) sprite = direction;
                if (row < 2) row = 2;
                timer += 1f + speed / 2f;
                if (timer > 6d) { row++; timer = 0d; }
                if (row >= count) row = 2;
            }
        }
        else return false;
        final = motion with { Simulation = motion.Simulation with {
            FrameIndex = row, FrameCounter = timer, DirectionX = direction, SpriteDirection = sprite, JustHit = false } };
        return true;
    }

    private static bool IsHumanoid(int type) => type is 44 or 77 or 104 or 140 or 181 or 185 or 196 or 197 or 198 or 489 or 534;

    // Main.npcFrameCount 1.4.5.8, independently read from the initialized official assembly.
    private static int FrameCount(int type) => type switch {
        44 or 140 or 197 => 15, 104 or 181 => 16, 196 => 14, 489 or 528 or 529 => 9,
        >= 524 and <= 527 => 8, 536 or 590 or 691 => 3, 464 => 7, 470 or 185 => 12,
        77 => 15, 381 or 382 or 385 => 9, 520 => 8, >= 449 and <= 451 or 481 => 19,
        452 or 482 => 20, 55 or 57 or 465 or 508 or 580 or 582 or 592 => 6,
        198 => 16, 199 or 532 or 534 => 8, 586 => 14, _ => 0 };
}
