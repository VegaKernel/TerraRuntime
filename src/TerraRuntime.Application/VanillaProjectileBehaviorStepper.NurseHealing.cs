using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Application;

internal static partial class VanillaProjectileBehaviorStepper
{
    private const int NurseMaximumHeal = 20;

    private static bool TryStepNurseHealing(in ProjectileSnapshot current,
        in VanillaProjectileDefinition definition, in VanillaProjectileBehaviorContext context,
        out VanillaProjectileBehaviorResult next)
    {
        next = default;
        // The source reads the current physical target slot each AI phase, including a replacement.
        // Retain the resulting generation/revision only for this speculative healing transaction.
        if (context.NpcTargets is null) return false;
        float speed = context.LocalAi.Ai1;
        if (speed == 0f) speed = MathF.Sqrt(current.VelocityX * current.VelocityX + current.VelocityY * current.VelocityY);
        ProjectileLocalAiState local = context.LocalAi with { Ai1 = speed };
        if (!context.NpcTargets.IsNpcSlotAddressable((int)current.Ai.Ai0) ||
            !context.NpcTargets.TryGetActiveNpc((int)current.Ai.Ai0, out var target) ||
            !VanillaNpcDefinitionCatalog.TryGet(target.TypeIdentity, target.NetIdentity, out var targetDefinition) ||
            targetDefinition.Role != NpcArchetypeRole.Town)
        {
            next = new(current.VelocityX, current.VelocityY, current.Ai.Ai0,
                Kill: true, LocalAiOverride: local);
            return true;
        }
        if (!targetDefinition.TryResolveHitbox(target.Simulation, out var size)) return false;
        float dx = target.PositionX + size.Width * .5f - (current.PositionX + definition.Width * .5f);
        float dy = target.PositionY + size.Height * .5f - (current.PositionY + definition.Height * .5f);
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        bool overlap = (int)current.PositionX < (int)target.PositionX + size.Width &&
            (int)current.PositionX + definition.Width > (int)target.PositionX &&
            (int)current.PositionY < (int)target.PositionY + size.Height &&
            (int)current.PositionY + definition.Height > (int)target.PositionY;
        if (distance < speed || overlap)
        {
            int amount = Math.Min(NurseMaximumHeal, target.Simulation.LifeMax - target.Simulation.Life);
            ProjectileNpcHealingApplication? heal = amount > 0
                ? new(target.Handle, target.Revision, amount) : null;
            next = new(current.VelocityX, current.VelocityY, current.Ai.Ai0,
                Kill: true, LocalAiOverride: local, NpcHealing: heal);
            return true;
        }
        dx *= 1f / distance;
        dy *= 1f / distance;
        dx *= speed;
        dy *= speed;
        dy = Math.Max(dy, current.VelocityY) + 1f;
        // XNA Vector2.Lerp evaluates the multiplication before addition.
        float vx = current.VelocityX + (dx - current.VelocityX) * .04f;
        float vy = current.VelocityY + (dy - current.VelocityY) * .04f;
        next = new(vx, vy, current.Ai.Ai0, LocalAiOverride: local);
        return true;
    }
}
