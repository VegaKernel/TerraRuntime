using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ServerPlayerLava1458Tests
{
    [Theory]
    [InlineData(false, false, false, 420)]
    [InlineData(true, false, false, 300)]
    [InlineData(false, true, false, 420)]
    [InlineData(false, true, true, 420)]
    public void Bare_actor_real_tick_takes_source_lava_damage(bool remix, bool expert, bool master, int life)
    {
        using var f = new Fixture(remix: remix, expert: expert, master: master);
        f.Tick();
        Assert.Equal(life, f.Player.Life);
    }

    [Fact]
    public void Terraspark_has_420_protected_updates_then_reduces_base_damage_to_35()
    {
        using var f = new Fixture(boots: true);
        f.Tick(420);
        Assert.Equal(500, f.Player.Life);
        f.Tick();
        Assert.Equal(465, f.Player.Life);
        f.Tick(14);
        Assert.Equal(465, f.Player.Life);
        f.Tick();
        Assert.Equal(464, f.Player.Life); // Ordinary OnFire: eight / 120, independent of armor.
    }

    [Fact]
    public void Lava_uses_a_separate_immunity_channel_from_npc_contact()
    {
        using var f = new Fixture();
        var npc = new NpcHandle(0, new NpcGeneration(1));
        Assert.Equal(PlayerDamageCommitResult.Committed, f.Authority.TryCommitAuthoritativeNpcContactDamage(
            0, npc, f.Handle, 10, 0, VanillaPlayerImmunityChannel1458.General, false, false, out _));
        f.Tick();
        Assert.Equal(410, f.Player.Life);
    }

    [Fact]
    public void Burning_bypasses_armor_and_hit_immunity_but_not_godmode()
    {
        using var f = new Fixture();
        f.Tick(); // 500 -> 420; source OnFire420.
        Assert.True(f.Authority.TryTeleport(f.Id, 500, 500));
        f.Tick(15);
        Assert.Equal(419, f.Player.Life);
        Assert.True(f.Authority.SetGodMode(f.Id, true));
        f.Tick(60);
        Assert.Equal(419, f.Player.Life);
    }

    [Theory]
    [InlineData(WorldLiquidKind.Water)]
    [InlineData(WorldLiquidKind.Honey)]
    public void Wet_non_lava_extinguishes_after_current_update_regen(WorldLiquidKind kind)
    {
        using var f = new Fixture();
        f.Tick();
        f.Fill(kind);
        f.Tick(20);
        Assert.Equal(420, f.Player.Life);
    }

    [Fact]
    public void Godmode_prevents_contact_damage_and_new_burning()
    {
        using var f = new Fixture();
        Assert.True(f.Authority.SetGodMode(f.Id, true));
        f.Tick(100);
        Assert.Equal(500, f.Player.Life);
        Assert.True(f.Authority.TryTeleport(f.Id, 500, 500));
        Assert.True(f.Authority.SetGodMode(f.Id, false));
        f.Tick(30);
        Assert.Equal(500, f.Player.Life);
    }

    [Fact]
    public void Vanity_boots_do_not_protect_and_unknown_functional_equipment_fails_closed()
    {
        using var f = new Fixture();
        Assert.True(f.Authority.SetItem(f.Id, new ServerPlayerItemState(
            (short)(VanillaPlayerItemSlotCatalog.ArmorStart + 13), new ItemTypeId(5000), 1, default, 0)));
        f.Tick();
        Assert.Equal(420, f.Player.Life);
        f.Equip(new ItemTypeId(2)); // Dirt cannot acquire accessory semantics.
        f.Tick(60);
        Assert.Equal(420, f.Player.Life);
    }

    [Fact]
    public void Dry_recharge_and_removing_then_reequipping_boots_do_not_refill_the_gauge()
    {
        using var f = new Fixture(boots: true);
        f.Tick(420);
        Assert.True(f.Authority.TryTeleport(f.Id, 500, 500));
        f.Tick(10);
        Assert.True(f.Authority.TryTeleport(f.Id, 160, 160));
        f.Tick(10);
        Assert.Equal(500, f.Player.Life);
        f.Equip(default);
        Assert.True(f.Authority.TryTeleport(f.Id, 500, 500));
        f.Tick();
        f.Equip(new ItemTypeId(5000));
        Assert.True(f.Authority.TryTeleport(f.Id, 160, 160));
        f.Tick();
        Assert.Equal(465, f.Player.Life);
    }

    [Fact]
    public void Recall_refills_protection_while_an_ordinary_teleport_does_not()
    {
        using var f = new Fixture(boots: true);
        f.Tick(420);
        Assert.True(f.Authority.TryTeleportWithRecallPresentation(f.Id, 11, 13));
        f.Tick(420);
        Assert.Equal(500, f.Player.Life);
        f.Tick();
        Assert.Equal(465, f.Player.Life);
    }

    [Fact]
    public void Death_and_generation_reuse_do_not_inherit_burning_or_empty_protection()
    {
        using var f = new Fixture();
        Assert.True(f.Authority.SetVitals(f.Id, new ServerPlayerVitalsState(1, 500, 200, 200)));
        f.Tick();
        Assert.True(f.Player.IsDead);
        Assert.True(f.Authority.Despawn(f.Id));
        var replacement = f.Authority.Create(f.Id, 160, 160);
        Assert.True(replacement.IsCreated);
        Assert.NotEqual(f.Handle.Generation, replacement.Player.Generation);
        f.Equip(new ItemTypeId(5000));
        Assert.True(f.Authority.SetVitals(f.Id, new ServerPlayerVitalsState(500, 500, 200, 200)));
        f.Tick(420);
        Assert.True(f.Authority.TryGet(replacement.Player, out var protectedPlayer));
        Assert.Equal(500, protectedPlayer.Life);
    }

    [Fact]
    public void Burning_expires_after_source_duration_without_permanent_damage()
    {
        using var f = new Fixture();
        f.Tick();
        Assert.True(f.Authority.TryTeleport(f.Id, 500, 500));
        f.Tick(420);
        Assert.Equal(392, f.Player.Life); // 80 contact + 420 * 8 / 120 regeneration loss.
        f.Tick(420);
        Assert.Equal(392, f.Player.Life);
    }

    [Fact]
    public void Unknown_world_facts_do_not_synthesize_environment_semantics()
    {
        using var f = new Fixture(knownWorld: false);
        f.Tick(60);
        Assert.Equal(500, f.Player.Life);
    }

    [Fact]
    public void Environment_provenance_is_typed_and_rejects_mixed_or_unknown_causes()
    {
        Assert.True(DamageSource.FromEnvironment(EnvironmentDamageCause.Lava).IsValid);
        Assert.True(DamageSource.FromEnvironment(EnvironmentDamageCause.Burning).IsValid);
        Assert.False(DamageSource.FromEnvironment((EnvironmentDamageCause)255).IsValid);
        Assert.False((DamageSource.Server with { EnvironmentCause = EnvironmentDamageCause.Lava }).IsValid);
    }

    private sealed class Fixture : IDisposable
    {
        public ServerPlayerId Id { get; } = new("test:lava");
        public ServerPlayerAuthority Authority { get; }
        public PlayerHandle Handle { get; }
        public ServerRuntimeState State { get; }
        private readonly WorldTileStore tiles = new(new WorldDimensions(80, 200));
        public PlayerStateSnapshot Player { get { Assert.True(Authority.TryGet(Handle, out var player)); return player; } }

        public Fixture(bool boots = false, bool remix = false, bool expert = false, bool master = false, bool knownWorld = true)
        {
            var slots = new PlayerSlotPool(2);
            var identities = new ServerPlayerSlotRegistry(slots);
            var states = new ServerPlayerStateStore(identities, slots.Capacity);
            Authority = new ServerPlayerAuthority(states, identities); // Stationary body, real environment/runtime tick.
            var created = Authority.Create(Id, 160, 160);
            Assert.True(created.IsCreated);
            Handle = created.Player;
            Assert.True(Authority.SetVitals(Id, new ServerPlayerVitalsState(500, 500, 200, 200)));
            if (boots) Equip(new ItemTypeId(5000));
            Fill(WorldLiquidKind.Lava);
            RuntimeTownCommerceWorldFacts1458 facts = default;
            State = new ServerRuntimeState(serverPlayers: Authority, worldTiles: tiles, expertMode: expert, masterMode: master,
                townCommerceWorldFacts: knownWorld ? facts with { RemixWorld = remix } : null);
        }
        public void Equip(ItemTypeId item) => Assert.True(Authority.SetItem(Id, new ServerPlayerItemState(
            (short)(VanillaPlayerItemSlotCatalog.ArmorStart + 3), item, (short)(item.IsNone ? 0 : 1), default, 0)));
        public void Fill(WorldLiquidKind kind)
        {
            for (int x = 8; x <= 15; x++)
            for (int y = 8; y <= 16; y++)
                tiles.Tiles[tiles.GetUncheckedIndex(x, y)] = x is 8 or 15 || y is 8 or 16
                    ? new WorldTile { Type = 1, Flags = WorldTileFlags.Active }
                    : new WorldTile { LiquidAmount = 255, LiquidKind = kind };
        }
        public void Tick(int count = 1) { for (int i = 0; i < count; i++) State.Tick(); }
        public void Dispose() => Authority.Despawn(Id);
    }
}
