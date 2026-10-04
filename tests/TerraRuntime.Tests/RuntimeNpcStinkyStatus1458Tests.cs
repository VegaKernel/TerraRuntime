using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcStinkyStatus1458Tests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(180)] [InlineData(32767)]
    public void Source_flags_are_set_before_expiry_and_do_not_start_during_AddBuff(int duration)
    {
        var npcs = new RuntimeNpcStore(); NpcSnapshot npc = Spawn(npcs, 0, VanillaNpcIds.Merchant.Value);
        var owner = new RuntimeNpcStinkyStatus1458(npcs);
        Assert.True(owner.TryApply(npc.Handle, duration));
        Assert.True(owner.TryGetStinky(npc.Handle, out bool immediate)); Assert.False(immediate);
        int expired = 0;
        for (int tick = 0; tick < 3; tick++)
        {
            owner.BeginWorldTick(_ => expired++);
            JsonElement row = Rows().Single(x => Case(x) == "duration" &&
                x.GetProperty("time").GetInt32() == duration && !x.GetProperty("immune").GetBoolean() &&
                x.GetProperty("tick").GetInt32() == tick);
            Assert.True(owner.TryGetStinky(npc.Handle, out bool flag));
            Assert.Equal(row.GetProperty("flag").GetBoolean(), flag);
            Assert.True(owner.TryGetWireDuration(npc.Handle, out int time));
            Assert.Equal(row.GetProperty("buffType").GetInt32() == 0 ? -1 : row.GetProperty("remaining").GetInt32(), time);
        }
        Assert.Equal(duration <= 2 ? 1 : 0, expired);
    }

    [Fact]
    public void Original_SetDefaults_sweep_pins_all_697_status_immunities()
    {
        JsonElement row = Rows().Single(x => Case(x) == "sourceDefaultsStinkyImmunity");
        var immune = row.GetProperty("immuneTypes").EnumerateArray().Select(x => x.GetInt32()).ToHashSet();
        Assert.Equal(VanillaNpcStinkyCatalog1458.VerifiedNpcTypeCount, row.GetProperty("count").GetInt32());
        Assert.Empty(row.GetProperty("failures").EnumerateArray());
        for (int type = 0; type < row.GetProperty("count").GetInt32(); type++)
            Assert.Equal(immune.Contains(type), VanillaNpcStinkyCatalog1458.IsImmune(type));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(60)] [InlineData(180)] [InlineData(181)]
    public void Refresh_uses_original_maximum_and_same_generation_transforms_retain_duration(int refresh)
    {
        var npcs = new RuntimeNpcStore(); NpcSnapshot npc = Spawn(npcs, 0, VanillaNpcIds.Merchant.Value);
        var owner = new RuntimeNpcStinkyStatus1458(npcs);
        Assert.True(owner.TryApply(npc.Handle, 180)); Assert.True(owner.TryApply(npc.Handle, refresh));
        Assert.True(owner.TryGetWireDuration(npc.Handle, out int time));
        Assert.Equal(Rows().Single(x => Case(x) == "refresh" && x.GetProperty("refresh").GetInt32() == refresh)
            .GetProperty("remaining").GetInt32(), time);
        var transformed = State(VanillaNpcIds.Guide.Value);
        Assert.True(npcs.TryUpdate(npc.Handle, in transformed, out NpcSnapshot after));
        Assert.Equal(npc.Handle, after.Handle);
        Assert.True(owner.TryGetWireDuration(after.Handle, out int retained)); Assert.Equal(time, retained);
        Assert.True(npcs.TryDespawn(after.Handle));
        NpcSnapshot replacement = Spawn(npcs, 0, VanillaNpcIds.Merchant.Value);
        Assert.NotEqual(after.Handle, replacement.Handle);
        Assert.False(owner.TryApply(after.Handle, 180));
        Assert.True(owner.TryGetWireDuration(replacement.Handle, out int cleared)); Assert.Equal(-1, cleared);
    }

    [Theory]
    [InlineData(1, false)] [InlineData(1, true)] [InlineData(2, false)] [InlineData(2, true)]
    public void Viewer_uses_current_earlier_slot_and_previous_later_slot_flag(int duration, bool earlier)
    {
        var npcs = new RuntimeNpcStore();
        NpcSnapshot resident = Spawn(npcs, earlier ? (byte)1 : (byte)0, VanillaNpcIds.Merchant.Value);
        NpcSnapshot peer = Spawn(npcs, earlier ? (byte)0 : (byte)1, VanillaNpcIds.Guide.Value);
        var owner = new RuntimeNpcStinkyStatus1458(npcs); Assert.True(owner.TryApply(peer.Handle, duration));
        for (int tick = 0; tick < 3; tick++)
        {
            owner.BeginWorldTick();
            Assert.True(owner.TryGetSourceOrderedStinky(resident.Handle, peer.Handle, out bool flag));
            JsonElement row = Rows().Single(x => Case(x) == "sourceSlotStatusView" &&
                x.GetProperty("duration").GetInt32() == duration && x.GetProperty("peerEarlier").GetBoolean() == earlier &&
                x.GetProperty("tick").GetInt32() == tick);
            Assert.Equal(row.GetProperty("peerFlagSeen").GetBoolean(), flag);
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(180)] [InlineData(32767)]
    public void Multiplicity_packets_53_and_54_match_original_SendData_bytes(int duration)
    {
        var request = new TerrariaNpcBuffState(0, (ushort)VanillaBuffIds.Stinky.Value, (short)duration);
        Assert.True(TerrariaNpcBuffCodec.TryEncodeAdd(in request, out byte[] add));
        Assert.True(TerrariaNpcBuffCodec.TryEncodeCurrent(0, duration, out byte[] update));
        foreach ((int message, byte[] frame) in new[] { (53, add), (54, update) })
        {
            JsonElement row = Rows().Single(x => Case(x) == "packetGolden" &&
                x.GetProperty("message").GetInt32() == message && x.GetProperty("duration").GetInt32() == duration);
            Assert.Equal(row.GetProperty("hex").GetString(), Convert.ToHexString(frame));
        }
    }

    [Theory]
    [InlineData(-1, 120, 180)] [InlineData(200, 120, 180)] [InlineData(0, 20, 180)] [InlineData(0, 120, -1)]
    public void Invalid_or_unowned_buff_proposal_is_rejected_before_queue(int slot, int buff, int duration)
    {
        var request = new TerrariaNpcBuffState((short)slot, (ushort)buff, (short)duration);
        Assert.False(TerrariaNpcBuffCodec.IsValid(in request));
        Assert.False(TerrariaNpcBuffCodec.TryEncodeAdd(in request, out _));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void Water_and_lava_remove_the_list_only_after_the_source_flag_was_observed(bool lava, bool flagged)
    {
        var npcs = new RuntimeNpcStore(); NpcSnapshot npc = Spawn(npcs, 0, VanillaNpcIds.Merchant.Value);
        var wet = State(npc.Type) with { Simulation = npc.Simulation with { Wet = true,
            LiquidContact = lava ? NpcLiquidContactKind.Lava : NpcLiquidContactKind.Water } };
        Assert.True(npcs.TryUpdate(npc.Handle, in wet, out npc));
        var owner = new RuntimeNpcStinkyStatus1458(npcs); Assert.True(owner.TryApply(npc.Handle, 180));
        if (flagged) { owner.BeginWorldTick(); Assert.True(owner.TryApply(npc.Handle, 180)); }
        Assert.True(owner.TryRemoveWetStatus(npc.Handle));
        JsonElement row = Rows().Single(x => Case(x) == "wetRemoval" &&
            x.GetProperty("lava").GetBoolean() == lava && x.GetProperty("flag").GetBoolean() == flagged);
        Assert.True(owner.TryGetWireDuration(npc.Handle, out int remaining));
        Assert.Equal(row.GetProperty("buffType").GetInt32() == 0 ? -1 : row.GetProperty("remaining").GetInt32(), remaining);
        Assert.True(owner.TryGetStinky(npc.Handle, out bool current)); Assert.Equal(row.GetProperty("afterFlag").GetBoolean(), current);
        owner.BeginWorldTick(); Assert.True(owner.TryGetStinky(npc.Handle, out current)); Assert.Equal(!flagged, current);
    }

    [Theory]
    [InlineData(0, 180, 1)] [InlineData(10, 180, 1)] [InlineData(11, 11, 0)] [InlineData(180, 180, 0)]
    public void Source_repeated_tile_offer_refreshes_only_at_ten_ticks_and_does_not_set_current_flag(
        int duration, int expected, int broadcasts)
    {
        var npcs = new RuntimeNpcStore(); NpcSnapshot npc = Spawn(npcs, 0, VanillaNpcIds.Merchant.Value);
        int changed = 0; var owner = new RuntimeNpcStinkyStatus1458(npcs, _ => changed++);
        if (duration > 0) Assert.True(owner.TryApply(npc.Handle, duration));
        Assert.True(owner.TryApplyRepeated(npc.Handle)); Assert.Equal(broadcasts, changed);
        Assert.True(owner.TryGetWireDuration(npc.Handle, out int current)); Assert.Equal(expected, current);
        Assert.True(owner.TryGetStinky(npc.Handle, out bool immediate)); Assert.False(immediate);
    }

    private static NpcStateUpdate State(int type) => new(type, (short)type, 100f, 100f, 0f, 0f, 255, default,
        NpcSimulationState.Initial with { Life = 250, LifeMax = 250 });
    private static NpcSnapshot Spawn(RuntimeNpcStore npcs, byte slot, int type)
    {
        var state = State(type); Assert.True(npcs.TrySpawn(slot, in state, out NpcSnapshot npc)); return npc;
    }
    private static string? Case(JsonElement row) => row.GetProperty("caseName").GetString();
    private static JsonElement[] Rows()
    {
        using Stream stream = typeof(RuntimeNpcStinkyStatus1458Tests).Assembly.GetManifestResourceStream("NpcStinkyStatus1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using JsonDocument document = JsonDocument.Parse(gzip);
        return document.RootElement.GetProperty("rows").EnumerateArray().Select(x => x.Clone()).ToArray();
    }
}
