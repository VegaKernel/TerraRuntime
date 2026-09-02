using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeTownNpcMelee1458Tests
{
    [Theory]
    [InlineData(207, 60, 15, 1, 11, 32, 32, 4.25, 12, 6)]
    [InlineData(441, 50, 15, 1, 9, 28, 28, 3.5, 9, 3)]
    [InlineData(353, 60, 12, 1, 10, 32, 32, 5.0, 15, 8)]
    public void Attack_type_three_profiles_match_pinned_ai007_sets(
        int type,
        int dangerRange,
        int attackTime,
        int averageChance,
        int baseDamage,
        int width,
        int height,
        double knockBack,
        int recoveryBase,
        int recoveryRandom)
    {
        Assert.True(VanillaTownNpcMeleeAttackCatalog1458.TryGet(new NpcTypeId(type), out var profile));
        Assert.Equal(dangerRange, profile.DangerDetectRange);
        Assert.Equal(attackTime, profile.AttackTime);
        Assert.Equal(averageChance, profile.AttackAverageChance);
        Assert.Equal(baseDamage, profile.BaseDamage);
        Assert.Equal(width, profile.HitboxWidth);
        Assert.Equal(height, profile.HitboxHeight);
        Assert.Equal((float)knockBack, profile.KnockBack);
        Assert.Equal(recoveryBase, profile.RecoveryBase);
        Assert.Equal(recoveryRandom, profile.RecoveryRandom);
    }

    [Fact]
    public void Current_town_pets_remain_source_dead_for_natural_melee_entry()
    {
        foreach (int type in new[] { 637, 638, 656, 670, 678, 679, 680, 681, 682, 683, 684 })
        {
            var npcType = new NpcTypeId(type);
            Assert.True(VanillaTownNpcMeleeAttackCatalog1458.IsSourceTownPet(npcType));
            Assert.False(VanillaTownNpcMeleeAttackCatalog1458.TryGet(npcType, out _));
        }
    }

    [Fact]
    public void Dye_trader_enters_state_fifteen_and_commits_one_melee_hit_with_server_immunity()
    {
        MeleeFixture f = MeleeFixture.Create(new NpcTypeId(207), "Dye");

        RuntimeTownNpcCombatTickSummary1458 start = f.Combat.Tick();
        Assert.Equal(1, start.AttacksStarted);
        Assert.True(f.Npcs.TryGetActive(0, out NpcSnapshot dye));
        Assert.Equal(15f, dye.Ai.Ai0);
        Assert.Equal(15f, dye.Ai.Ai1);

        RuntimeTownNpcCombatTickSummary1458 swing = f.Combat.Tick();
        Assert.Equal(1, swing.MeleeHits);
        Assert.Single(f.Damage.Hits);
        Assert.Equal(11, f.Damage.Hits[0].Damage);
        Assert.Equal(4.25f, f.Damage.Hits[0].KnockBack);
        Assert.Equal(1, f.Damage.Hits[0].Direction);

        for (int i = 0; i < 6; i++)
            f.Combat.Tick();
        Assert.Single(f.Damage.Hits);
    }

    [Fact]
    public void Tax_collector_named_Andrew_doubles_source_damage_and_knockback_before_progression_scaling()
    {
        MeleeFixture f = MeleeFixture.Create(new NpcTypeId(441), "Andrew");

        f.Combat.Tick();
        f.Combat.Tick();

        MeleeHit hit = Assert.Single(f.Damage.Hits);
        Assert.Equal(18, hit.Damage);
        Assert.Equal(7f, hit.KnockBack);
    }

    [Fact]
    public void Swing_rectangle_matches_pinned_middle_and_late_phase_widening()
    {
        MeleeFixture f = MeleeFixture.Create(new NpcTypeId(353), "Stylist");
        Assert.True(f.Npcs.TryGetActive(0, out NpcSnapshot stylist));

        Assert.True(RuntimeTownNpcCombat1458.TryGetSwingRectangle(
            in stylist, 24, 11, 1, 32, 32, out VanillaTownNpcSwingRectangle1458 middle));
        Assert.Equal(32, middle.Width);
        Assert.Equal(32, middle.Height);

        Assert.True(RuntimeTownNpcCombat1458.TryGetSwingRectangle(
            in stylist, 24, 20, 1, 32, 32, out VanillaTownNpcSwingRectangle1458 late));
        Assert.Equal(64, late.Width);
        Assert.Equal(44, late.Height);
        Assert.True(late.X < middle.X);
    }

    [Fact]
    public void Production_melee_sink_finalizes_a_lethal_ordinary_npc_generation()
    {
        var npcs = new RuntimeNpcStore();
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(207), out VanillaNpcDefinition attackerDefinition));
        var attackerUpdate = new NpcStateUpdate(
            207,
            207,
            100f,
            100f,
            0f,
            0f,
            VanillaNpcDefinitionCatalog.DefaultTarget,
            default,
            NpcSimulationState.Initial with
            {
                Life = attackerDefinition.LifeMax,
                LifeMax = attackerDefinition.LifeMax
            });
        Assert.True(npcs.TrySpawnVanilla(in attackerUpdate, out NpcSnapshot attacker));

        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.BlueSlime, out VanillaNpcDefinition slimeDefinition));
        var targetUpdate = new NpcStateUpdate(
            VanillaNpcIds.BlueSlime.Value,
            checked((short)VanillaNpcIds.BlueSlime.Value),
            120f,
            100f,
            0f,
            0f,
            VanillaNpcDefinitionCatalog.DefaultTarget,
            default,
            NpcSimulationState.Initial with
            {
                Life = 1,
                LifeMax = slimeDefinition.LifeMax
            });
        Assert.True(npcs.TrySpawnVanilla(in targetUpdate, out NpcSnapshot target));

        var items = new RuntimeWorldItemStore();
        var leases = new RuntimeWorldItemInstancedLeaseStore(items);
        var pipeline = new RuntimeNpcNetworkCombatPipeline(
            npcs,
            items,
            EmptyPlayers.Instance,
            npcReplication: null,
            leases,
            worldItemReplication: null,
            worldClock: null,
            progression: new RuntimeWorldProgressionMutations(),
            expertMode: false,
            masterMode: false);

        RuntimeTownNpcMeleeDamageResult1458 result = pipeline.TryStrike(
            attacker.Handle,
            target.Handle,
            baseDamage: 100,
            knockBack: 0f,
            hitDirection: 1);

        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed, result);
        Assert.False(npcs.TryGet(target.Handle, out _));
    }

    private sealed class MeleeFixture
    {
        private MeleeFixture(RuntimeNpcStore npcs, RuntimeTownNpcCombat1458 combat, RecordingDamage damage)
        {
            Npcs = npcs;
            Combat = combat;
            Damage = damage;
        }

        public RuntimeNpcStore Npcs { get; }
        public RuntimeTownNpcCombat1458 Combat { get; }
        public RecordingDamage Damage { get; }

        public static MeleeFixture Create(NpcTypeId townType, string givenName)
        {
            var tiles = new WorldTileStore(new WorldDimensions(100, 100));
            var persistence = new WorldNpcPersistence(
                [],
                [new WorldTownNpc(townType.Value, givenName, 160f, 160f, true, 10, 14, null, false)],
                []);
            var town = new RuntimeTownNpcStateStore(persistence, [], tiles.Dimensions);
            var npcs = new RuntimeNpcStore();
            Assert.True(town.TryReserveRuntimeSlots(npcs));

            Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.Zombie, out VanillaNpcDefinition zombie));
            var hostile = new NpcStateUpdate(
                VanillaNpcIds.Zombie.Value,
                checked((short)VanillaNpcIds.Zombie.Value),
                180f,
                145f,
                0f,
                0f,
                VanillaNpcDefinitionCatalog.DefaultTarget,
                default,
                NpcSimulationState.Initial with
                {
                    Life = zombie.LifeMax,
                    LifeMax = zombie.LifeMax
                });
            Assert.True(npcs.TrySpawnVanilla(in hostile, out _));

            var projectiles = new RuntimeProjectileStore(capacity: 8);
            var metadata = new WorldFileRuntimeMetadata();
            RuntimeTownNpcCombatWorldFacts1458 facts = RuntimeTownNpcCombatWorldFacts1458.FromMetadata(metadata);
            RuntimeWorldProgressionMutations progression = new();
            var combat = new RuntimeTownNpcCombat1458(
                town,
                npcs,
                projectiles,
                tiles,
                in facts,
                progression,
                expertMode: false,
                masterMode: false,
                new ZeroRandom());
            var damage = new RecordingDamage();
            combat.SetMeleeDamageSink(damage);
            return new MeleeFixture(npcs, combat, damage);
        }
    }

    private readonly record struct MeleeHit(int Damage, float KnockBack, int Direction);

    private sealed class RecordingDamage : IRuntimeTownNpcMeleeDamageSink1458
    {
        public List<MeleeHit> Hits { get; } = [];

        public RuntimeTownNpcMeleeDamageResult1458 TryStrike(
            NpcHandle attacker,
            NpcHandle target,
            int baseDamage,
            float knockBack,
            int hitDirection)
        {
            Assert.True(attacker.IsAssigned);
            Assert.True(target.IsAssigned);
            Hits.Add(new MeleeHit(baseDamage, knockBack, hitDirection));
            return RuntimeTownNpcMeleeDamageResult1458.Committed;
        }
    }

    private sealed class ZeroRandom : IRuntimeTownNpcCombatRandom1458
    {
        public int Next(int exclusiveMax)
        {
            Assert.True(exclusiveMax > 0);
            return 0;
        }

        public float NextFloat(float inclusiveMin, float exclusiveMax) =>
            (inclusiveMin + exclusiveMax) * 0.5f;
    }

    private sealed class EmptyPlayers : IRuntimePlayerSlotSnapshotLookup
    {
        public static EmptyPlayers Instance { get; } = new();
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot)
        {
            snapshot = default;
            return false;
        }
    }
}
