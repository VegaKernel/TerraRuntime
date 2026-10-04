using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class BannerClosest1458Tests
{
    [Fact]
    public void Retained_last_interaction_is_bounded_by_physical_slot_and_stale_forget_cannot_clear_replacement()
    {
        var store = new RuntimeNpcStore(1); var ledger = new RuntimeNpcPlayerInteractionLedger(store);
        NpcHandle stale = default;
        for (int index = 0; index < 512; index++)
        {
            var update = new NpcStateUpdate(1, 1, 100, 100, 0, 0, 255, default, NpcSimulationState.Initial);
            Assert.True(store.TrySpawn(0, update, out var npc));
            Assert.False(ledger.TryGetLastInteraction(npc.Handle, out _));
            var player = new PlayerHandle(new((byte)(index % 255)), new(1));
            Assert.True(ledger.TryMark(npc.Handle, player)); ledger.Forget(stale);
            Assert.True(ledger.TryGetLastInteraction(npc.Handle, out var slot)); Assert.Equal(player.Slot, slot);
            Assert.True(store.TryDespawn(npc.Handle)); Assert.False(ledger.TryGetLastInteraction(npc.Handle, out _));
            stale = npc.Handle;
        }
        var state = (System.Collections.IDictionary)typeof(RuntimeNpcPlayerInteractionLedger).GetField("_lastInteraction",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ledger)!;
        Assert.Single(state);
    }
    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Actual_banner_boundary_matches_original_squared_float_body_dead_ghost_and_ties(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        var npcs = new RuntimeNpcStore(1); var items = new RuntimeWorldItemStore();
        var pipeline = new RuntimeNpcNetworkCombatPipeline(npcs, items, new Players(row), new PlayerAuthority(null, null),
            static () => 0, null, new(items), null, null, new(), false, false);
        var update = new NpcStateUpdate(4, 4, 1000.75f, 1000.25f, 0, 0, 255, default,
            NpcSimulationState.Initial with { HitboxOverride = new(row.GetProperty("width").GetInt32(), row.GetProperty("height").GetInt32()) });
        Assert.True(npcs.TrySpawn(0, update, out var npc));
        object?[] arguments = [npc, default(PlayerStateSnapshot), ""];
        var method = typeof(RuntimeNpcNetworkCombatPipeline).GetMethod("TryFindBannerPlayer", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.True((bool)method.Invoke(pipeline, arguments)!);
        Assert.Equal(row.GetProperty("chosen").GetByte(), Assert.IsType<PlayerStateSnapshot>(arguments[1]).Player.Slot.Value);
    }
    public static IEnumerable<object[]> OriginalRows() => NpcDeathPrelude1458Tests.Rows("banner-closest");
    private sealed class Players(JsonElement row) : IRuntimePlayerSlotSnapshotLookup
    {
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot player)
        {
            bool first = slot.Value == 0; int mode = row.GetProperty("mode").GetInt32();
            player = default(PlayerStateSnapshot) with
            {
                Player = new(slot, new(1)), Revision = new(1),
                PositionX = row.GetProperty(first ? "ax" : "bx").GetSingle(),
                PositionY = row.GetProperty(first ? "ay" : "by").GetSingle(),
                IsDead = (mode & (first ? 1 : 4)) != 0,
                MovementFlags = (byte)((mode & (first ? 2 : 8)) != 0 ? 1 << 6 : 0)
            };
            return slot.Value < 2;
        }
    }
}
