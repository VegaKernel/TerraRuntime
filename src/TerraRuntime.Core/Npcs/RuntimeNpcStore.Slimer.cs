using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

public sealed partial class RuntimeNpcStore
{
    /// <summary>Owns Slimer's private construction/reset and single completed outward child birth.</summary>
    internal bool TryExecuteSlimerDeathSpawn(in NpcSnapshot parent, out bool spawned, out NpcSnapshot child)
    {
        child = default;
        spawned = false;
        var simulation = parent.Simulation;
        if (parent.TypeIdentity != VanillaNpcIds.Slimer || parent.Simulation.Life != 0 ||
            !MatchesSource(in parent) || !VanillaNpcDefinitionCatalog.TryGet(parent.TypeIdentity, out var parentDefinition) ||
            !parentDefinition.TryResolveHitbox(in simulation, out var parentBody)) return false;
        float bottomX = parent.PositionX + parentBody.Width / 2;
        float bottomY = parent.PositionY + parentBody.Height;
        if (!float.IsFinite(bottomX) || !float.IsFinite(bottomY) ||
            bottomX < int.MinValue || bottomX >= int.MaxValue || bottomY < int.MinValue || bottomY >= int.MaxValue) return false;
        int type = VanillaNpcIds.CorruptSlime.Value;
        short netId = checked((short)type);
        if (!TryCaptureSpawnDefaults(ref type, ref netId, out var initialDefaults, out float difficulty,
            out var context) || !MatchesSource(in parent) ||
            !VanillaNpcSpawnDefaults.TryResolveSlimerChild(in context, OperatingSystem.IsWindows(), out var defaults)) return false;
        // NewNPC's GoodWorld draw precedes the physical allocation, even when the source sentinel is returned.
        var initial = new NpcStateUpdate(type, netId, (int)bottomX, (int)bottomY, 0f, 0f, 255,
            default, NpcSimulationState.Initial with { SpawnDifficulty = difficulty });
        if (!TrySpawnVanillaCore(in initial, out var birth, 0, initialDefaults, publish: false)) return true;
        if (!MatchesSource(in birth) || !MatchesSource(in parent)) return false;
        var update = new NpcStateUpdate(VanillaNpcIds.CorruptSlime.Value, checked((short)VanillaNpcNetVariantCatalog.Slimer2.Value),
            (int)bottomX - defaults.Hitbox.Width / 2, (int)bottomY - defaults.Hitbox.Height,
            parent.VelocityX, parent.VelocityY, 255, default, NpcSimulationState.Initial with
            {
                DirectionY = 1, TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft,
                Life = defaults.LifeMax, LifeMax = defaults.LifeMax, BaseLifeMax = defaults.LifeMax,
                BaseDamage = defaults.Damage, BaseDefense = defaults.Defense,
                DamageOverride = defaults.Damage, DefenseOverride = defaults.Defense,
                SpawnDifficulty = difficulty, KnockBackResist = defaults.KnockBackResist,
                HitboxOverride = new(defaults.Hitbox.Width, defaults.Hitbox.Height), Scale = defaults.Scale,
                Alpha = 55, MoneyValue = defaults.VerifiedMoneyValue
            });
        ref SlotState state = ref _slots[birth.Handle.Slot];
        state.Update = RuntimeNpcStateOwnershipPolicy.MaterializeSpawnDefaults(in update, defaults);
        MarkSlotMutation();
        child = Capture(birth.Handle.Slot, in state);
        spawned = true;
        _commitSink?.NpcStateCommitted(NpcStateCommitKind.Spawn, in child);
        return true;
    }
}
