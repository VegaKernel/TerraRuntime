using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

/// <summary>Server-owned Expert projectile clock from TerrariaServer 1.4.5.8 AI_003 Gastropod.</summary>
internal sealed class VanillaGastropodBehavior(IVanillaNpcRandom random)
{
    private readonly IVanillaNpcRandom random = random ?? throw new ArgumentNullException(nameof(random));
    private IVanillaNpcProjectileEnvironment? environment;

    public void SetProjectileEnvironment(IVanillaNpcProjectileEnvironment value) =>
        environment = value ?? throw new ArgumentNullException(nameof(value));

    public NpcSnapshot Complete(in NpcSnapshot before, in NpcSnapshot committed, VanillaNpcBehaviorContext context,
        INpcAiCommittedNpcMutationSink mutations)
    {
        if (before.TypeIdentity != VanillaNpcIds.Gastropod || committed.TypeIdentity != VanillaNpcIds.Gastropod ||
            !context.ExpertMode || committed.Simulation.Confused || environment is null || committed.Target >= byte.MaxValue ||
            !context.TryFindCandidate((byte)committed.Target, out VanillaNpcTargetCandidate target) ||
            !target.Active || target.Dead || target.Ghost ||
            !environment.CanHit(committed.PositionX + 25f, committed.PositionY + 10f, 1, 1,
                target.CenterX, target.CenterY, 1, 1))
        {
            return committed;
        }

        float clock = committed.Simulation.LocalAi.Ai0 + 1f;
        if (before.Simulation.JustHit)
            clock = MathF.Max(0f, clock - random.NextInt32(20, 60));
        bool fire = clock > random.NextInt32(180, 900);
        if (fire)
            clock = 0f;

        var update = new NpcStateUpdate(committed.Type, committed.NetId, committed.PositionX, committed.PositionY,
            committed.VelocityX, committed.VelocityY, committed.Target, committed.Ai,
            committed.Simulation with { LocalAi = committed.Simulation.LocalAi with { Ai0 = clock } });
        if (!mutations.TryUpdateState(in committed, in update, out NpcSnapshot updated))
            return committed;
        if (!fire)
            return updated;

        float velocityX = target.CenterX - (updated.PositionX + 25f);
        float velocityY = target.CenterY - (updated.PositionY + 10f);
        float length = MathF.Sqrt(velocityX * velocityX + velocityY * velocityY);
        if (length <= 0f || !float.IsFinite(length))
            return updated;
        velocityX *= 8f / length;
        velocityY *= 8f / length;
        mutations.TrySpawnProjectile(in updated, new NpcAiProjectileIntent(VanillaProjectileIds.GastropodBolt,
            updated.PositionX + 21f, updated.PositionY + 6f, velocityX, velocityY, 18, 0f), out _);
        return updated;
    }
}
