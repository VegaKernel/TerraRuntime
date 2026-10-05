using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeTownNpcSchedule1458
{
    private const float NurseAttackDuration = 34f;
    private const float NurseProjectileSpeed = 8f;
    private const float NurseHealingRange = 500f;

    private bool TryPlanNurseAdmission(NpcHandle actor, in NpcStateUpdate body, ReadOnlySpan<NpcSnapshot> peers,
        out NpcStateUpdate next, out bool force)
    {
        next = body;
        force = false;
        if (body.Type != VanillaNpcIds.Nurse.Value || body.Ai.Ai0 >= 2f || body.VelocityY != 0f) return true;
        if (body.Simulation.Wet || !body.Simulation.Breath.HasValue) return false;
        if (body.Simulation.Breath <= 0) return true;
        if (!VanillaNpcDefinitionCatalog.TryGet(new(body.Type), new(body.NetId), out var definition) ||
            !definition.TryResolveHitbox(body.Simulation, out var size)) return false;
        float sx = body.PositionX + size.Width * .5f, sy = body.PositionY + size.Height * .5f;
        NpcSnapshot? selected = null;
        foreach (NpcSnapshot retained in peers)
        {
            // Original Main.npc[whoAmI] already contains this actor's pre-AI DoT and vitals.
            // Peer generations remain the unchanged captured table until their physical turn.
            NpcSnapshot candidate = retained.Handle == actor ? retained with { Simulation = body.Simulation,
                PositionX = body.PositionX, PositionY = body.PositionY } : retained;
            if (!VanillaNpcDefinitionCatalog.TryGet(candidate.TypeIdentity, candidate.NetIdentity, out var targetDefinition) ||
                targetDefinition.Role != NpcArchetypeRole.Town || candidate.Simulation.Life == candidate.Simulation.LifeMax)
                continue;
            if (selected is { } prior && candidate.Simulation.LifeMax - candidate.Simulation.Life <=
                prior.Simulation.LifeMax - prior.Simulation.Life) continue;
            if (!targetDefinition.TryResolveHitbox(candidate.Simulation, out var targetSize)) return false;
            if (!VanillaWorldLineOfSight.CanHitLine(tiles, body.PositionX + size.Width / 2,
                body.PositionY + size.Height / 2,
                candidate.PositionX + targetSize.Width / 2, candidate.PositionY + targetSize.Height / 2)) continue;
            float dx = candidate.PositionX + targetSize.Width * .5f - sx;
            float dy = candidate.PositionY + targetSize.Height * .5f - sy;
            if (MathF.Sqrt(dx * dx + dy * dy) >= NurseHealingRange) continue;
            selected = candidate;
        }
        if (selected is not { } target) return true;
        next = body with {
            Ai = body.Ai with { Ai0 = 13f, Ai1 = NurseAttackDuration, Ai2 = target.Handle.Slot },
            Simulation = body.Simulation with { DirectionX = body.PositionX < target.PositionX ? 1 : -1,
                LocalAi = body.Simulation.LocalAi with { Ai3 = 0f } } };
        force = true;
        return true;
    }

    private bool TryPlanActiveNurse(in NpcSnapshot source, ReadOnlySpan<NpcSnapshot> peers,
        out NpcStateUpdate next, out NpcAiProjectileIntent? projectile, out bool force)
    {
        next = default;
        projectile = null;
        force = false;
        NpcAiState ai = source.Ai with { Ai1 = source.Ai.Ai1 - 1f };
        NpcAiState local = source.Simulation.LocalAi with { Ai3 = source.Simulation.LocalAi.Ai3 + 1f };
        double clock = source.Ai.Ai1 == NurseAttackDuration ? 0d : source.Simulation.FrameCounter;
        if (local.Ai3 == 1f)
        {
            NpcSnapshot? selected = null;
            foreach (NpcSnapshot peer in peers)
                if (peer.Handle.Slot == (int)ai.Ai2) { selected = peer; break; }
            if (selected is not { } target ||
                !VanillaNpcDefinitionCatalog.TryGet(source.TypeIdentity, source.NetIdentity, out var ownDefinition) ||
                !ownDefinition.TryResolveHitbox(source.Simulation, out var ownSize) ||
                !VanillaNpcDefinitionCatalog.TryGet(target.TypeIdentity, target.NetIdentity, out var targetDefinition) ||
                !targetDefinition.TryResolveHitbox(target.Simulation, out var targetSize)) return false;
            float cx = source.PositionX + ownSize.Width * .5f, cy = source.PositionY + ownSize.Height * .5f;
            float tx = target.PositionX + targetSize.Width * .5f, ty = target.PositionY + targetSize.Height * .5f;
            float dx = tx - cx, dy = ty - 20f - cy;
            float distance = MathF.Sqrt(dx * dx + dy * dy);
            dx *= 1f / distance;
            dy *= 1f / distance;
            int sprite = source.Simulation.SpriteDirection;
            if (float.IsNaN(dx) || float.IsNaN(dy) || Math.Sign(dx) == -sprite) { dx = sprite; dy = -1f; }
            projectile = new(VanillaProjectileIds.NurseSyringeHeal, cx + sprite * 16f, cy - 2f,
                dx * NurseProjectileSpeed, dy * NurseProjectileSpeed, 0, 0f) {
                InitialAi = new(ai.Ai2, 0f, 0f) };
        }
        if (ai.Ai1 <= 0f)
        {
            ai = ai with { Ai0 = 0f, Ai1 = 10 + random.Next(10), Ai2 = 0f };
            local = local with { Ai3 = 5 + random.Next(10) };
            force = true;
        }
        next = ToUpdate(in source) with { VelocityX = source.VelocityX * .8f, Ai = ai,
            Simulation = source.Simulation with { LocalAi = local, FrameCounter = clock } };
        return true;
    }
}
