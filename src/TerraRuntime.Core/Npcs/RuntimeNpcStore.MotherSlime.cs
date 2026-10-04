using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

public sealed partial class RuntimeNpcStore
{
    // HitEffect constructs a fresh Blue Slime, resets it to Baby Slime, then assigns motion.
    // Keep this construction private: the only public birth is the completed Baby Slime.
    internal bool TrySpawnMotherSlimeChild(in NpcSnapshot parent, int bottomX, int bottomY,
        int index, IVanillaNpcRandom random, out NpcSnapshot child)
    {
        child = default;
        if (!MatchesSource(in parent))
            return false;
        int type = VanillaNpcIds.BlueSlime.Value;
        short netId = checked((short)type);
        if (!TryCaptureSpawnDefaults(ref type, ref netId, out var blueDefaults, out float difficulty,
            out var context) || !MatchesSource(in parent) ||
            !VanillaNpcSpawnDefaults.TryResolveMotherSlimeChild(in context, out var babyDefaults, out float moneyValue))
            return false;

        var blue = new NpcStateUpdate(type, netId, bottomX - 12f, bottomY - 18f, 0f, 0f, 255,
            default, NpcSimulationState.Initial with
            {
                TimeLeft = VanillaNpcDefinitionCatalog.NewNpcTimeLeft,
                SpawnDifficulty = difficulty
            });
        bool allocated = TrySpawnVanillaCore(in blue, out var birth, 0, blueDefaults, publish: false);
        // NewNPC returns sentinel 200 on exhaustion; HitEffect still consumes all three motion draws.
        float velocityX = parent.VelocityX * 2f;
        float velocityY = parent.VelocityY;
        velocityX += random.NextInt32(-20, 20) * .1f + index * parent.Simulation.DirectionX * .3f;
        velocityY -= random.NextInt32(0, 10) * .1f + index;
        var ai = new NpcAiState(-1000f * random.NextInt32(0, 3), 0f, 0f, 0f);
        if (!allocated || !MatchesSource(in birth) || !MatchesSource(in parent))
            return false;

        var update = new NpcStateUpdate(type, checked((short)VanillaNpcNetVariantCatalog.BabySlime.Value),
            bottomX - babyDefaults.Hitbox.Width / 2, bottomY - babyDefaults.Hitbox.Height,
            velocityX, velocityY, 255, ai, NpcSimulationState.Initial with
            {
                TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft,
                DirectionY = 1,
                Life = babyDefaults.LifeMax,
                LifeMax = babyDefaults.LifeMax,
                BaseLifeMax = babyDefaults.LifeMax,
                BaseDamage = babyDefaults.Damage,
                BaseDefense = babyDefaults.Defense,
                DamageOverride = babyDefaults.Damage,
                DefenseOverride = babyDefaults.Defense,
                SpawnDifficulty = difficulty,
                KnockBackResist = babyDefaults.KnockBackResist,
                HitboxOverride = new(babyDefaults.Hitbox.Width, babyDefaults.Hitbox.Height),
                Scale = babyDefaults.Scale,
                Alpha = 120,
                MoneyValue = moneyValue
            });
        ref SlotState state = ref _slots[birth.Handle.Slot];
        state.Update = RuntimeNpcStateOwnershipPolicy.MaterializeSpawnDefaults(in update, babyDefaults);
        MarkSlotMutation();
        child = Capture(birth.Handle.Slot, in state);
        _commitSink?.NpcStateCommitted(NpcStateCommitKind.Spawn, in child);
        return true;
    }
}
