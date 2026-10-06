using System.Text.Json;
using System.IO.Compression;
using Xunit;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Worlds;

namespace TerraRuntime.Tests;

public sealed class VanillaInvasionLifecycle1458Tests
{
    private static JsonElement[] Rows(string phase)
    {
        using var stream = typeof(VanillaInvasionLifecycle1458Tests).Assembly.GetManifestResourceStream("InvasionLifecycle1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        return document.RootElement.EnumerateArray().Where(row => row.GetProperty("phase").GetString() == phase)
            .Select(row => row.Clone()).ToArray();
    }

    private static int Number(JsonElement row, string name) => row.GetProperty(name).GetInt32();

    private static InvasionState1458 State(JsonElement row) => new(Number(row, "type"), Number(row, "size"),
        Number(row, "sizeStart"), Number(row, "delay"), row.GetProperty("x").GetDouble(), Number(row, "warn"),
        Number(row, "progress"), Number(row, "maximum"), Number(row, "icon"), Number(row, "wave"),
        row.GetProperty("downedGoblin").GetBoolean(), row.GetProperty("downedPirate").GetBoolean(),
        row.GetProperty("downedMartian").GetBoolean(), row.GetProperty("lantern").GetBoolean());

    [Fact]
    public void Original_start_counts_active_high_base_life_players_and_retains_exact_rng_and_sync_progress()
    {
        var rows = Rows("StartInvasion");
        Assert.Equal(18, rows.Length);
        foreach (var row in rows)
        {
            var before = State(row.GetProperty("before"));
            var random = new VanillaUnifiedRandom1458(Number(row, "seed"));
            Assert.True(VanillaInvasionLifecycle1458.TryStart(in before, Number(row, "type"),
                Number(row, "count"), 100, 50, random.Next, out var transition));
            Assert.Equal(State(row.GetProperty("after")), transition.State);
            Assert.Equal(Number(row, "next"), random.Next());
            Assert.Equal(default, transition.Effects); // Source start itself does not send7/78.
            var frames = row.GetProperty("frames").EnumerateArray().ToArray();
            if (transition.State.Type == 0) Assert.Empty(frames);
            else
            {
                byte[] frame = Convert.FromHexString(Assert.Single(frames).GetString()!);
                Assert.Equal(78, frame[2]);
                Assert.Equal(transition.State.SizeStart - transition.State.Size, BitConverter.ToInt32(frame, 3));
                Assert.Equal(transition.State.SizeStart, BitConverter.ToInt32(frame, 7));
                Assert.Equal(transition.State.Type + 3, frame[11]);
                Assert.Equal(0, frame[12]);
            }
        }
    }

    [Fact]
    public void Original_late_world_update_finishes_then_still_moves_and_preserves_warning_order()
    {
        var rows = Rows("UpdateInvasion");
        Assert.Equal(12, rows.Length);
        foreach (var row in rows)
        {
            var before = State(row.GetProperty("before"));
            Assert.True(VanillaInvasionLifecycle1458.TryAdvance(in before, 50, Number(row, "rate"), out var transition));
            Assert.Equal(State(row.GetProperty("after")), transition.State);
            var packets = row.GetProperty("frames").EnumerateArray().Select(x => Convert.FromHexString(x.GetString()!)[2]).ToArray();
            Assert.Equal(packets.Count(x => x == 82), transition.Effects.Warnings);
            Assert.Equal(packets.Contains((byte)7), transition.Effects.WorldInfo);
            Assert.Equal(packets.Contains((byte)98), transition.Effects.ProgressionEvent != 0);
            Assert.Equal(before.Size <= 0 ? before.Type : transition.State.Type, transition.Effects.FirstWarning.Type);
            if (before.Size <= 0)
            {
                Assert.Equal(new byte[] {98, 82, 7, 82, 82}, packets);
                Assert.Equal(1, transition.Effects.SecondWarning.Type);
                Assert.Equal(InvasionWarningPhase1458.Ended, transition.Effects.SecondWarning.Phase);
                Assert.Equal(3600, transition.State.Warning);
            }
        }
    }

    [Fact]
    public void Genuine_check_dead_credits_matching_groups_after_items_with_source_zero_and_large_weights()
    {
        var rows = Rows("NPC.checkDead");
        Assert.Equal(8, rows.Length);
        foreach (var row in rows)
        {
            var before = State(row.GetProperty("before"));
            Assert.True(VanillaInvasionLifecycle1458.TryCreditDeath(in before, Number(row, "type"), out var transition));
            Assert.Equal(State(row.GetProperty("after")), transition.State);
            var packets = row.GetProperty("frames").EnumerateArray().Select(x => Convert.FromHexString(x.GetString()!)[2]).ToArray();
            Assert.Equal(packets.Contains((byte)78), transition.Effects.Progress);
            if (transition.Effects.Progress) Assert.Equal(78, packets[^1]);
            var mismatch = before with { Type = before.Type == 1 ? 3 : 1 };
            Assert.True(VanillaInvasionLifecycle1458.TryCreditDeath(in mismatch, Number(row, "type"), out var ignored));
            Assert.Equal(mismatch, ignored.State);
            Assert.Equal(default, ignored.Effects);
        }
    }

    [Fact]
    public void Legacy_recovery_matches_original_and_current326_does_not_invent_recovery()
    {
        var rows = Rows("FakeLoadInvasionStart");
        Assert.Equal(12, rows.Length);
        foreach (var row in rows)
        {
            Assert.True(VanillaInvasionLifecycle1458.TryRecoverLegacySizeStart(Number(row, "type"), Number(row, "size"), out int size));
            Assert.Equal(Number(row.GetProperty("after"), "sizeStart"), size);
        }
        Assert.True(VanillaInvasionLifecycle1458.TryRecoverLegacySizeStart(2, 121, out int frost));
        Assert.Equal(160, frost);
        Assert.False(VanillaInvasionLifecycle1458.CanOwnLive(new(Type: 2, Size: 121, SizeStart: frost,
            Delay: 0, X: 1, Warning: 0, Progress: 0, ProgressMax: 0, ProgressIcon: 0,
            ProgressWave: 0, DownedGoblin: false, DownedPirate: false, DownedMartian: false, LanternNextNight: false)));
    }

    [Fact]
    public void Unsupported_metadata_and_arithmetic_reject_before_draw_while_existing_start_and_repeat_clear_remain_source_exact()
    {
        var state = State(Rows("StartInvasion")[1].GetProperty("after"));
        int draws = 0;
        int Next(int min, int max) { draws++; return min; }
        Assert.False(VanillaInvasionLifecycle1458.TryStart(state with { X = double.NaN }, 1, 1, 100, 50, Next, out _));
        Assert.False(VanillaInvasionLifecycle1458.TryStart(in state, 4, 256, 100, 50, Next, out _));
        Assert.True(VanillaInvasionLifecycle1458.TryStart(in state, 3, 1, 100, 50, Next, out var blocked));
        Assert.Equal(state, blocked.State);
        Assert.Equal(0, draws);
        var delayed = state with { Type = 0, Delay = 1 };
        Assert.False(VanillaInvasionLifecycle1458.CanStart(in delayed, 1));
        Assert.True(VanillaInvasionLifecycle1458.CanStart(in delayed, 1, ignoreDelay: true));
        var exhausted = state with { Size = 0, X = 50, DownedGoblin = true, LanternNextNight = false };
        Assert.True(VanillaInvasionLifecycle1458.TryAdvance(in exhausted, 50, 1, out var ended));
        Assert.False(ended.State.LanternNextNight);
        Assert.Equal(1, ended.Effects.Warnings);
        Assert.Equal(10, ended.Effects.ProgressionEvent);
        Assert.False(VanillaInvasionLifecycle1458.TryAdvance(state with { X = 99, Warning = int.MinValue }, 50, 1, out _));
        Assert.False(VanillaInvasionLifecycle1458.TryCreditDeath(state with { Size = int.MinValue }, 27, out _));
        Assert.False(VanillaInvasionLifecycle1458.TryRecoverLegacySizeStart(4, int.MaxValue, out _));
    }
}
