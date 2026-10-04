using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed class NpcRuntimeTownCombatRandom1458(IVanillaNpcRandom random) : IRuntimeTownNpcCombatRandom1458
{
    internal IVanillaNpcRandom Current { get; set; } = random;
    public int Next(int exclusiveMax) => Current.NextInt32(0, exclusiveMax);
    public float NextFloat(float inclusiveMin, float exclusiveMax) =>
        (float)Current.NextDouble() * (exclusiveMax - inclusiveMin) + inclusiveMin;
}

internal sealed partial class RuntimeTownNpcCombat1458
{
    // Called after the source body and real offers, before physics. No store mutation or shot here.
    internal bool TryPlanAttackInitialization(in NpcSnapshot source,
        in NpcStateUpdate body, in RuntimeTownNpcDanger1458 danger, bool activeTalk,
        out NpcStateUpdate next, out bool force)
    {
        next = body;
        force = false;
        bool projectile = VanillaTownNpcProjectileAttackCatalog1458.TryGet(source.TypeIdentity, out var shot);
        bool melee = VanillaTownNpcMeleeAttackCatalog1458.TryGet(source.TypeIdentity, out var swing);

        float cooldown = body.Simulation.LocalAi.Ai1;
        if (cooldown > 0f) cooldown--;
        next = body with { Simulation = body.Simulation with {
            LocalAi = body.Simulation.LocalAi with { Ai1 = cooldown } } };
        if (next.Ai.Ai0 is not (0f or 1f or 8f) || !danger.WithinRange || danger.Stinky ||
            next.VelocityY != 0f || cooldown > 0f) return true;
        if (!projectile && !melee) return false;
        int average = projectile ? shot.AttackAverageChance : swing.AttackAverageChance;
        // Source rolls before checking the retained target or its final attack-specific LOS/angle.
        if (random.Next(GetAttackChance(average, activeTalk)) != 0) return true;
        NpcSnapshot target = danger.AttackTarget;
        if (!CanHitAttackTarget(in next, in target, projectile &&
                shot.Kind == VanillaTownNpcProjectileAttackKind1458.Straight))
        {
            target = danger.ThreatDirection == 1 ? danger.LeftAttack : danger.RightAttack;
            if (!CanHitAttackTarget(in next, in target, projectile &&
                    shot.Kind == VanillaTownNpcProjectileAttackKind1458.Straight)) return true;
        }
        float aimY = 0f;
        if (projectile && shot.Kind == VanillaTownNpcProjectileAttackKind1458.Straight)
        {
            if (!VanillaTownNpcDefinitionCatalogBridge.TryGetHitbox(in source, out var size) ||
                !VanillaTownNpcDefinitionCatalogBridge.TryGetCenter(in target, out float tx, out float ty)) return false;
            float dx = tx - (next.PositionX + size.Width * .5f);
            float dy = ty - (next.PositionY + size.Height * .5f);
            float length = MathF.Sqrt(dx * dx + dy * dy);
            aimY = dy * (1f / length);
            if (aimY is < -.5f or > .5f) return true;
        }
        int facing = next.PositionX < target.PositionX ? 1 : -1;
        next = next with
        {
            Ai = next.Ai with { Ai0 = projectile ? shot.AttackState : 15f,
                Ai1 = projectile ? shot.AttackTime : swing.AttackTime, Ai2 = aimY },
            Simulation = next.Simulation with { DirectionX = facing,
                LocalAi = next.Simulation.LocalAi with { Ai2 = next.Ai.Ai0, Ai3 = 0f } }
        };
        force = true;
        // Facing changes here; sprite/velocity remain body state until their own source stage.
        return true;
    }

    private bool CanHitAttackTarget(in NpcStateUpdate source, in NpcSnapshot target, bool straight)
    {
        if (!target.IsActive || !npcs.TryGet(target.Handle, out var live) || live.Revision != target.Revision ||
            !VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(source.Type), new NpcNetId(source.NetId), out var definition) ||
            !definition.TryResolveHitbox(source.Simulation, out var size) ||
            !VanillaTownNpcDefinitionCatalogBridge.TryGetCenter(in target, out float tx, out float ty)) return false;
        float sx = source.PositionX + size.Width * .5f, sy = source.PositionY + size.Height * .5f;
        return straight ? VanillaWorldLineOfSight.CanHitLine(tiles, sx, sy, tx, ty) :
            VanillaWorldCanHit.HasLineOfSight(tiles, sx, sy, 1, 1, tx, ty, 1, 1);
    }
}
