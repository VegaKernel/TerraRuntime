using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Protocol.Multiplicity;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class NpcDefinitionOnlyWireRetention1458Tests
{
    [Fact]
    public void Explicit_import_and_update_preserve_all_source_bodies_life_flags_and_signed_identity()
    {
        int identities = 0;
        foreach (JsonElement row in Read("npc-source-default-roster-official-1458.json.gz"))
        {
            if (!Selected(row)) continue;
            identities++;
            var store = new RuntimeNpcStore(1);
            NpcStateUpdate state = Explicit(row, 0);
            Assert.True(store.TrySpawn(0, in state, out NpcSnapshot imported), $"type={state.Type} net={state.NetId} simulation={state.Simulation}");
            Assert.Equal(state.Simulation.Life, imported.Simulation.Life);
            Assert.Equal(state.Simulation.LifeMax, imported.Simulation.LifeMax);
            Assert.Equal(state.Simulation.HitboxOverride, imported.Simulation.HitboxOverride);
            Assert.Equal(state.Simulation.Scale, imported.Simulation.Scale);
            Assert.Equal(state.Simulation.Friendly, imported.Simulation.Friendly);
            Assert.Equal(state.Simulation.Immortal, imported.Simulation.Immortal);
            Assert.Equal(state.Simulation.DontTakeDamage, imported.Simulation.DontTakeDamage);
            Assert.Equal(state.Simulation.NoGravity, imported.Simulation.NoGravity);
            Assert.Equal(state.Simulation.NoTileCollide, imported.Simulation.NoTileCollide);
            NpcStateUpdate update = Explicit(row, 1);
            Assert.True(store.TryUpdate(imported.Handle, in update, out NpcSnapshot updated));
            Assert.Equal(imported.Handle, updated.Handle);
            Assert.Equal(imported.Revision.Value + 1, updated.Revision.Value);
            Assert.Equal(update.Type, updated.Type);
            Assert.Equal(update.NetId, updated.NetId);
            Assert.Equal(update.Simulation.Life, updated.Simulation.Life);
            Assert.Equal(update.Simulation.SpawnDifficulty, updated.Simulation.SpawnDifficulty);
            Assert.Equal(update.Simulation.HitboxOverride, updated.Simulation.HitboxOverride);
        }
        Assert.Equal(326, identities);
    }

    [Fact]
    public void Metadata_does_not_manufacture_manual_spawn_or_update_live_defaults()
    {
        int identities = 0;
        foreach (JsonElement row in Read("npc-source-default-roster-official-1458.json.gz"))
        {
            if (!Selected(row)) continue;
            identities++;
            var store = new RuntimeNpcStore(1);
            NpcStateUpdate state = Explicit(row, 0) with { Simulation = NpcSimulationState.Initial };
            Assert.True(store.TrySpawn(0, in state, out NpcSnapshot imported), $"type={state.Type} net={state.NetId} simulation={state.Simulation}");
            CheckUnknown(imported);
            Assert.True(store.TryUpdate(imported.Handle, in state, out NpcSnapshot updated));
            CheckUnknown(updated);
        }
        Assert.Equal(326, identities);
    }

    [Fact]
    public void Packet23_for_explicit_imports_matches_independent_original_SendData_bytes()
    {
        var metadata = Read("npc-source-default-roster-official-1458.json.gz")
            .ToDictionary(x => Int(x, "requested"));
        int frames = 0;
        var mismatches = new List<string>();
        foreach (JsonElement wire in Read("npc-definition-wire-official-1458.json.gz"))
        {
            JsonElement row = metadata[Int(wire, "requested")];
            if (!Selected(row) || Int(wire, "mode") == 2) continue;
            frames++;
            int mode = Int(wire, "mode");
            var store = new RuntimeNpcStore(1);
            NpcStateUpdate state = Explicit(row, mode);
            Assert.True(store.TrySpawn(0, in state, out NpcSnapshot imported), $"type={state.Type} net={state.NetId} simulation={state.Simulation}");
            RuntimeNpcSyncKind kind = mode == 0 ? RuntimeNpcSyncKind.Update : RuntimeNpcSyncKind.Spawn;
            Assert.True(RuntimeNpcPacketProjection.TryCreate(in imported, kind, out var projection));
            Assert.True(TerrariaNpcUpdateEncoder.TryEncode(in projection, out byte[] encoded));
            using var reader = new BinaryReader(new MemoryStream(encoded));
            var decoded = new global::Multiplicity.Packets.NpcUpdate(reader);
            Assert.Equal(imported.Type, decoded.NpcType);
            Assert.Equal(imported.NetId, decoded.NpcNetId);
            Assert.Equal((byte)1, decoded.Generation);
            Assert.Equal(projection.SpawnDifficulty, decoded.Difficulty);
            if (mode == 1) Assert.Equal(imported.Simulation.Life, decoded.Life);
            if (!Convert.FromHexString(wire.GetProperty("frame").GetString()!).AsSpan().SequenceEqual(encoded))
                mismatches.Add($"type={imported.Type} net={imported.NetId} mode={mode}: expected={wire.GetProperty("frame")} actual={Convert.ToHexString(encoded)}");
        }
        Assert.Equal(652, frames);
        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    [Fact]
    public void Original_packet23_roundtrips_generation255_nondefault_release_owner_and_negative_identity()
    {
        var metadata = Read("npc-source-default-roster-official-1458.json.gz").ToDictionary(x => Int(x, "requested"));
        int cases = 0;
        foreach (JsonElement wire in Read("npc-definition-wire-official-1458.json.gz").Where(x => Int(x, "mode") == 2))
        {
            cases++;
            JsonElement row = metadata[Int(wire, "requested")];
            NpcStateUpdate state = Explicit(row, 1);
            var store = new RuntimeNpcStore(1);
            Assert.True(store.TrySpawn(0, in state, out NpcSnapshot npc));
            npc = npc with { Handle = new NpcHandle(0, new NpcGeneration(255)) };
            Assert.True(RuntimeNpcPacketProjection.TryCreate(in npc, RuntimeNpcSyncKind.Spawn, out var projection));
            projection = projection with { ReleaseOwner = 7 };
            Assert.True(TerrariaNpcUpdateEncoder.TryEncode(in projection, out byte[] encoded));
            Assert.Equal(Convert.FromHexString(wire.GetProperty("frame").GetString()!), encoded);
            using var reader = new BinaryReader(new MemoryStream(encoded));
            var decoded = new global::Multiplicity.Packets.NpcUpdate(reader);
            Assert.Equal((byte)255, decoded.Generation);
            Assert.Equal(npc.NetId, decoded.NpcNetId);
            Assert.Equal(npc.Simulation.Life, decoded.Life);
            if (npc.Type == 442) Assert.Equal((byte)7, decoded.ReleaseOwner);
        }
        Assert.Equal(2, cases);
    }

    private static void CheckUnknown(in NpcSnapshot npc)
    {
        Assert.Equal(0, npc.Simulation.LifeMax);
        Assert.Null(npc.Simulation.HitboxOverride);
        Assert.Null(npc.Simulation.BaseDamage);
        Assert.Null(npc.Simulation.BaseDefense);
        Assert.Null(npc.Simulation.BaseLifeMax);
        Assert.Null(npc.Simulation.KnockBackResist);
        Assert.Null(npc.Simulation.Friendly);
        Assert.Null(npc.Simulation.Immortal);
        Assert.False(npc.Simulation.NoGravity);
        Assert.False(npc.Simulation.NoTileCollide);
        Assert.False(npc.Simulation.DontTakeDamage);
        Assert.False(RuntimeNpcPacketProjection.TryCreate(in npc, RuntimeNpcSyncKind.Update, out _));
    }

    private static NpcStateUpdate Explicit(JsonElement row, int mode)
    {
        int maximum = Int(row, "lifeMax");
        NpcSimulationState simulation = NpcSimulationState.Initial with
        {
            Life = mode == 0 ? maximum : Math.Max(0, maximum - 1), LifeMax = maximum,
            HitboxOverride = Int(row, "type") == 664 ? new(17, 23) : new(Int(row, "width"), Int(row, "height")),
            Scale = row.GetProperty("scale").GetSingle(),
            Friendly = row.GetProperty("friendly").GetBoolean(),
            Immortal = row.GetProperty("immortal").GetBoolean(),
            DontTakeDamage = row.GetProperty("dontTakeDamage").GetBoolean(),
            NoGravity = row.GetProperty("noGravity").GetBoolean(),
            NoTileCollide = row.GetProperty("noTileCollide").GetBoolean(),
            Hidden = row.GetProperty("hidden").GetBoolean(),
            DirectionX = -1, DirectionY = 1, SpriteDirection = 1,
            SpawnDifficulty = mode == 0 ? 1f : 1.75f
        };
        return new(Int(row, "type"), checked((short)Int(row, "netId")), 120.5f, 240.25f,
            1.5f, -2.25f, 255, new(0, -2.5f, 0, .25f), simulation);
    }

    private static bool Selected(JsonElement row) => Int(row, "lifeMax") > 0 &&
        VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(Int(row, "type")), new NpcNetId(Int(row, "netId")), out var definition) && definition.DefinitionOnly;
    private static int Int(JsonElement row, string name) => row.GetProperty(name).GetInt32();
    private static JsonElement[] Read(string name)
    {
        Assembly assembly = typeof(NpcDefinitionOnlyWireRetention1458Tests).Assembly;
        string logicalName = name.StartsWith("npc-definition-wire", StringComparison.Ordinal) ? "NpcDefinitionWire1458" : "NpcSourceDefaultRoster1458";
        string resource = assembly.GetManifestResourceNames().Single(x => x == logicalName || x.EndsWith(name, StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(resource)!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using JsonDocument document = JsonDocument.Parse(gzip);
        return document.RootElement.EnumerateArray().Select(x => x.Clone()).ToArray();
    }
}
