using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class EventClockLifecycle1458Tests
{
    [Fact]
    public void Paid_blood_moon_and_tablet_transitions_reject_repeats_and_wrong_time()
    {
        // TerrariaServer 1.4.5.8 MessageBuffer case 61: -10 replaces empty moon with phase 5;
        // -6 starts only during daytime. The caller, not this scalar owner, pays for either request.
        var night = Clock(false, moon: 4);
        Assert.False(night.TryStartSolarEclipse());
        Assert.True(night.TryStartBloodMoon());
        Assert.True(night.BloodMoonActive);
        Assert.Equal((VanillaMoonPhase)5, night.MoonPhase);
        Assert.True(night.ConsumeWorldInfoSyncRequest());
        Assert.False(night.TryStartBloodMoon());
        Assert.False(night.ConsumeWorldInfoSyncRequest());

        var day = Clock(true);
        Assert.False(day.TryStartBloodMoon());
        Assert.True(day.TryStartSolarEclipse());
        Assert.True(day.EclipseActive);
        Assert.True(day.ConsumeWorldInfoSyncRequest());
        Assert.False(day.TryStartSolarEclipse());
        Assert.False(day.ConsumeWorldInfoSyncRequest());
    }

    [Fact]
    public void Moon_starts_clear_blood_moon_and_publish_one_initial_progress_revision()
    {
        // Main.startPumpkinMoon/startSnowMoon both clear bloodMoon before initializing shared wave state.
        foreach (bool pumpkin in new[] { false, true })
        {
            var clock = Clock(false, bloodMoon: true);
            Assert.True(pumpkin ? clock.TryStartPumpkinMoon() : clock.TryStartSnowMoon());
            Assert.False(clock.BloodMoonActive);
            Assert.Equal(pumpkin, clock.PumpkinMoonActive);
            Assert.Equal(!pumpkin, clock.SnowMoonActive);
            Assert.Equal(1, clock.MoonEventWaveNumber);
            Assert.Equal(0f, clock.MoonEventWaveKills);
            Assert.Equal(0f, clock.MoonEventTotalInvasionPoints);
            Assert.Equal(1, clock.MoonEventProgressRevision);
            Assert.True(clock.ConsumeWorldInfoSyncRequest());
            Assert.False(clock.TryStartPumpkinMoon());
            Assert.False(clock.TryStartSnowMoon());
            Assert.Equal(1, clock.MoonEventProgressRevision);
            Assert.False(clock.ConsumeWorldInfoSyncRequest());
        }
        var daytime = Clock(true);
        Assert.False(daytime.TryStartPumpkinMoon());
        Assert.False(daytime.TryStartSnowMoon());
        Assert.Equal(0, daytime.MoonEventProgressRevision);
    }

    [Fact]
    public void Eclipse_ends_at_dusk_and_moons_end_at_dawn_using_strict_source_thresholds()
    {
        var day = Clock(true, time: RuntimeWorldClock.DayLength - 1d);
        Assert.True(day.TryStartSolarEclipse());
        day.Tick();
        Assert.True(day.DayTime);
        Assert.True(day.EclipseActive);
        day.Tick();
        Assert.False(day.DayTime);
        Assert.False(day.EclipseActive);

        var night = Clock(false, time: RuntimeWorldClock.NightLength - 1d);
        Assert.True(night.TryStartSnowMoon());
        Assert.True(night.TryAdvanceMoonEventDeath(new(344), false, false));
        night.Tick();
        Assert.True(night.SnowMoonActive);
        night.Tick();
        Assert.True(night.DayTime);
        Assert.False(night.MoonEventActive);
        Assert.Equal(0, night.MoonEventWaveNumber);
        Assert.Equal(0f, night.MoonEventWaveKills);
        Assert.Equal(0f, night.MoonEventTotalInvasionPoints);
    }

    [Fact]
    public void Daytime_cleanup_still_runs_with_frozen_world_time()
    {
        // Main.UpdateTime's daytime branch clears these events independently of dayRate.
        var clock = new RuntimeWorldClock(100d, true, default, 0d, 0,
            pumpkinMoon: true, snowMoon: true, freezeRain: true, freezeWind: true);
        clock.Tick();
        Assert.Equal(100d, clock.Time);
        Assert.False(clock.MoonEventActive);
        Assert.Equal(0, clock.MoonEventWaveNumber);
        Assert.True(clock.ConsumeWorldInfoSyncRequest());

        var staleEclipse = new RuntimeWorldClock(100d, false, default, 0d, 0,
            eclipse: true, freezeRain: true, freezeWind: true);
        staleEclipse.Tick();
        Assert.False(staleEclipse.EclipseActive);
        Assert.True(staleEclipse.ConsumeWorldInfoSyncRequest());
        staleEclipse.Tick();
        Assert.False(staleEclipse.ConsumeWorldInfoSyncRequest());

        var dusk = new RuntimeWorldClock(RuntimeWorldClock.DayLength, true, default, 0d, 1,
            pumpkinMoon: true, freezeRain: true, freezeWind: true);
        dusk.Tick();
        Assert.False(dusk.DayTime);
        Assert.False(dusk.MoonEventActive);
        Assert.Equal(0, dusk.MoonEventWaveNumber);
    }

    [Fact]
    public void Packet_7_overrides_loaded_eclipse_in_both_directions()
    {
        var header = new WorldFileHeader("event", "seed", 1, Guid.Empty, 1, 0, 320, 0, 320, new(20, 20));
        foreach (bool active in new[] { false, true })
        {
            var metadata = new WorldFileRuntimeMetadata { DayTime = true, Eclipse = !active };
            var live = new WorldInfoRuntimeState(0, true, 0, false, false) { Eclipse = active };
            var packet = WorldInfoPacketMapper.Create(header, metadata, runtime: live);
            Assert.Equal(active ? (byte)5 : (byte)1, packet.DayandMoonInfo);
            Assert.Equal(!active ? (byte)5 : (byte)1,
                WorldInfoPacketMapper.Create(header, metadata).DayandMoonInfo);
        }
    }

    [Fact]
    public void Independent_official_header_preserves_all_other_bytes_when_event_flags_change()
    {
        // This tracked fixture contains actual WorldFile.SaveWorldHeader output from the official server.
        using var raw = typeof(EventClockLifecycle1458Tests).Assembly.GetManifestResourceStream("InvasionPersistence1458");
        using var gzip = new GZipStream(Assert.IsAssignableFrom<Stream>(raw), CompressionMode.Decompress);
        using var reference = JsonDocument.Parse(gzip);
        byte[] original = Convert.FromBase64String(reference.RootElement.GetProperty("saves")[0]
            .GetProperty("headerBase64").GetString()!);
        var envelope = new WorldFileEnvelope(326, 0, 0, [0, original.Length], 0, []);
        Assert.Equal(WorldFileHeaderParseResult.Parsed,
            WorldFileHeaderParser.TryParse(original, envelope, out var header));
        var metadata = Parse(original, envelope, header!);
        foreach (bool active in new[] { true, false })
        {
            Assert.Equal(WorldFileClockHeaderPatchResult.Patched,
                WorldFileClockHeaderPatcher.TryPatch(original, header!, metadata.Time, metadata.DayTime,
                    metadata.MoonPhase, metadata.SlimeRainTime, metadata.WindSpeed, metadata.Raining,
                    metadata.RainTime, metadata.MaxRain, out var changed, bloodMoon: active, eclipse: active));
            var saved = Parse(changed, envelope, header!);
            Assert.Equal(active, saved.BloodMoon);
            Assert.Equal(active, saved.Eclipse);
            int changedBytes = original.Zip(changed).Count(pair => pair.First != pair.Second);
            Assert.Equal((metadata.BloodMoon == active ? 0 : 1) + (metadata.Eclipse == active ? 0 : 1), changedBytes);
        }
    }

    [Fact]
    public async Task Detached_checkpoint_keeps_event_flags_and_cold_reload_discards_moon_wave_state()
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        byte[] bytes = (byte[])typeof(WorldFileLoaderTests).GetMethod("CreateCompleteCurrentWorld", flags)!.Invoke(null, null)!;
        var limits = (WorldFileLoadLimits)typeof(WorldFileLoaderTests).GetMethod("CreateLimits", flags)!.Invoke(null, null)!;
        Assert.True(WorldFileLoader.TryLoad(bytes, limits, out var world).IsLoaded);
        Assert.True(WorldFilePreservedSections.TryCapture(bytes, world!.Envelope, out var preserved));

        foreach (bool active in new[] { true, false })
        {
            var clock = Clock(false, bloodMoon: active, eclipse: active);
            var source = new RuntimeWorldCheckpointSnapshotSource(world.Tiles, new RuntimeChestStore(world.Chests), 8,
                worldClock: clock);
            source.CaptureTileBootstrap(8);
            Assert.True(source.TryCapture(out var snapshot));
            Assert.Equal(active, snapshot!.Clock!.Value.BloodMoon);
            Assert.Equal(active, snapshot.Clock.Value.Eclipse);
            Assert.True(clock.TryStartSnowMoon());
            Assert.True(clock.TryAdvanceMoonEventDeath(new(344), false, false));
            Assert.False(clock.BloodMoonActive);

            using var stream = new MemoryStream();
            var serialize = typeof(RuntimeWorldCheckpointCoordinator).GetMethod("SerializeAsync", flags)!;
            await (Task)serialize.Invoke(null, [world.Envelope, world.Header, preserved!, snapshot, stream, CancellationToken.None])!;
            Assert.True(WorldFileLoader.TryLoad(stream.ToArray(), limits, out var saved).IsLoaded);
            Assert.Equal(active, saved!.RuntimeMetadata.BloodMoon);
            Assert.Equal(active, saved.RuntimeMetadata.Eclipse);
            var restarted = RuntimeWorldClock.FromWorld(saved.RuntimeMetadata, saved.CreativePowers);
            Assert.Equal(active, restarted.BloodMoonActive);
            Assert.Equal(active, restarted.EclipseActive);
            Assert.False(restarted.MoonEventActive);
            Assert.Equal(0, restarted.MoonEventWaveNumber);
            Assert.Equal(0f, restarted.MoonEventTotalInvasionPoints);
        }
    }

    private static RuntimeWorldClock Clock(bool dayTime, byte moon = 0, bool bloodMoon = false,
        bool eclipse = false, double time = 100d) =>
        new(time, dayTime, (VanillaMoonPhase)moon, 0d, 1, bloodMoonActive: bloodMoon,
            eclipse: eclipse, freezeRain: true, freezeWind: true);

    private static WorldFileRuntimeMetadata Parse(byte[] bytes, WorldFileEnvelope envelope, WorldFileHeader header)
    {
        Assert.Equal(WorldFileRuntimeMetadataParseResult.Parsed,
            WorldFileRuntimeMetadataParser.TryParse(bytes, envelope, header,
                new(16384, 16 * 1024 * 1024, 255, 762, 255, 16 * 1024 * 1024), out var metadata, out _));
        return metadata!;
    }
}
