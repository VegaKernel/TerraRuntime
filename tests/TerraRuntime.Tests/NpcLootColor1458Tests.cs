using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class NpcLootColor1458Tests
{
    public static IEnumerable<object[]> Original()
    {
        using var stream = typeof(NpcLootColor1458Tests).Assembly.GetManifestResourceStream("NpcLootColor1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(Original))]
    public void Actual_defaults_CommonCode_and_recorded_color_packet_match(string json)
    {
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        int I(string key) => row.GetProperty(key).GetInt32();
        var context = new NpcLootColorContext1458(new(I("type")), new(I("net")), row.GetProperty("remix").GetBoolean());
        Assert.True(VanillaNpcLootColor1458.TryResolve(in context, new(I("item")), out var color));
        var frames = row.GetProperty("frames");
        Assert.Equal(frames.GetArrayLength() == 1, color is not null);
        if (color is { } retained)
        {
            Assert.Equal(new WorldItemColor((byte)I("r"), (byte)I("g"), (byte)I("b"), (byte)I("a")), retained);
            Assert.Equal(Convert.FromHexString(frames[0].GetString()!), TerrariaWorldItemColorCodec1458.Encode((short)I("slot"), retained));
        }
        var sourceRandom = new VanillaUnifiedRandom1458(1458);
        Assert.Equal(I("next"), sourceRandom.Next()); // Actual source modification consumes no random draws.
        if (I("item") == VanillaItemIds.Gel.Value)
        {
            var withContext = new NpcLootWorldItemOrigin(100, 100, context);
            var plain = new NpcLootWorldItemOrigin(100, 100);
            var drop = new NpcLootDrop(VanillaItemIds.Gel, 1);
            var first = new Rolls(1458);
            var second = new Rolls(1458);
            Assert.True(VanillaNpcLootWorldItemMaterializer.Instance.TryMaterialize(in withContext, in drop, first, out var tinted));
            Assert.True(VanillaNpcLootWorldItemMaterializer.Instance.TryMaterialize(in plain, in drop, second, out var direct));
            Assert.Equal(color, tinted.Color);
            Assert.Null(direct.Color); // A plain Item.NewItem call does not apply CommonCode's postdrop modification.
            Assert.Equal(tinted with { Color = null }, direct);
            Assert.Equal(first.SourceRandom.Next(), second.SourceRandom.Next());
        }
    }

    [Fact]
    public void Unknown_remix_or_custom_slime_tint_reject_before_creation_random()
    {
        var random = new Rolls(1458);
        var expected = new VanillaUnifiedRandom1458(1458);
        var drop = new NpcLootDrop(VanillaItemIds.Gel, 1);
        foreach (var context in new[]
        {
            new NpcLootColorContext1458(VanillaNpcIds.LavaSlime, new(59), null),
            new NpcLootColorContext1458(VanillaNpcIds.BlueSlime, new(777), false)
        })
        {
            var origin = new NpcLootWorldItemOrigin(100, 100, context);
            Assert.False(VanillaNpcLootWorldItemMaterializer.Instance.TryMaterialize(in origin, in drop, random, out _));
        }
        Assert.Equal(expected.Next(), random.SourceRandom.Next());
    }

    [Fact]
    public void Retained_color_survives_partial_drop_owner_motion_and_generation_guards()
    {
        var store = new RuntimeWorldItemStore();
        var color = new WorldItemColor(0, 80, 255, 100);
        var drop = Drop() with { Color = color };
        Assert.True(store.TryAllocateDrop(drop, out var initial));
        Assert.Equal(color, initial.Color);
        Assert.True(store.TryApplyDrop(initial.Handle.Slot, Drop(), out var partial));
        Assert.Equal(color, partial.Color);
        Assert.True(store.TryApplyOwner(partial.Handle.Slot, new(0, 15, 0, 0, 1, 2), out var owned));
        Assert.True(store.TryAdvanceMotion(owned.Handle, 3, 4, 5, 6, out var moved));
        Assert.Equal(color, moved.Color);
        Assert.False(store.TryApplyColor(initial, new(1, 2, 3, 4), out _));
        Assert.True(store.TryApplyColor(moved, new(1, 2, 3, 4), out var changed));
        Assert.Equal(moved.Revision.Value + 1, changed.Revision.Value);
        Assert.True(store.TryRemove(changed.Handle.Slot, out _));
        Assert.True(store.TryAllocateDrop(Drop(), out var replacement));
        Assert.Null(replacement.Color);
        Assert.False(store.TryApplyColor(changed, color, out _));
        Assert.True(WorldItemBootstrapPacketEncoder.TryEncode([changed], out var baseline) == WorldItemBootstrapPacketEncodeResult.Encoded);
        Assert.Equal(new byte[] { 21, 22 }, baseline.Select(frame => frame.Span[2])); // Source join never replays88.
    }

    [Fact]
    public void Claimed_source_replacement_cannot_be_recolored_through_the_old_live_snapshot()
    {
        var store = new RuntimeWorldItemStore();
        for (short slot = 0; slot < 400; slot++)
            Assert.True(store.TryAllocateDrop(Drop() with { Stack = 9999 }, out _));
        store.TickReservationTimers(); // Source age makes the first oldest item the genuine overflow replacement.
        Assert.True(store.TryGetActive(0, out var before));
        using var allocation = store.CreateAllocationPreview();
        Assert.True(allocation.TrySpawnSource(Drop(), 0, out var selected));
        Assert.Equal(0, selected);
        Assert.True(allocation.TryClaim());
        Assert.False(store.TryApplyColor(before, new(1, 2, 3, 4), out _));
        Assert.True(store.TryGetActive(0, out var stillOwned));
        Assert.Equal(before, stillOwned);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Accepted_source_allocation_emits_drop_owner_then_color_for_physical_and_sentinel(bool sentinel)
    {
        var registry = new RuntimeWorldItemReplicationRegistry();
        var trace = new Trace(registry);
        var store = new RuntimeWorldItemStore(trace);
        store.AttachOwnerFactsProvider(new Owner());
        if (sentinel)
            for (short slot = 0; slot < 400; slot++)
                Assert.True(store.TryAllocateDrop(Drop() with { Stack = 9999 }, out _));
        var outbound = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(16, 16384, 1024));
        var source = GameCommandSourceId.FromConnection(9810);
        Assert.True(registry.TryRegister(source, outbound));
        registry.PlayerSpawned(new(source, new(new(0), new(1))), new(new(0), 10, 10, 0, 0, 0, 0, 0));
        trace.Commits.Clear();
        var drop = Drop() with { Color = new(0, 80, 255, 100) };
        using var plan = store.CreateAllocationPreview();
        Assert.True(plan.TrySpawnSource(drop, 0, out var selected));
        Assert.Equal(sentinel ? 400 : 0, selected);
        Assert.True(plan.TryClaim());
        Assert.Equal(0, outbound.QueuedFrames);
        Assert.True(plan.TryCommitNext(out _, out _));
        var first = Read(outbound);
        var second = Read(outbound);
        var third = Read(outbound);
        Assert.Equal(21, first[2]);
        Assert.Equal(22, second[2]);
        Assert.Equal(TerrariaWorldItemColorCodec1458.Encode(selected, drop.Color.Value), third);
        Assert.Equal(0, outbound.QueuedFrames);
        if (!sentinel)
        {
            Assert.True(store.TryGetActive(selected, out var retained));
            Assert.Equal(drop.Color, retained.Color);
            Assert.Equal(new[] { WorldItemStateCommitKind.Drop, WorldItemStateCommitKind.Owner, WorldItemStateCommitKind.Color },
                trace.Commits.Select(commit => commit.Kind));
            Assert.Null(trace.Commits[0].Snapshot.Color);
            Assert.Null(trace.Commits[1].Snapshot.Color);
            Assert.Equal(drop.Color, trace.Commits[2].Snapshot.Color);
            Assert.Equal(trace.Commits[1].Snapshot.Revision.Value + 1, retained.Revision.Value);
        }
        else Assert.False(store.TryGetActive(selected, out _));
    }

    private static WorldItemDropStateUpdate Drop() => new(100, 100, 0, 0, 1, 0, WorldItemOwnershipMode.None, 23, false, 0, 0);
    private sealed class Trace(RuntimeWorldItemReplicationRegistry registry) : IWorldItemStateCommitSink, IWorldItemSentinelCommitSink1458
    {
        public List<(WorldItemStateCommitKind Kind, WorldItemSnapshot Snapshot)> Commits { get; } = [];
        public void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot snapshot)
        {
            Commits.Add((kind, snapshot));
            registry.WorldItemStateCommitted(kind, snapshot);
        }
        public void WorldItemSentinelCommitted(in WorldItemSentinelCommit1458 commit) => registry.WorldItemSentinelCommitted(commit);
    }
    private sealed class Rolls(int seed) : INpcLootRollSource
    {
        public VanillaUnifiedRandom1458 SourceRandom { get; } = new(seed);
        public int RollLuck(int chanceDenominator) => SourceRandom.Next(chanceDenominator);
        public int NextInt32(int inclusiveMin, int exclusiveMax) => SourceRandom.Next(inclusiveMin, exclusiveMax);
    }
    private sealed class Owner : IWorldItemOwnerFactsProvider1458, IWorldItemOwnerFactsSnapshot1458
    {
        public bool IsCurrent => true;
        public IWorldItemOwnerFactsSnapshot1458 Capture() => this;
        public bool TrySelectOwner(in WorldItemDropStateUpdate drop, int grabDelay, byte grabPlayer, out byte owner)
        { owner = 0; return true; }
    }
    private static byte[] Read(TerrariaConnectionOutboundQueue outbound)
    {
        var property = typeof(TerrariaConnectionOutboundQueue).GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var queue = Assert.IsType<BoundedOutboundQueue>(property.GetValue(outbound));
        Assert.True(queue.TryRead(out var frame));
        return frame.Bytes.ToArray();
    }
}
