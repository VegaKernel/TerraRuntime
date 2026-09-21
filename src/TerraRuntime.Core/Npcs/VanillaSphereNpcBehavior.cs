using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Core.Npcs;

/// <summary>NPC.AI style 9 for Burning Sphere, Water Sphere and the Good World Eater projectile NPC, TerrariaServer 1.4.5.8.</summary>
internal sealed class VanillaSphereNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    public bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        _ = inner;
        bool water = npc.TypeIdentity == VanillaNpcIds.WaterSphere;
        bool chaos = npc.TypeIdentity == VanillaNpcIds.ChaosBall || npc.TypeIdentity == VanillaNpcIds.TimFireball;
        bool eaterSpit = npc.TypeIdentity == VanillaNpcIds.EaterOfWorldsSpit;
        if ((!water && !chaos && !eaterSpit && npc.TypeIdentity != VanillaNpcIds.BurningSphere) ||
            definition.AiStyle != VanillaNpcAiStyles.BurningSphere ||
            !definition.TryResolveHitbox(npc.Simulation, out var hitbox))
        {
            next = default;
            return false;
        }

        bool protectedByBoss = !chaos && context.GoodWorld &&
            context.CountNpcPeers(water ? VanillaNpcIds.SkeletronHead : VanillaNpcIds.WallOfFlesh) > 0;
        ushort targetSlot = npc.Target;
        float velocityX = npc.VelocityX, velocityY = npc.VelocityY;
        int direction = npc.Simulation.DirectionX, directionY = npc.Simulation.DirectionY;
        if (targetSlot == byte.MaxValue)
        {
            // TargetClosest falls back to raw slot zero when it found no eligible player.
            targetSlot = context.TrySelectClosestTarget(in npc, in definition, out var selected) ? selected.Target : (ushort)0;
            if (!context.TryFindCandidate((byte)targetSlot, out var target))
                target = new((byte)targetSlot, VanillaPlayerHitboxFacts.BaseWidth * .5f,
                    VanillaPlayerHitboxFacts.BaseHeight * .5f, 0, false, false, false, false);
            float centerX = npc.PositionX + hitbox.Width * .5f, centerY = npc.PositionY + hitbox.Height * .5f;
            float dx = target.CenterX - centerX, dy = target.CenterY - centerY;
            float speed = eaterSpit ? context.GoodWorld ? 10f : 7f : water ? 6f : 5f;
            if (protectedByBoss)
                speed = water ? (VanillaSkeletronCombat.HasRedHatAdjustments(npc.TypeIdentity, npc.Ai, npc.Simulation.LocalAi) ? 8f : 10f) : 14f;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            if (length <= 0f) length = 1f;
            float multiplier = speed / length;
            velocityX = dx * multiplier;
            velocityY = dy * multiplier;
            if (!target.Dead && !(target.NoAggro && direction != 0))
            {
                // SetTargetTrackingValues compares the truncated player rectangle to integer NPC half-sizes.
                int playerCenterX = (int)(target.CenterX - VanillaPlayerHitboxFacts.BaseWidth * .5f) + (int)VanillaPlayerHitboxFacts.BaseWidth / 2;
                int playerCenterY = (int)(target.CenterY - VanillaPlayerHitboxFacts.BaseHeight * .5f) + (int)VanillaPlayerHitboxFacts.BaseHeight / 2;
                direction = playerCenterX < npc.PositionX + hitbox.Width / 2 ? -1 : 1;
                directionY = playerCenterY < npc.PositionY + hitbox.Height / 2 ? -1 : 1;
            }
        }

        NpcAiState ai = npc.Ai;
        float positionX = npc.PositionX;
        float positionY = npc.PositionY;
        bool hitsSolid = eaterSpit && npc.Simulation.SolidCollision;
        int timeLeft = npc.Simulation.TimeLeft < 0 ? 100 : Math.Min(npc.Simulation.TimeLeft, 100);
        bool protectedAboveSurface = eaterSpit && context.GoodWorld && !npc.Simulation.DontTakeDamage &&
            !double.IsPositiveInfinity(context.WorldSurfacePixels) &&
            npc.PositionY + hitbox.Height * .5f < context.WorldSurfacePixels;
        int? damageOverride = npc.Simulation.DamageOverride;
        if (eaterSpit)
        {
            // NPC.GetAttackDamage_CappedAtMaster(32): the retained spawn difficulty supplies the source
            // difficulty multiplier while values above Master remain capped at the Master endpoint.
            float difficulty = Math.Clamp(npc.Simulation.SpawnDifficulty ?? 1f, .5f, 3f);
            damageOverride = (int)(32f * difficulty);
            ai = ai with { Ai0 = Math.Min(3f, ai.Ai0 + 1f) };
            if (ai.Ai0 == 2f)
            {
                positionX += velocityX;
                positionY += velocityY;
            }
            if (hitsSolid)
                timeLeft = 0;
        }

        var simulation = npc.Simulation with
        {
            DirectionX = direction,
            DirectionY = directionY,
            DontTakeDamage = npc.Simulation.DontTakeDamage || protectedByBoss || protectedAboveSurface,
            TimeLeft = timeLeft,
            Rotation = (npc.Simulation.Rotation ?? 0f) + .4f * direction,
            DamageOverride = damageOverride,
            Life = hitsSolid ? 0 : npc.Simulation.Life
        };
        next = new(npc.Type, npc.NetId, positionX, positionY, velocityX, velocityY, targetSlot, ai, simulation);
        return true;
    }
}
