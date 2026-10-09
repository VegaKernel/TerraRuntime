using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SelectedItemCritPhase1458Tests
{
    [Fact]
    public void Original_selected_switch_preserves_derived_class_crit_until_an_actual_player_phase()
    {
        using var source = Read("SelectedPrefixCritConfigured1458");
        Assert.Equal(18, source.RootElement.GetArrayLength());
        foreach (var row in source.RootElement.EnumerateArray())
        {
            string mode = row.GetProperty("mode").GetString()!;
            using var f = new Fixture(mode.StartsWith("bonus"), 1458);
            f.State.Tick();
            AssertCrit(row.GetProperty("phaseCrit"), f.Member);
            AssertRandom(row.GetProperty("phaseRng"), f.Random);
            if (mode.Contains("switch"))
            {
                var retained = f.Member.ItemPhase!.Value.DerivedCrit;
                f.Move(1);
                Assert.Equal(retained, f.Member.ItemPhase!.Value.DerivedCrit);
                if (mode.EndsWith("with-phase")) f.State.Tick();
            }
            AssertCrit(row.GetProperty("beforeHitCrit"), f.Member);
            // The fixture is a configured phase/Shoot/private-hit composition. This Fact
            // proves its phase boundary, without claiming ordinary dedicated Projectile.Update.
        }
    }

    [Fact]
    public void Source_constructor_dead_ghost_and_outside_have_distinct_crit_custody()
    {
        using var source = Read("SelectedCritEarly1458");
        using (var f = new Fixture(true, 0)) AssertCrit(source.RootElement.GetProperty("constructor"), f.Member);
        Assert.Equal(3, source.RootElement.GetProperty("rows").GetArrayLength());
        foreach (var row in source.RootElement.GetProperty("rows").EnumerateArray())
        {
            using var f = new Fixture(true, 1458);
            f.State.Tick();
            AssertCrit(row.GetProperty("living"), f.Member);
            f.Move(1);
            string mode = row.GetProperty("mode").GetString()!;
            if (mode == "dead")
            {
                f.Member.IsDead = true;
                f.Member.ItemPhase = f.Member.ItemPhase!.Value with { DeadTime = 17, RespawnTimer = 500 };
            }
            else if (mode == "ghost") f.Member.MovementFlags |= 64;
            else { f.Member.PositionX = 0; f.Member.PositionY = 0; }
            foreach (var step in row.GetProperty("steps").EnumerateArray())
            {
                f.State.Tick();
                AssertCrit(step.GetProperty("player"), f.Member);
                AssertRandom(step.GetProperty("rng"), f.Random);
            }
        }
    }

    [Fact]
    public async Task Real_transfer_preserves_known_and_unknown_crit_and_rejects_negative_fields_before_writes()
    {
        foreach (PlayerDerivedCritState1458? crit in new PlayerDerivedCritState1458?[]
            { new(4, 4, 4), new(9, 7, 5), null, new(-1, 4, 4), new(4, -1, 4), new(4, 4, -1) })
        {
            using var f = new Fixture(false, 0);
            var phase = f.Member.ItemPhase!.Value;
            f.Member.ItemPhase = phase with { DerivedCombat = crit is { } known
                ? phase.DerivedCombat!.Value with { MeleeCrit = known.Melee, RangedCrit = known.Ranged, MagicCrit = known.Magic }
                : null };
            var detach = new TaskCompletionSource<RuntimePlayerTransferState?>();
            f.State.Apply(new PlayerTransferDetachRuntimeCommand(f.Connection, detach));
            var transfer = Assert.IsType<RuntimePlayerTransferState>(await detach.Task);
            Assert.Equal(crit, transfer.ItemPhase!.Value.DerivedCrit);
            var inventory = (RuntimePlayerInventoryStore)typeof(PlayerAuthority)
                .GetField("inventory", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f.Players)!;
            ulong serial = inventory.Serial;
            var attach = new TaskCompletionSource<bool>();
            f.State.Apply(new PlayerTransferAttachRuntimeCommand(f.Connection, transfer, 100, 103, true, false, attach));
            bool valid = crit is not { Melee: < 0 } and not { Ranged: < 0 } and not { Magic: < 0 };
            Assert.Equal(valid, await attach.Task);
            if (valid) Assert.Equal(crit, f.Member.ItemPhase!.Value.DerivedCrit);
            else
            {
                Assert.False(f.Players.TryGet(f.Connection, out _));
                Assert.Equal(serial, inventory.Serial);
                Assert.False(f.Players.TryGetInventoryItem(f.Connection, 0, out _));
            }
        }
    }

    [Fact]
    public void Unknown_import_is_overwritten_only_by_a_genuine_living_phase()
    {
        using var f = new Fixture(true, 0);
        f.Member.ItemPhase = f.Member.ItemPhase!.Value with { DerivedCombat = null };
        f.Member.PositionX = 0; f.Member.PositionY = 0;
        f.State.Tick();
        Assert.Null(f.Member.ItemPhase!.Value.DerivedCrit);
        f.Move(0);
        f.State.Tick();
        Assert.Equal(new PlayerDerivedCritState1458(9, 9, 9), f.Member.ItemPhase!.Value.DerivedCrit);
        var random = f.Random.Clone();
        var before = f.Member.CaptureSnapshot();
        f.Players.SetNpcHealthWorldFacts(() =>
        {
            f.Member.ItemPhase = f.Member.ItemPhase!.Value with
            {
                DerivedCombat = f.Member.ItemPhase.Value.DerivedCombat!.Value with { MeleeCrit = 17, RangedCrit = 18, MagicCrit = 19 }
            };
            return new(false, false);
        });
        Assert.False(f.Players.TryTickRemotePlayerPhase());
        Assert.Equal(new PlayerDerivedCritState1458(17, 18, 19), f.Member.ItemPhase!.Value.DerivedCrit);
        Assert.Equal(before, f.Member.CaptureSnapshot());
        Assert.True(f.Random.HasSameState(random));
    }

    private static JsonDocument Read(string resource)
    {
        using var stream = typeof(SelectedItemCritPhase1458Tests).Assembly.GetManifestResourceStream(resource)!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    private static void AssertCrit(JsonElement source, RuntimePlayerMember member) =>
        Assert.Equal(new PlayerDerivedCritState1458(source.GetProperty("melee").GetInt32(),
            source.GetProperty("ranged").GetInt32(), source.GetProperty("magic").GetInt32()),
            member.ItemPhase!.Value.DerivedCrit);

    private static void AssertRandom(JsonElement source, VanillaUnifiedRandom1458 random)
    {
        Assert.Equal(source.GetProperty("cursor").GetUInt32(),
            typeof(VanillaUnifiedRandom1458).GetField("inext", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(random));
        Assert.Equal(source.GetProperty("state").EnumerateArray().Select(x => x.GetInt32()),
            (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(random)!);
    }

    internal sealed class Fixture : IDisposable
    {
        internal readonly ServerRuntimeState State;
        internal readonly PlayerAuthority Players;
        internal readonly RuntimeProjectileStore Projectiles = new();
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly ConnectionHandle Connection;
        private readonly PlayerJoinSession session;
        internal RuntimePlayerMember Member { get { Assert.True(Players.TryGet(0, out var member)); return member; } }

        internal Fixture(bool bonus, int seed)
        {
            var tiles = new WorldTileStore(new WorldDimensions(400, 300));
            for (int x = 0; x < 400; x++) tiles.Set(x, 103, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            State = new(worldTiles: tiles, projectiles: Projectiles, playerUpdateRandomSeed: new(seed));
            Players = ((ServerRuntimeComposition)typeof(ServerRuntimeState)
                .GetField("_runtime", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(State)!).Players;
            Random = (VanillaUnifiedRandom1458)typeof(PlayerAuthority)
                .GetField("playerUpdateRandom", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Players)!;
            Players.SetPlayerUpdateWorldFacts(new RuntimePlayerUpdateWorld1458(400, 300, 80, false, false)
                { WindowsItemPrefixArithmetic = OperatingSystem.IsWindows() });
            Players.SetRemotePlayerEnvironment(new(false, false), Projectiles);
            var pool = new PlayerSlotPool(1);
            Assert.True(pool.TryAcquireConnection(out var lease));
            session = new(lease!); session.ObserveWorldRequest(); session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(99801), session.Handle);
            State.Apply(new PlayerHealthRuntimeCommand(Connection, new(session.Slot, 400, 400)));
            State.Apply(new PlayerManaRuntimeCommand(Connection, new(session.Slot, 200, 200)));
            Equipment(0, 219, 1, (byte)(bonus ? 82 : 0));
            Equipment(1, 219, 1, (byte)(bonus ? 0 : 82));
            Equipment(54, 97, 20);
            State.Apply(new PlayerSpawnRuntimeCommand(Connection, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
            Move(0);
        }

        internal void Equipment(short slot, short type, short stack, byte prefix = 0) =>
            State.Apply(new PlayerEquipmentRuntimeCommand(Connection, new(new(0), slot, stack, prefix, type, 0)));

        internal void Move(byte selected) => State.Apply(new PlayerMovementRuntimeCommand(Connection,
            new(new(0), 64, 16, 0, 64, selected, 1600, 1606,
                false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));

        public void Dispose() => session.Dispose();
    }
}
