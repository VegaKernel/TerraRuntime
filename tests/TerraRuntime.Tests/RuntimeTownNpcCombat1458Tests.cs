using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeTownNpcCombat1458Tests
{
    [Theory]
    [InlineData(17, 320, 34, 30, 48)]
    [InlineData(18, 300, 34, 60, 583)]
    [InlineData(19, 900, 40, 30, 14)]
    [InlineData(22, 700, 30, 30, 1)]
    public void Admitted_attack_profiles_match_pinned_ai007_sets(
        int npcType,
        int dangerRange,
        int attackTime,
        int averageChance,
        int normalProjectile)
    {
        Assert.True(VanillaTownNpcProjectileAttackCatalog1458.TryGet(new NpcTypeId(npcType), out var profile));
        Assert.Equal(dangerRange, profile.DangerDetectRange);
        Assert.Equal(attackTime, profile.AttackTime);
        Assert.Equal(averageChance, profile.AttackAverageChance);
        Assert.Equal(normalProjectile, profile.NormalProjectile.Value);
    }

    [Fact]
    public void Merchant_enters_state_ten_and_commits_throwing_knife_after_source_tick_ten()
    {
        CombatFixture f = CombatFixture.Create(VanillaNpcIds.Merchant, hardMode: false);

        RuntimeTownNpcCombatTickSummary1458 first = f.Combat.Tick();

        Assert.Equal(1, first.AttacksStarted);
        Assert.True(f.Npcs.TryGetActive(0, out NpcSnapshot merchant));
        Assert.Equal(10f, merchant.Ai.Ai0);
        Assert.Equal(34f, merchant.Ai.Ai1);
        Assert.Equal(0, f.Projectiles.ActiveCount);

        for (int i = 0; i < 10; i++)
            f.Combat.Tick();

        Assert.Equal(1, f.Projectiles.ActiveCount);
        Assert.True(TryGetOnlyProjectile(f.Projectiles, out ProjectileSnapshot knife));
        Assert.Equal(VanillaProjectileIds.ThrowingKnife, knife.Type);
        Assert.Equal(12, knife.Damage);
    }

    [Fact]
    public void Arms_dealer_hardmode_burst_commits_four_bullets_on_source_ticks()
    {
        CombatFixture f = CombatFixture.Create(VanillaNpcIds.ArmsDealer, hardMode: true);

        f.Combat.Tick();
        for (int i = 0; i < 30; i++)
            f.Combat.Tick();

        Assert.Equal(4, f.Projectiles.ActiveCount);
        Span<ProjectileSnapshot> shots = stackalloc ProjectileSnapshot[4];
        int count = f.Projectiles.CopyActive(shots);
        Assert.Equal(4, count);
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(VanillaProjectileIds.Bullet, shots[i].Type);
            Assert.Equal(21, shots[i].Damage);
        }
    }

    [Theory]
    [InlineData(false, 1, 12)]
    [InlineData(true, 2, 37)]
    public void Guide_switches_to_fire_arrow_in_hardmode_and_uses_progression_difficulty_damage(
        bool hardMode,
        int projectileType,
        int expectedDamage)
    {
        CombatFixture f = CombatFixture.Create(
            VanillaNpcIds.Guide,
            hardMode,
            expertMode: hardMode);

        f.Combat.Tick();
        f.Combat.Tick();

        Assert.True(TryGetOnlyProjectile(f.Projectiles, out ProjectileSnapshot arrow));
        Assert.Equal(projectileType, arrow.Type.Value);
        Assert.Equal(expectedDamage, arrow.Damage);
    }

    [Fact]
    public void Solid_line_of_sight_blocks_attack_start()
    {
        CombatFixture f = CombatFixture.Create(VanillaNpcIds.Guide, hardMode: false, blockedLineOfSight: true);

        RuntimeTownNpcCombatTickSummary1458 tick = f.Combat.Tick();

        Assert.Equal(0, tick.AttacksStarted);
        Assert.Equal(0, f.Projectiles.ActiveCount);
        Assert.True(f.Npcs.TryGetActive(0, out NpcSnapshot guide));
        Assert.Equal(0f, guide.Ai.Ai0);
    }

    [Fact]
    public void Local_attack_cooldown_blocks_new_attack_until_it_expires()
    {
        CombatFixture f = CombatFixture.Create(VanillaNpcIds.Nurse, hardMode: false, localCooldown: 3f);

        Assert.Equal(0, f.Combat.Tick().AttacksStarted);
        Assert.Equal(0, f.Combat.Tick().AttacksStarted);
        Assert.Equal(1, f.Combat.Tick().AttacksStarted);
    }

    [Fact]
    public void Combat_books_and_world_progression_scale_chance_and_damage_source_shape()
    {
        var metadata = new WorldFileRuntimeMetadata
        {
            HardMode = true,
            DownedBoss1 = true,
            DownedBoss2 = true,
            DownedBoss3 = true,
            CombatBookWasUsed = true,
            CombatBookVolumeTwoWasUsed = true
        };
        CombatFixture f = CombatFixture.Create(
            VanillaNpcIds.Merchant,
            metadata,
            expertMode: false,
            masterMode: false);

        // 1 + books .5 + Eye .05 + evil .1 + Skeletron .1 + Hardmode .4 = 2.15.
        Assert.Equal(25, f.Combat.GetAttackDamage(12));
        // int(30 * 2 * .8 * .8 * .985^4) = 36.
        Assert.Equal(36, f.Combat.GetAttackChance(30));
    }

    private static bool TryGetOnlyProjectile(RuntimeProjectileStore projectiles, out ProjectileSnapshot projectile)
    {
        Span<ProjectileSnapshot> buffer = stackalloc ProjectileSnapshot[8];
        int count = projectiles.CopyActive(buffer);
        if (count != 1)
        {
            projectile = default;
            return false;
        }
        projectile = buffer[0];
        return true;
    }

    private sealed class CombatFixture
    {
        private CombatFixture(
            RuntimeNpcStore npcs,
            RuntimeProjectileStore projectiles,
            RuntimeTownNpcCombat1458 combat)
        {
            Npcs = npcs;
            Projectiles = projectiles;
            Combat = combat;
        }

        public RuntimeNpcStore Npcs { get; }
        public RuntimeProjectileStore Projectiles { get; }
        public RuntimeTownNpcCombat1458 Combat { get; }

        public static CombatFixture Create(
            NpcTypeId townType,
            bool hardMode,
            bool expertMode = false,
            bool masterMode = false,
            bool blockedLineOfSight = false,
            float localCooldown = 0f) =>
            Create(
                townType,
                new WorldFileRuntimeMetadata { HardMode = hardMode },
                expertMode,
                masterMode,
                blockedLineOfSight,
                localCooldown);

        public static CombatFixture Create(
            NpcTypeId townType,
            WorldFileRuntimeMetadata metadata,
            bool expertMode,
            bool masterMode,
            bool blockedLineOfSight = false,
            float localCooldown = 0f)
        {
            var tiles = new WorldTileStore(new WorldDimensions(100, 100));
            if (blockedLineOfSight)
            {
                for (int y = 4; y <= 20; y++)
                {
                    tiles.Set(20, y, new WorldTile
                    {
                        Type = checked((ushort)VanillaTileIds.Stone.Value),
                        Flags = WorldTileFlags.Active
                    });
                }
            }

            var persistence = new WorldNpcPersistence(
                [],
                [new WorldTownNpc(townType.Value, "Town", 160f, 160f, true, 10, 14, null, false)],
                []);
            var town = new RuntimeTownNpcStateStore(persistence, [], tiles.Dimensions);
            var npcs = new RuntimeNpcStore();
            Assert.True(town.TryReserveRuntimeSlots(npcs));
            Assert.True(npcs.TryGetActive(0, out NpcSnapshot resident));
            if (localCooldown > 0f)
            {
                var update = new NpcStateUpdate(
                    resident.Type,
                    resident.NetId,
                    resident.PositionX,
                    resident.PositionY,
                    resident.VelocityX,
                    resident.VelocityY,
                    resident.Target,
                    resident.Ai,
                    resident.Simulation with
                    {
                        LocalAi = resident.Simulation.LocalAi with { Ai1 = localCooldown }
                    });
                Assert.True(npcs.TryUpdate(resident.Handle, in update, out _));
            }

            Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.Zombie, out VanillaNpcDefinition zombie));
            var hostile = new NpcStateUpdate(
                VanillaNpcIds.Zombie.Value,
                checked((short)VanillaNpcIds.Zombie.Value),
                400f,
                160f,
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

            var projectiles = new RuntimeProjectileStore(capacity: 32);
            RuntimeTownNpcCombatWorldFacts1458 facts = RuntimeTownNpcCombatWorldFacts1458.FromMetadata(metadata);
            RuntimeWorldProgressionMutations progression = RuntimeWorldProgressionRegistry.GetOrCreate(tiles);
            var combat = new RuntimeTownNpcCombat1458(
                town,
                npcs,
                projectiles,
                tiles,
                in facts,
                progression,
                expertMode,
                masterMode,
                new ZeroRandom());
            return new CombatFixture(npcs, projectiles, combat);
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
}
