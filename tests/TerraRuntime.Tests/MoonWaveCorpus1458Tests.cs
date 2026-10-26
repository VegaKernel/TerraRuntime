using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class MoonWaveCorpus1458Tests
{
    [Fact]
    public void All_original_transition_point_and_float_rows_match_clock_and_actual_publication_bytes()
    {
        using var reference = Load();
        var rows = reference.RootElement.GetProperty("rows").EnumerateArray().ToArray();
        Assert.Equal(243, rows.Length);
        Assert.Equal(114, rows.Count(row => row.GetProperty("profile").GetString() == "transition"));
        var prior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            int packets = 0;
            foreach (var row in rows)
            {
                bool frost = row.GetProperty("frost").GetBoolean();
                bool active = row.GetProperty("active").GetBoolean();
                int mode = row.GetProperty("mode").GetInt32();
                int type = row.GetProperty("type").GetInt32();
                var clock = new RuntimeWorldClock(0, false, VanillaMoonPhase.Full, 0, 0,
                    pumpkinMoon: !frost && active, snowMoon: frost && active);
                Set(clock, nameof(clock.MoonEventWaveNumber), row.GetProperty("wave").GetInt32());
                Set(clock, nameof(clock.MoonEventWaveKills), row.GetProperty("kills").GetSingle());
                Set(clock, nameof(clock.MoonEventTotalInvasionPoints), row.GetProperty("total").GetSingle());
                var replication = new RuntimeNpcReplicationRegistry();
                replication.BindMoonEventClock(clock);
                var queue = Register(replication);
                for (int repeat = 0; repeat < row.GetProperty("repeats").GetInt32(); repeat++)
                {
                    bool accepted = clock.TryAdvanceMoonEventDeath(new(type), mode != 0, mode == 2, out var effects);
                    Assert.Equal(active && type != 1, accepted);
                    if (accepted)
                    {
                        Assert.True(effects.IsCurrent(clock));
                        Assert.Equal(clock.MoonEventProgressReportRequired, effects.ShouldReportProgress);
                        replication.PublishMoonEventDeathEffects(in effects);
                    }
                    else Assert.Equal(default(MoonEventDeathEffects1458), effects);
                }
                var after = row.GetProperty("after");
                Assert.Equal(after.GetProperty("wave").GetInt32(), clock.MoonEventWaveNumber);
                Assert.Equal(after.GetProperty("kills").GetSingle(), clock.MoonEventWaveKills);
                Assert.Equal(after.GetProperty("total").GetSingle(), clock.MoonEventTotalInvasionPoints);
                byte[][] expected = row.GetProperty("frames").EnumerateArray()
                    .Select(value => Convert.FromHexString(value.GetString()!)).ToArray();
                byte[][] actual = Drain(queue);
                Assert.Equal(expected.Length, actual.Length);
                for (int index = 0; index < actual.Length; index++) Assert.Equal(expected[index], actual[index]);
                packets += actual.Length;
                Assert.Equal(clock.MoonEventProgressRevision, replication.HandledMoonEventProgressRevision);
            }
            Assert.Equal(355, packets);
        }
        finally { CultureInfo.CurrentCulture = prior; }
    }

    [Fact]
    public void Stale_outer_wave_fifteen_drains_earned_achievement_after_newer_progress_without_rewinding_text_or_progress()
    {
        foreach (bool snow in new[] { false, true })
        {
            var clock = new RuntimeWorldClock(0, false, VanillaMoonPhase.Full, 0, 0,
                pumpkinMoon: !snow, snowMoon: snow);
            Set(clock, nameof(clock.MoonEventWaveNumber), 14);
            Set(clock, nameof(clock.MoonEventWaveKills), 674f);
            var replication = new RuntimeNpcReplicationRegistry();
            replication.BindMoonEventClock(clock);
            var queue = Register(replication);
            Assert.True(clock.TryAdvanceMoonEventDeath(new(snow ? 338 : 305), false, false, out var outer));
            Assert.True(outer.WaveAdvanced);
            Assert.Equal(15, outer.Wave);
            Assert.True(clock.TryAdvanceMoonEventDeath(new(snow ? 338 : 305), false, false, out var nested));
            replication.PublishMoonEventDeathEffects(in nested);
            replication.PublishMoonEventDeathEffects(in outer);
            var frames = Drain(queue);
            Assert.Equal(new byte[] { 78, 98 }, frames.Select(frame => frame[2]).ToArray());
            Assert.Equal(snow ? "0500620E00" : "0500620F00", Convert.ToHexString(frames[1]));
            Assert.Equal(nested.ProgressRevision, replication.HandledMoonEventProgressRevision);
            Assert.Equal(1f, clock.MoonEventWaveKills);
        }
    }

    [Fact]
    public void Preview_adoption_retains_source_no_report_predicate_and_dawn_capture()
    {
        var clock = new RuntimeWorldClock(RuntimeWorldClock.NightLength, false, VanillaMoonPhase.Full, 0, 1);
        Assert.True(clock.TryStartPumpkinMoon());
        Assert.Equal(2, clock.ConsumeMoonEventStartSyncRequest());
        var preview = clock.CreateDeathPreview();
        Assert.True(preview.TryAdvanceMoonEventDeath(new(327), false, false, out var effects));
        Assert.True(effects.WaveAdvanced);
        Assert.False(effects.ShouldReportProgress);
        Assert.Equal(1, clock.MoonEventWaveNumber);
        clock.AdoptDeathProgress(preview);
        Assert.Equal(2, clock.MoonEventWaveNumber);
        Assert.False(clock.MoonEventProgressReportRequired);
        clock.Tick();
        var dawn = Assert.IsType<MoonEventDawnNotice1458>(clock.ConsumeMoonEventDawnNotice());
        Assert.Equal(2, dawn.Wave);
        Assert.Equal(150f, dawn.TotalPoints);
        Assert.False(dawn.ShouldReportProgress);
        Assert.False(dawn.HadUnpublishedStart);
    }

    [Fact]
    public void Dual_flag_component_credits_the_actual_participant_family_and_keeps_snow_score_priority()
    {
        var clock = new RuntimeWorldClock(RuntimeWorldClock.NightLength, false, VanillaMoonPhase.Full, 0, 1,
            pumpkinMoon: true, snowMoon: true);
        Set(clock, nameof(clock.MoonEventWaveNumber), 14);
        Set(clock, nameof(clock.MoonEventWaveKills), 674f);
        var replication = new RuntimeNpcReplicationRegistry();
        replication.BindMoonEventClock(clock);
        var queue = Register(replication);
        Assert.True(clock.TryAdvanceMoonEventDeath(new(305), false, false, out var pumpkin));
        Assert.Equal(2, pumpkin.MoonIcon);
        Assert.Equal(15, pumpkin.EarnedProgressionEvent);
        Assert.True(pumpkin.IsCurrent(clock));
        replication.PublishMoonEventDeathEffects(in pumpkin);
        var frames = Drain(queue);
        Assert.Equal(new byte[] { 82, 98, 78 }, frames.Select(frame => frame[2]).ToArray());
        Assert.Equal(TerrariaMoonEventAnnouncementCodec1458.EncodeWave(false, 15), frames[0]);
        Assert.Equal("0500620F00", Convert.ToHexString(frames[1]));
        Assert.Equal(2, frames[2][11]);
        Assert.True(clock.TryAdvanceMoonEventDeath(new(338), false, false, out var snow));
        Assert.Equal(1, snow.MoonIcon);
        Assert.Equal(0, snow.EarnedProgressionEvent);
        replication.PublishMoonEventDeathEffects(in snow);
        Assert.Equal(1, Assert.Single(Drain(queue))[11]);
        clock.Tick();
        var dawn = Assert.IsType<MoonEventDawnNotice1458>(clock.ConsumeMoonEventDawnNotice());
        Assert.Equal(1, dawn.MoonIcon);
        Assert.Equal(1, dawn.ProgressIcon);
    }

    [Fact]
    public void Wave_codec_refuses_out_of_range_source_waves()
    {
        foreach (bool snow in new[] { false, true })
        foreach (int wave in new[] { int.MinValue, -1, 0, 21, int.MaxValue })
            Assert.Throws<ArgumentOutOfRangeException>(() => TerrariaMoonEventAnnouncementCodec1458.EncodeWave(snow, wave));
    }

    private static JsonDocument Load()
    {
        using var resource = typeof(MoonWaveCorpus1458Tests).Assembly.GetManifestResourceStream("MoonWave1458");
        Assert.NotNull(resource);
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var bytes = new MemoryStream();
        gzip.CopyTo(bytes);
        Assert.Equal("F88BC4D75EB40D525C62360F833BA42F3C30E86141BBC127319730B1D5CE3124",
            Convert.ToHexString(SHA256.HashData(bytes.ToArray())));
        return JsonDocument.Parse(bytes.ToArray());
    }

    private static void Set(RuntimeWorldClock clock, string property, object value) =>
        typeof(RuntimeWorldClock).GetProperty(property)!.SetValue(clock, value);

    private static BoundedOutboundQueue Register(RuntimeNpcReplicationRegistry replication)
    {
        var outbound = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(32, 32768, 4096));
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(1489), new(new(0), new(1)));
        Assert.True(replication.TryRegister(connection.Source, outbound));
        replication.PlayerSpawned(connection, new(connection.Player.Slot, 10, 10, 0, 0, 0, 0, 0));
        return Assert.IsType<BoundedOutboundQueue>(typeof(TerrariaConnectionOutboundQueue)
            .GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(outbound));
    }

    private static byte[][] Drain(BoundedOutboundQueue queue)
    {
        var frames = new List<byte[]>();
        while (queue.TryRead(out var frame)) frames.Add(frame.Bytes.ToArray());
        return frames.ToArray();
    }
}
