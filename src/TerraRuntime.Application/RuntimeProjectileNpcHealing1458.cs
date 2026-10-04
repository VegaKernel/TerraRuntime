using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Application;

/// <summary>Accepted AI110 healing, after the projectile Kill publication and before later NPC phases.</summary>
internal sealed class RuntimeProjectileNpcHealing1458(RuntimeNpcStore npcs, RuntimeNpcReplicationRegistry? replication)
{
    public bool TryApply(in ProjectileNpcHealingApplication application)
    {
        if (application.Amount <= 0 || !npcs.TryGet(application.Target, out var target) ||
            target.Revision != application.Revision ||
            (long)target.Simulation.Life + application.Amount > target.Simulation.LifeMax) return false;
        NpcStateUpdate next = new(target.Type, target.NetId, target.PositionX, target.PositionY,
            target.VelocityX, target.VelocityY, target.Target, target.Ai,
            target.Simulation with { Life = target.Simulation.Life + application.Amount });
        if (!npcs.TryUpdateUnpublished(target.Handle, in next, out var committed)) return false;
        replication?.NpcBodyHealed(in committed, application.Amount);
        return true;
    }
}
