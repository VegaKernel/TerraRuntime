using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;

/// <summary>
/// Generation-safe runtime mirror of Projectile.localNPCImmunity for the source-backed projectile slice.
/// Vanilla owns this state on the projectile and both Damage_PVE and target acquisition consult it; keeping one
/// shared registry prevents authoritative collision and projectile AI from diverging about recently hit NPCs.
/// </summary>
internal sealed class RuntimeProjectileNpcLocalImmunityRegistry
{
    private readonly int projectileCapacity;
    private readonly int npcCapacity;
    private readonly ProjectileGeneration[] projectileGenerations;
    private readonly NpcGeneration[] npcGenerations;
    private readonly long[] lastHitTicks;

    public RuntimeProjectileNpcLocalImmunityRegistry(int projectileCapacity, int npcCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(projectileCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(npcCapacity);
        this.projectileCapacity = projectileCapacity;
        this.npcCapacity = npcCapacity;
        int cells = checked(projectileCapacity * npcCapacity);
        projectileGenerations = new ProjectileGeneration[cells];
        npcGenerations = new NpcGeneration[cells];
        lastHitTicks = new long[cells];
        Array.Fill(lastHitTicks, long.MinValue);
    }

    public bool IsImmune(ProjectileHandle projectile, NpcHandle target, long tick, int cooldownTicks)
    {
        if (!projectile.IsAssigned || !target.IsAssigned || cooldownTicks == 0 || tick < 0)
            return true;

        int index = GetIndex(projectile, target);
        if (projectileGenerations[index] != projectile.Generation || npcGenerations[index] != target.Generation)
            return false;

        if (cooldownTicks < 0)
            return true;

        long previous = lastHitTicks[index];
        return previous != long.MinValue && tick - previous < cooldownTicks;
    }

    public void MarkHit(ProjectileHandle projectile, NpcHandle target, long tick)
    {
        if (!projectile.IsAssigned)
            throw new ArgumentException("Projectile handle must be assigned.", nameof(projectile));
        if (!target.IsAssigned)
            throw new ArgumentException("NPC handle must be assigned.", nameof(target));
        ArgumentOutOfRangeException.ThrowIfNegative(tick);

        int index = GetIndex(projectile, target);
        projectileGenerations[index] = projectile.Generation;
        npcGenerations[index] = target.Generation;
        lastHitTicks[index] = tick;
    }

    private int GetIndex(ProjectileHandle projectile, NpcHandle target)
    {
        if ((uint)projectile.Slot >= (uint)projectileCapacity)
            throw new ArgumentOutOfRangeException(nameof(projectile));
        if ((uint)target.Slot >= (uint)npcCapacity)
            throw new ArgumentOutOfRangeException(nameof(target));
        return checked(projectile.Slot * npcCapacity + target.Slot);
    }
}
