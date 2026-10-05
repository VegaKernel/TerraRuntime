using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeTownNpcDebuffOuter1458Tests
{
    public static IEnumerable<object[]> Cases()
    {
        using var stream = typeof(RuntimeTownNpcDebuffOuter1458Tests).Assembly.GetManifestResourceStream("TownNpcDebuffOuter1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(Cases))]
    public void Genuine_outer_UpdateNPC_continuous_flags_expiry_dot_ai_physics_and_rng_match(string json)
    {
        using var doc = JsonDocument.Parse(json); var row = doc.RootElement;
        int type = row.GetProperty("type").GetInt32(), start = row.GetProperty("start").GetInt32();
        var tiles = new WorldTileStore(new(100, 80));
        for (int x = 0; x < 100; x++) tiles.Set(x, 30, new() { Type = 1, Flags = WorldTileFlags.Active });
        var npcs = new RuntimeNpcStore();
        WorldTownNpc[] homes = start == 3 ? [new(type, "A", 639, 440, false, 40, 30, null, false),
            new(22, "B", 679, 440, false, 42, 30, null, false)] : [new(type, "A", 639, 440, false, 40, 30, null, false)];
        WorldTownRoom[] rooms = type == VanillaNpcIds.Guide.Value ? [new(type, 40, 30)] :
            [new(type, 40, 30), new(22, 42, 30)];
        var town = new RuntimeTownNpcStateStore(new([], homes, []), rooms, tiles.Dimensions);
        Assert.True(town.TryReserveRuntimeSlots(npcs));
        NpcStateUpdate Initial(int t, byte slot) => new(t, (short)t, slot == 0 ? 639 : 679, 440, 0, 0, 255,
            new(slot == 0 ? start : 4, 300, slot == 0 ? 1 : 0, 0), NpcSimulationState.Initial with {
                DirectionX = slot == 0 ? 1 : -1, SpriteDirection = slot == 0 ? 1 : -1,
                Life = 250, LifeMax = 250, BaseLifeMax = 250, BaseDefense = 0, KnockBackResist = 1,
                HitboxOverride = new(18, 40), LocalAi = new(0, 0, 0, 99), FrameIndex = 0, Breath = 200,
                LifeRegenCounter = slot == 0 ? row.GetProperty("counter").GetInt32() : 0 });
        Assert.True(npcs.TryGetActive(0, out var actor)); var own = Initial(type, 0);
        Assert.True(npcs.TryUpdate(actor.Handle, in own, out actor));
        if (start == 3)
        { Assert.True(npcs.TryGetActive(1, out var peer)); var p = Initial(22, 1); Assert.True(npcs.TryUpdate(peer.Handle, in p, out _)); }
        var expired = new List<byte[]>();
        RuntimeNpcBuffStatus1458? status = null;
        status = new(npcs, handle => {
            Span<TerraRuntime.Protocol.Multiplicity.TerrariaNpcBuffEntryState> buffs = stackalloc TerraRuntime.Protocol.Multiplicity.TerrariaNpcBuffEntryState[20];
            Assert.True(status!.TryCopyWireBuffs(handle, buffs, out int count));
            Assert.True(TerraRuntime.Protocol.Multiplicity.TerrariaNpcBuffCodec.TryEncodeCurrent(handle.Slot, buffs[..count], out var bytes)); expired.Add(bytes);
        });
        Assert.True(status.TryApply(actor.Handle, new(row.GetProperty("buff").GetInt32()), row.GetProperty("duration").GetInt32()));
        NpcSnapshot current = default;
        for (int tick = 1; tick <= row.GetProperty("tick").GetInt32(); tick++)
        {
            expired.Clear(); status.BeginWorldTick();
            var random = new VanillaUnifiedRandom1458(1458 + tick); var adapter = new SystemVanillaNpcRandom(random);
            var schedule = new RuntimeTownNpcSchedule1458(town, npcs, tiles, new NpcRuntimeTownScheduleRandom1458(adapter));
            var combat = new RuntimeTownNpcCombat1458(town, npcs, new RuntimeProjectileStore(32), tiles, default,
                new(), false, false, new NpcRuntimeTownCombatRandom1458(adapter));
            var conditions = new RuntimeTownNpcScheduleConditions1458(true, false, false, false, false);
            Assert.Equal(0, schedule.Tick(in conditions, [], status, combat).RejectedCommits);
            Assert.True(npcs.TryGet(actor.Handle, out current));
            if (tick != row.GetProperty("tick").GetInt32()) continue;
            AssertState(row.GetProperty("actor"), in current);
            Assert.True(status.TryGetDebuffs(current.Handle, out var flags));
            Assert.Equal(row.GetProperty("actor").GetProperty("fire").GetBoolean(), flags.OnFire);
            Assert.Equal(row.GetProperty("actor").GetProperty("poison").GetBoolean(), flags.Poisoned);
            Assert.True(status.TryGetStinky(current.Handle, out bool stinky)); Assert.Equal(row.GetProperty("actor").GetProperty("stinky").GetBoolean(), stinky);
            Assert.Equal(row.GetProperty("next").GetInt32(), random.Next());
            Assert.Equal(row.GetProperty("frames").EnumerateArray().Select(x => x.GetString()!).Where(x => x.Substring(4, 2) == "36"), expired.Select(Convert.ToHexString));
            if (start == 3) { Assert.True(npcs.TryGetActive(1, out var peer)); AssertState(row.GetProperty("peer"), in peer); }
        }
    }

    private static void AssertState(JsonElement row, in NpcSnapshot npc)
    {
        Assert.Equal(row.GetProperty("life").GetInt32(), npc.Simulation.Life);
        Assert.Equal(row.GetProperty("count").GetInt32(), npc.Simulation.LifeRegenCounter);
        Assert.Equal(row.GetProperty("regen").GetInt32(), npc.Simulation.FriendlyRegenerationCounter);
        Assert.Equal(row.GetProperty("x").GetSingle(), npc.PositionX); Assert.Equal(row.GetProperty("y").GetSingle(), npc.PositionY);
        Assert.Equal(row.GetProperty("vx").GetSingle(), npc.VelocityX); Assert.Equal(row.GetProperty("vy").GetSingle(), npc.VelocityY);
        Assert.Equal(row.GetProperty("frame").GetDouble(), npc.Simulation.FrameCounter);
        Assert.Equal(new NpcAiState(row.GetProperty("ai")[0].GetSingle(), row.GetProperty("ai")[1].GetSingle(), row.GetProperty("ai")[2].GetSingle(), row.GetProperty("ai")[3].GetSingle()), npc.Ai);
        Assert.Equal(new NpcAiState(row.GetProperty("local")[0].GetSingle(), row.GetProperty("local")[1].GetSingle(), row.GetProperty("local")[2].GetSingle(), row.GetProperty("local")[3].GetSingle()), npc.Simulation.LocalAi);
    }
}
