using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

/// <summary>Dedicated-server NPC.AI style 8 for verified caster variants, including Hardmode Dungeon Skeletons.</summary>
internal sealed class VanillaGoblinSorcererBehavior(IVanillaNpcRandom random) : IVanillaNpcBehaviorStrategy
{
    private readonly IVanillaNpcRandom random = random ?? throw new ArgumentNullException(nameof(random));
    private IVanillaWallOfFleshEnvironment? environment;

    public void SetEnvironment(IVanillaWallOfFleshEnvironment value) =>
        environment = value ?? throw new ArgumentNullException(nameof(value));

    public bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        _ = inner;
        if (environment is null || !IsSupported(npc.TypeIdentity) ||
            definition.AiStyle != VanillaNpcAiStyles.Caster)
        {
            next = default;
            return false;
        }

        ushort targetSlot = context.TrySelectClosestTarget(in npc, in definition, out var selected) ? selected.Target : (ushort)0;
        context.TryFindCandidate((byte)targetSlot, out VanillaNpcTargetCandidate target);
        int directionX = npc.Simulation.DirectionX, directionY = npc.Simulation.DirectionY;
        if (!target.Dead && !(target.NoAggro && directionX != 0))
        {
            directionX = target.CenterX < npc.PositionX + definition.Width / 2 ? -1 : 1;
            directionY = target.CenterY < npc.PositionY + definition.Height / 2 ? -1 : 1;
        }

        float x = npc.PositionX, y = npc.PositionY, vx = npc.VelocityX * .93f, vy = npc.VelocityY;
        if (vx is > -.1f and < .1f) vx = 0f;
        NpcAiState ai = npc.Ai;
        bool teleported = ai.Ai2 != 0f && ai.Ai3 != 0f;
        if (teleported)
        {
            x = ai.Ai2 * 16f - definition.Width / 2 + 8f;
            y = ai.Ai3 * 16f - definition.Height;
            vx = vy = 0f;
            ai = ai with { Ai2 = 0f, Ai3 = 0f };
        }

        float timer = ai.Ai0 == 0f ? 501f : ai.Ai0 + 1f;
        float attack = ai.Ai1;
        bool withinFiringRange = VanillaNpcGlobalFiringDistance.Contains(
            x + definition.Width * .5f, y + definition.Height * .5f, target.CenterX, target.CenterY);
        bool armsAttack = ArmsAttack(npc.TypeIdentity, timer, withinFiringRange);
        if (armsAttack)
        {
            attack = 30f;
        }
        if (ShouldTeleport(npc.TypeIdentity, ai.Ai0)) timer = 1f;
        if (attack > 0f) attack--;
        NpcSimulationState simulation = npc.Simulation with
        {
            DirectionX = directionX,
            DirectionY = directionY,
            JustHit = false
        };
        if (npc.TypeIdentity == VanillaNpcIds.RuneWizard)
        {
            int alpha = npc.Simulation.JustHit ? 0 : Math.Min(255, npc.Simulation.Alpha + 1);
            if (teleported) alpha = 255;
            simulation = simulation with { Alpha = alpha };
        }
        next = new(npc.Type, npc.NetId, x, y, vx, vy, targetSlot, ai with { Ai0 = timer, Ai1 = attack },
            simulation);
        return true;
    }

    public NpcSnapshot Complete(in NpcSnapshot before, in NpcSnapshot committed, VanillaNpcBehaviorContext context,
        INpcAiCommittedNpcMutationSink mutations)
    {
        if (environment is null || !IsSupported(before.TypeIdentity) ||
            committed.TypeIdentity != before.TypeIdentity || !ShouldTeleport(before.TypeIdentity, before.Ai.Ai0) ||
            !context.TryFindCandidate((byte)committed.Target, out VanillaNpcTargetCandidate target) ||
            !target.Active || target.Dead || target.Ghost ||
            !VanillaNpcDefinitionCatalog.TryGet(committed.TypeIdentity, out VanillaNpcDefinition definition))
            return CompleteProjectile(in before, in committed, context, mutations);

        NpcSnapshot current = committed;
        float centerX = current.PositionX + definition.Width * .5f;
        float centerY = current.PositionY + definition.Height * .5f;
        bool found = before.TypeIdentity.Value is >= 281 and <= 286 && environment is IVanillaDungeonCasterEnvironment dungeon
            ? dungeon.TryFindDungeonCasterTeleportSpot(centerX, centerY, (int)target.CenterX / 16, (int)target.CenterY / 16,
                context.CountNpcPeers(VanillaNpcIds.SkeletronHead) > 0, context.Candidates, random, out int x, out int y)
            : environment.TryFindTeleportSpot(centerX, centerY, (int)target.CenterX / 16, (int)target.CenterY / 16,
                context.Candidates, random, out x, out y);
        if (!found ||
            !mutations.TryUpdateAi(in current, current.Ai with { Ai1 = 19f, Ai2 = x, Ai3 = y }, out current))
        {
            return committed;
        }
        return CompleteProjectile(in before, in current, context, mutations);
    }

    private static float AdvanceTimer(float timer) => timer == 0f ? 501f : timer + 1f;

    private static bool ArmsAttack(NpcTypeId type, float timer, bool withinFiringRange)
    {
        if (type == VanillaNpcIds.RuneWizard)
            return withinFiringRange && timer is 75f or 150f or 225f or 300f or 375f or 450f;
        if (type == VanillaNpcIds.GoblinSorcerer)
            return timer is 100f or 200f or 300f;
        if (type.Value is 281 or 282)
            return withinFiringRange && timer is 100f or 120f or 140f or 200f or 220f or 240f or 300f or 320f or 340f;
        if (type.Value is 283 or 284)
            return withinFiringRange && timer is 100f or 150f or 200f or 250f or 300f;
        return withinFiringRange && timer is 100f or 200f or 300f;
    }

    private static bool ShouldTeleport(NpcTypeId type, float previousTimer)
    {
        float timer = AdvanceTimer(previousTimer);
        return type.Value switch
        {
            281 or 282 => timer >= 540f,
            283 or 284 => timer >= 450f,
            285 or 286 => timer > 400f,
            _ => timer >= 650f
        };
    }

    private NpcSnapshot CompleteProjectile(in NpcSnapshot before, in NpcSnapshot committed,
        VanillaNpcBehaviorContext context, INpcAiCommittedNpcMutationSink mutations)
    {
        if (before.Ai.Ai1 != 26f || committed.Ai.Ai1 != 25f ||
            !context.TryFindCandidate((byte)committed.Target, out VanillaNpcTargetCandidate target) ||
            !target.Active || target.Dead || target.Ghost)
            return committed;

        if (before.TypeIdentity != VanillaNpcIds.RuneWizard)
            return CompleteDungeonCasterProjectile(in before, in committed, target, context, mutations);

        float centerX = committed.PositionX + 9f;
        float centerY = committed.PositionY + 20f;
        float velocityX = target.CenterX - centerX + random.NextInt32(-10, 11);
        float velocityY = target.CenterY - centerY + random.NextInt32(-10, 11);
        float scale = 10f / MathF.Sqrt(velocityX * velocityX + velocityY * velocityY);
        var intent = new NpcAiProjectileIntent(VanillaProjectileIds.RuneBlast, centerX - 7f, centerY - 7f,
            velocityX * scale, velocityY * scale, 40, 0f) { TimeLeftOverride = 300 };
        mutations.TrySpawnProjectile(in committed, in intent, out _);
        return committed;
    }

    private NpcSnapshot CompleteDungeonCasterProjectile(in NpcSnapshot before, in NpcSnapshot committed,
        in VanillaNpcTargetCandidate target, VanillaNpcBehaviorContext context, INpcAiCommittedNpcMutationSink mutations)
    {
        if (before.TypeIdentity.Value is < 281 or > 286)
            return committed;

        float centerX = committed.PositionX + 9f;
        float centerY = committed.PositionY;
        float velocityX = target.CenterX - centerX;
        float velocityY = target.CenterY - centerY;
        ProjectileTypeId projectileType;
        float speed;
        int damage;
        if (before.TypeIdentity.Value is 283 or 284)
        {
            velocityX += random.NextInt32(-30, 31) - target.VelocityX * 10f;
            velocityY += random.NextInt32(-30, 31) - target.VelocityY * 10f;
            projectileType = VanillaProjectileIds.DungeonBeam;
            speed = 6f;
            damage = context.ExpertMode ? 24 : 30;
        }
        else if (before.TypeIdentity.Value is 285 or 286)
        {
            projectileType = VanillaProjectileIds.DungeonFlame;
            speed = 8f;
            damage = context.ExpertMode ? 32 : 40;
        }
        else
        {
            projectileType = VanillaProjectileIds.DungeonSkull;
            speed = 4f;
            damage = context.ExpertMode ? 32 : 40;
        }

        float length = MathF.Sqrt(velocityX * velocityX + velocityY * velocityY);
        if (!(length > 0f) || !float.IsFinite(length))
            return committed;
        float scale = speed / length;
        int halfSize = projectileType == VanillaProjectileIds.DungeonBeam ? 2 : 6;
        var intent = new NpcAiProjectileIntent(projectileType, centerX - halfSize, centerY - halfSize,
            velocityX * scale, velocityY * scale, damage, 0f);
        if (projectileType == VanillaProjectileIds.DungeonFlame)
            intent = intent with { InitialAi = new ProjectileAiState(target.CenterX, target.CenterY, 0f) };
        mutations.TrySpawnProjectile(in committed, in intent, out _);
        return committed;
    }

    private static bool IsSupported(NpcTypeId type) =>
        type == VanillaNpcIds.GoblinSorcerer || type == VanillaNpcIds.Tim || type == VanillaNpcIds.RuneWizard ||
        type.Value is >= 281 and <= 286;
}
