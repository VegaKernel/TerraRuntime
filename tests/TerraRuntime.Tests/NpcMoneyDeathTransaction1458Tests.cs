using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class NpcMoneyDeathTransaction1458Tests
{
    public static IEnumerable<object[]> OriginalClosestRows() => BossRecovery1458Tests.Rows("NpcMoneyClosest1458")
        .Select(row => new object[] { row });

    [Theory, MemberData(nameof(OriginalClosestRows))]
    public void Closest_luck_player_matches_original_live_integer_body_dead_filter_and_ties(JsonElement row)
    {
        var npcs = new RuntimeNpcStore(1); var items = new RuntimeWorldItemStore();
        var players = new ClosestPlayers(row);
        var pipeline = new RuntimeNpcNetworkCombatPipeline(npcs, items, players, new PlayerAuthority(null, null),
            static () => 0, null, new(items), null, null, new(), false, false);
        var update = new NpcStateUpdate(VanillaNpcIds.EyeOfCthulhu.Value, (short)VanillaNpcIds.EyeOfCthulhu.Value,
            1000.75f, 1000.25f, 0, 0, 255, default,
            NpcSimulationState.Initial with { HitboxOverride = new(row.GetProperty("width").GetInt32(), row.GetProperty("height").GetInt32()) });
        Assert.True(npcs.TrySpawn(0, in update, out var npc));
        object?[] arguments = [npc, default(PlayerStateSnapshot)];
        var method = typeof(RuntimeNpcNetworkCombatPipeline).GetMethod("TryFindClosestPlayer",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        Assert.True((bool)method.Invoke(pipeline, arguments)!);
        var closest = Assert.IsType<PlayerStateSnapshot>(arguments[1]);
        Assert.Equal(row.GetProperty("chosen").GetByte(), closest.Player.Slot.Value);
        Assert.Equal(closest.Player.Slot.Value == 0 ? -1f : 1f, closest.Luck);
    }
    public static IEnumerable<object[]> OriginalClassicDeaths() => BossRecovery1458Tests.Rows("BossHealingCoupled1458")
        .Where(row => row.GetProperty("difficulty").GetInt32() == 0 && row.GetProperty("mode").GetInt32() == 0 &&
            row.GetProperty("type").GetInt32() is 4 or 13 or 14 or 15 or 35 or 50 or 113 or 125 or 126 or 127 or 134 or 222 or 245 or 262 or 266 or 398 or 657 or 668)
        .Select(row => new object[] { row });

    [Theory, MemberData(nameof(OriginalClassicDeaths))]
    public void Actual_lethal_request_commits_original_imported_recovery_money_healing_and_rng(JsonElement row)
    {
        var fixture = new BossRecoveryPipeline1458Tests.Fixture(row.GetProperty("seed").GetInt32());
        // The independent oracle isolates these callbacks; already unlocked town slime suppresses the
        // distinct KingSlime unlock callback, whose real preview/commit is covered separately.
        fixture.Clock.MarkSlimeBlueSpawnUnlocked();
        int type = row.GetProperty("type").GetInt32(); bool paired = row.GetProperty("paired").GetBoolean();
        var npc = fixture.Spawn(type, row.GetProperty("npcWidth").GetInt32(), row.GetProperty("npcHeight").GetInt32());
        if (paired && type is 125 or 126) fixture.Spawn(type == 125 ? 126 : 125);
        if (!paired && type is 13 or 14 or 15) fixture.Spawn(type == 14 ? 15 : 14);
        if (paired && type is 4 or 113) fixture.Daily.Record(type == 4 ? VanillaNpcIds.WallOfFlesh : VanillaNpcIds.EyeOfCthulhu);
        if (type == VanillaNpcIds.BrainOfCthulhu.Value)
        {
            // Source loot callback represents the exposed boss after its Creepers are gone.
            var exposed = new NpcStateUpdate(npc.Type, npc.NetId, npc.PositionX, npc.PositionY, 0, 0, 0,
                npc.Ai, npc.Simulation with { DontTakeDamage = false });
            Assert.True(fixture.Npcs.TryUpdate(npc.Handle, in exposed, out npc));
        }
        if (type == VanillaNpcIds.MoonLordCore.Value)
        {
            var update = new NpcStateUpdate(npc.Type, npc.NetId, npc.PositionX, npc.PositionY, 0, 0, 0,
                new(2, 600, 0, 0), npc.Simulation with { Life = 0 });
            Assert.True(fixture.Npcs.TryUpdate(npc.Handle, in update, out npc)); fixture.Pipeline.NpcAiStateCommitted(in npc);
        }
        else Assert.Equal(RuntimeProjectileNpcDamageResult.Killed, fixture.Hit(npc));
        Assert.False(fixture.Npcs.TryGet(npc.Handle, out _));
        var expected = row.GetProperty("drops").EnumerateArray().ToArray(); var actual = fixture.Items();
        Assert.Equal(expected.Length, actual.Length);
        for (int index = 0; index < expected.Length; index++)
        {
            var e = expected[index]; var a = actual[index];
            Assert.Equal((e.GetProperty("id").GetInt32(), e.GetProperty("prefix").GetInt32(), e.GetProperty("stack").GetInt32()),
                ((int)a.ItemNetId, (int)a.Prefix, (int)a.Stack));
            Assert.Equal((e.GetProperty("x").GetSingle(), e.GetProperty("y").GetSingle(), e.GetProperty("vx").GetSingle(), e.GetProperty("vy").GetSingle()),
                (a.PositionX, a.PositionY, a.VelocityX, a.VelocityY));
        }
        Assert.Equal(row.GetProperty("eoc").GetBoolean(), fixture.Daily.EyeKilled);
        Assert.Equal(row.GetProperty("wof").GetBoolean(), fixture.Daily.WallKilled);
        Assert.Equal(row.GetProperty("next").GetInt32(), fixture.Random.Next());
    }

    [Theory]
    [InlineData(390, false)] [InlineData(389, true)] [InlineData(400, false)]
    public void Pressure_rejection_preserves_NPC_daily_ledger_items_and_live_random(int occupied, bool held)
    {
        var fixture = new BossRecoveryPipeline1458Tests.Fixture(1458);
        for (int index = 0; index < occupied; index++)
            Assert.True(fixture.Store.TryAllocateDrop(new(0, 0, 0, 0, 1, 0, WorldItemOwnershipMode.None, 1, false, 0, 0), out _));
        WorldItemDropReservation reservation = default;
        if (held) Assert.True(fixture.Store.TryReserveDropSlot(out reservation));
        var npc = fixture.Spawn(VanillaNpcIds.EyeOfCthulhu.Value); var before = fixture.Random.Clone();
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected, fixture.Pipeline.TryStrikeEnvironment(npc.Handle, 100000));
        Assert.True(fixture.Npcs.TryGet(npc.Handle, out var unchanged)); Assert.Equal(npc, unchanged);
        Assert.True(before.HasSameState(fixture.Random)); Assert.False(fixture.Daily.EyeKilled);
        Assert.False(fixture.Progression.IsCompleted(TerraRuntime.World.VanillaWorldProgressionId.EyeOfCthulhu));
        Assert.Equal(occupied, fixture.Store.ActiveCount);
        if (held) Assert.True(fixture.Store.TryReleaseDropReservation(in reservation));
    }

    [Fact]
    public void Exact_eleven_remaining_slots_accept_and_duplicate_stale_generation_cannot_repeat()
    {
        var fixture = new BossRecoveryPipeline1458Tests.Fixture(1458);
        for (int index = 0; index < 389; index++)
            Assert.True(fixture.Store.TryAllocateDrop(new(0, 0, 0, 0, 1, 0, WorldItemOwnershipMode.None, 1, false, 0, 0), out _));
        var npc = fixture.Spawn(VanillaNpcIds.EyeOfCthulhu.Value);
        Assert.Equal(RuntimeProjectileNpcDamageResult.Killed, fixture.Hit(npc)); Assert.Equal(400, fixture.Store.ActiveCount);
        var before = fixture.Random.Clone(); Assert.Equal(RuntimeProjectileNpcDamageResult.Rejected, fixture.Hit(npc));
        Assert.True(before.HasSameState(fixture.Random));
    }

    private sealed class ClosestPlayers(JsonElement row) : IRuntimePlayerSlotSnapshotLookup
    {
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot player)
        {
            bool first = slot.Value == 0;
            player = default(PlayerStateSnapshot) with
            {
                Player = new(slot, new(1)), Revision = new(1),
                PositionX = row.GetProperty(first ? "ax" : "bx").GetSingle(),
                PositionY = row.GetProperty(first ? "ay" : "by").GetSingle(),
                IsDead = first && (row.GetProperty("mode").GetInt32() & 4) != 0,
                Luck = first ? -1f : 1f
            };
            return slot.Value < 2;
        }
    }
}
