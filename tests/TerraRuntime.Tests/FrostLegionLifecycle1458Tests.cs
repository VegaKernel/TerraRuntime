using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Worlds;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class FrostLegionLifecycle1458Tests
{
    private static InvasionState1458 Idle => new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        false, false, false, false);

    [Fact]
    public void Frost_start_and_completion_follow_source_shared_lifecycle_and_first_victory_lantern()
    {
        // Main.StartInvasion/UpdateInvasion 1.4.5.8: Frost uses Goblin size/coast rules,
        // but its own downed flag and progression event 12.
        int draws = 0;
        int Next(int min, int max) { Assert.Equal(0, min); Assert.Equal(2, max); draws++; return 1; }
        Assert.True(VanillaInvasionLifecycle1458.TryStart(Idle, 2, 2, 100, 50, Next, out var start));
        Assert.Equal(1, draws);
        Assert.Equal(2, start.State.Type);
        Assert.Equal(160, start.State.Size);
        Assert.Equal(160, start.State.SizeStart);
        Assert.Equal(5, start.State.ProgressIcon);
        Assert.Equal(100d, start.State.X);
        Assert.Equal(default, start.Effects);
        Assert.True(VanillaInvasionLifecycle1458.TryAdvance(start.State, 50, 2, out var approach));
        Assert.Equal(98d, approach.State.X);
        Assert.Equal(new InvasionWarning1458(2, InvasionWarningPhase1458.East), approach.Effects.FirstWarning);
        var dead = start.State with { Size = 0, X = 50 };
        Assert.True(VanillaInvasionLifecycle1458.TryAdvance(in dead, 50, 1, out var completed));
        Assert.Equal(0, completed.State.Type);
        Assert.True(completed.State.DownedFrost);
        Assert.False(completed.State.DownedGoblin);
        Assert.False(completed.State.DownedPirate);
        Assert.False(completed.State.DownedMartian);
        Assert.True(completed.State.LanternNextNight);
        Assert.Equal(12, completed.Effects.ProgressionEvent);
        Assert.True(completed.Effects.WorldInfo);
        Assert.Equal(new InvasionWarning1458(2, InvasionWarningPhase1458.Ended), completed.Effects.FirstWarning);
        Assert.True(VanillaInvasionLifecycle1458.TryAdvance(dead with { DownedFrost = true }, 50, 1, out var repeat));
        Assert.False(repeat.State.LanternNextNight);
        Assert.True(VanillaInvasionLifecycle1458.TryAdvance(dead with { DownedFrost = true, LanternNextNight = true },
            50, 1, out var pending));
        Assert.True(pending.State.LanternNextNight);
    }

    [Fact]
    public void Frost_credit_uses_existing_exact_dead_generation_owner_and_refuses_reentry()
    {
        // NPC.GetNPCInvasionGroup/checkDead: the three Frost participants each contribute one.
        foreach (int type in new[] { 143, 144, 145 })
        {
            var store = new RuntimeNpcStore(2);
            var update = new NpcStateUpdate(type, checked((short)type), 800, 440, 0, 0, 0, default,
                NpcSimulationState.Initial with { Life = 0, LifeMax = 100 });
            Assert.True(store.TrySpawn(0, in update, out var dead));
            var owner = new RuntimeWorldInvasion1458(Idle with { Type = 2, Size = 1, SizeStart = 120 });
            Assert.True(RuntimeNpcInvasionDeathCredit1458.TryPrepare(owner, store, in dead, out var first));
            Assert.True(RuntimeNpcInvasionDeathCredit1458.TryPrepare(owner, store, in dead, out var duplicate));
            Assert.True(first!.TryAdoptAfterLoot(out var accepted, out var terminal));
            Assert.Equal(0, accepted.State.Size);
            Assert.Equal(120, accepted.State.Progress);
            Assert.Equal(5, accepted.State.ProgressIcon);
            Assert.False(store.TryGet(dead.Handle, out _));
            Assert.False(duplicate!.TryAdoptAfterLoot(out _, out _));
            Assert.True(store.TrySpawn(0, in update, out var replacement));
            Assert.True(terminal!.TryPublish());
            Assert.False(first.TryAdoptAfterLoot(out _, out _));
            Assert.True(store.TryGet(replacement.Handle, out _));

            Assert.True(RuntimeNpcInvasionDeathCredit1458.TryPrepare(owner, store, in replacement, out var stale));
            Assert.True(store.TryUpdate(replacement.Handle, in update, out _));
            Assert.False(stale!.TryAdoptAfterLoot(out _, out _));
            Assert.True(owner.TryCapture(out var current));
            Assert.Equal(0, current.State.Size);
        }
    }

    [Fact]
    public void Frost_journal_changes_only_source_header_flag_survives_reload_and_reaches_packet7()
    {
        // Independent official SaveWorldHeader Goblin pair establishes the existing flag byte.
        // Source 1.4.5.8 places Clown then Frost immediately after it; no decompiled bytes are shipped.
        using var stream = typeof(FrostLegionLifecycle1458Tests).Assembly.GetManifestResourceStream("InvasionPersistence1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reference = JsonDocument.Parse(gzip);
        byte[] Header(string profile) => Convert.FromBase64String(reference.RootElement.GetProperty("progressions")
            .EnumerateArray().Single(row => row.GetProperty("profile").GetString() == profile)
            .GetProperty("headerBase64").GetString()!);
        byte[] baseline = Header("none"), goblin = Header("goblin");
        using var bytes = new MemoryStream(baseline);
        using var reader = new BinaryReader(bytes);
        _ = reader.ReadString(); _ = reader.ReadString();
        bytes.Position += 8 + 16 + 4 + 16 + 8 + 4 + 9 + 8;
        baseline.AsSpan((int)bytes.Position, 8).CopyTo(goblin.AsSpan((int)bytes.Position, 8));
        int goblinOffset = Assert.Single(Enumerable.Range(0, baseline.Length), i => baseline[i] != goblin[i]);
        var envelope = new WorldFileEnvelope(326, 0, 0, [0, baseline.Length], 0, []);
        Assert.Equal(WorldFileHeaderParseResult.Parsed, WorldFileHeaderParser.TryParse(baseline, envelope, out var header));
        var mutation = new RuntimeWorldProgressionMutationSnapshot(1UL << (int)VanillaWorldProgressionId.FrostLegion);
        Assert.Equal(WorldFileProgressionHeaderPatchResult.Patched,
            WorldFileProgressionHeaderPatcher.TryPatch(baseline, header!, in mutation, out var patched));
        Assert.Equal(goblinOffset + 2, Assert.Single(Enumerable.Range(0, baseline.Length), i => baseline[i] != patched[i]));
        var limits = new WorldFileRuntimeMetadataLimits(16384, 16 * 1024 * 1024, 255, 762, 255, 16 * 1024 * 1024);
        Assert.Equal(WorldFileRuntimeMetadataParseResult.Parsed,
            WorldFileRuntimeMetadataParser.TryParse(patched, envelope, header!, limits, out var loaded, out _));
        Assert.True(loaded!.DownedFrost);
        Assert.False(loaded.DownedGoblins);
        Assert.False(loaded.DownedClown);
        Assert.False(loaded.DownedPirates);
        Assert.True(VanillaWorldStateProjection.GetProgression(loaded).IsComplete(VanillaWorldProgressionId.FrostLegion));
        var persisted = WorldInfoPacketMapper.Create(header!, loaded);
        var live = new WorldInfoRuntimeState(0, true, 0, false, false) { ProgressionMutations = mutation };
        var fresh = WorldInfoPacketMapper.Create(header!, new WorldFileRuntimeMetadata(), runtime: live);
        Assert.Equal(2, persisted.EventInfo5 & 2);
        Assert.Equal(persisted.EventInfo5 & 2, fresh.EventInfo5 & 2);
        Assert.Equal(new byte[] { 5, 0, 98, 12, 0 }, TerrariaProgressionEventCodec1458.Encode(12));
    }

    [Fact]
    public void Loaded_frost_keeps_stored_remaining_and_start_counts_and_repeat_flag()
    {
        var metadata = new WorldFileRuntimeMetadata { InvasionType = 2, InvasionDelay = 7,
            InvasionSize = 17, InvasionSizeStart = 211, InvasionX = 12.75, DownedFrost = true,
            LanternNightNextNight = true };
        var owner = RuntimeWorldInvasion1458.FromMetadata(metadata, 100);
        Assert.True(owner.TryCapture(out var before));
        Assert.True(before.State.DownedFrost);
        Assert.Equal(new WorldInvasionSaveState1458(7, 17, 2, 12.75, 211, true), owner.CaptureSaveState());
        var complete = new InvasionTransition1458(before.State with { Size = 0 }, default);
        Assert.True(owner.TryAdopt(in before, in complete, out var exhausted));
        Assert.True(VanillaInvasionLifecycle1458.TryAdvance(exhausted.State, 50, 1, out var ended));
        Assert.True(ended.State.DownedFrost);
        Assert.True(ended.State.LanternNextNight);
    }
}
