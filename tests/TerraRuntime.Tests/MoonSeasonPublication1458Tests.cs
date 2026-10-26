using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class MoonSeasonPublication1458Tests
{
    [Fact]
    public void Invocation_world_colors_and_first_wave_nested_keys_follow_source_order()
    {
        foreach (var (action, remix, key) in new[]
            { ((short)-4, false, "LegacyMisc.31"), ((short)-5, false, "LegacyMisc.34"),
              ((short)-6, false, "LegacyMisc.20"), ((short)-6, true, "LegacyMisc.106"),
              ((short)-10, false, "LegacyMisc.8") })
            Assert.Equal(new Message(key, null, 50, 255, 130),
                Read(TerrariaMoonEventAnnouncementCodec1458.EncodeInvocation(action, remix)));
        foreach (bool snow in new[] { false, true })
        {
            using var reader = new BinaryReader(new MemoryStream(TerrariaMoonEventAnnouncementCodec1458.EncodeFirstWave(snow)));
            Assert.Equal(reader.BaseStream.Length, reader.ReadUInt16());
            Assert.Equal(82, reader.ReadByte()); Assert.Equal(1, reader.ReadUInt16()); Assert.Equal(255, reader.ReadByte());
            Assert.Equal(2, reader.ReadByte());
            Assert.Equal(snow ? "Game.InvasionWave_Type2" : "Game.InvasionWave_Type1", reader.ReadString());
            Assert.Equal(snow ? 3 : 2, reader.ReadByte());
            string[] keys = snow ? ["Game.FirstWave", "NPCName.ZombieElf", "NPCName.GingerbreadMan"]
                : ["Game.FirstWave", "NPCName.Scarecrow1"];
            foreach (string key in keys)
            {
                Assert.Equal(2, reader.ReadByte()); Assert.Equal(key, reader.ReadString()); Assert.Equal(0, reader.ReadByte());
            }
            Assert.Equal(new byte[] { 175, 75, 255 }, reader.ReadBytes(3));
            Assert.Equal(reader.BaseStream.Length, reader.BaseStream.Position);
        }
    }

    [Fact]
    public void Score_uses_a_literal_float_substitution_and_season_changes_use_world_color()
    {
        // Independent byte reader follows source NetTextModule.SerializeServerMessage and
        // NetworkText.Serialize, without decoding through the production packet library.
        var prior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal(new Message("Misc.PumpkinMoonScore", "12.5", 175, 75, 255),
                Read(TerrariaMoonEventAnnouncementCodec1458.EncodeScore(false, 12.5f)));
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            Assert.Equal(new Message("Misc.FrostMoonScore", "12,5", 175, 75, 255),
                Read(TerrariaMoonEventAnnouncementCodec1458.EncodeScore(true, 12.5f)));
        }
        finally { CultureInfo.CurrentCulture = prior; }
        foreach (var (christmas, active, key) in new[]
                 {
                     (false, true, "Misc.StartedVictoryHalloween"), (false, false, "Misc.EndedVictoryHalloween"),
                     (true, true, "Misc.StartedVictoryXmas"), (true, false, "Misc.EndedVictoryXmas")
                 })
            Assert.Equal(new Message(key, null, 50, 255, 130),
                Read(TerrariaMoonEventAnnouncementCodec1458.EncodeSeasonChange(christmas, active)));
        foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity })
            Assert.Throws<ArgumentOutOfRangeException>(() => TerrariaMoonEventAnnouncementCodec1458.EncodeScore(true, invalid));
    }

    [Fact]
    public async Task Live_dawn_adopts_season_baseline_then_publishes_score_halloween_xmas_and_world_info()
    {
        foreach (bool victory in new[] { false, true })
        {
            var source = new SandboxWorldSource.Generated(FlatProvider.GeneratorId, "Moon dawn", 1458,
                32, 24, WorldGenerationOptions.Default);
            var materializer = new SandboxWorldMaterializer(BuiltInWorldGeneratorSource.Instance,
                ServerWorldLoadPolicy.CreateLimits());
            var materialized = materializer.Materialize(source, CancellationToken.None);
            Assert.True(materialized.Succeeded, materialized.Error);
            var loaded = materialized.World! with
            {
                RuntimeMetadata = new WorldFileRuntimeMetadata
                {
                    SpawnX = 10, SpawnY = 10, WorldSurface = 12, RockLayer = 16,
                    Time = (int)RuntimeWorldClock.NightLength, DayTime = false,
                    GameMode = 2,
                    ForceHalloweenForToday = !victory, ForceXMasForToday = !victory,
                    ForceHalloweenForever = !victory, ForceXMasForever = !victory
                }
            };
            using var world = new WorldRuntime(
                new WorldRuntimeIdentity(WorldRuntimeId.CreateNew(), WorldSessionId.CreateNew()), source,
                loaded, materialized.Bootstrap!, new InterestManagementControl(), new WorldRuntimeOptions { MaxPlayers = 2 });
            world.WorldClock.SetDayRate(1);
            Assert.True(world.WorldClock.TryStartSnowMoon());
            Assert.True(world.WorldClock.TryAdvanceMoonEventDeath(new(338), true, true));
            if (victory)
            {
                int kills = 0;
                while (world.WorldClock.MoonEventWaveNumber < 15)
                {
                    Assert.True(kills++ < 100);
                    Assert.True(world.WorldClock.TryAdvanceMoonEventDeath(new(345), true, true));
                }
                // Source thresholds1..14: eleven375-point kills, then three pairs; first Elf adds2.5.
                Assert.Equal(17, kills);
                Assert.Equal(6377.5f, world.WorldClock.MoonEventTotalInvasionPoints);
            }
            var oldSnapshot = world.CaptureFreshWorldBootstrap();
            var outbound = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(64, 65_536, 1_024));
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(1480),
                new PlayerHandle(new PlayerSlotId(0), new PlayerSessionGeneration(1)));
            Assert.True(world.RuntimeConnections.TryRegister(connection.Source, outbound));
            world.RuntimeConnections.PlayerSpawned(connection, new(connection.Player.Slot, 10, 10, 0, 0, 0, 0, 0));
            var queue = Assert.IsType<BoundedOutboundQueue>(typeof(TerrariaConnectionOutboundQueue)
                .GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(outbound));
            while (queue.TryRead(out _)) { }
            world.Start();
            try
            {
                var frames = new List<byte[]>();
                var deadline = Stopwatch.StartNew();
                while (!frames.Any(frame => frame[2] == 7) && deadline.Elapsed < TimeSpan.FromSeconds(5))
                {
                    while (queue.TryRead(out OutboundFrame frame))
                    {
                        frames.Add(frame.Bytes.ToArray());
                        if (frame.Bytes.Span[2] == 82 && BinaryPrimitives.ReadUInt16LittleEndian(frame.Bytes.Span[3..]) == 1)
                        {
                            var accepted = world.CaptureWorldBootstrap();
                            Assert.True(accepted.Runtime.DayTime);
                            Assert.False(accepted.Runtime.ForceHalloweenForToday);
                            Assert.Equal(victory, accepted.Runtime.ForceXMasForToday);
                            Assert.False(accepted.Transient.SnowMoon);
                        }
                    }
                    if (!frames.Any(frame => frame[2] == 7)) await Task.Delay(10, TestContext.Current.CancellationToken);
                }
                Assert.Equal(victory ? new byte[] { 82, 82, 7 } : new byte[] { 82, 82, 82, 7 },
                    frames.Select(frame => frame[2]).ToArray());
                Assert.Equal(new Message("Misc.FrostMoonScore", (victory ? 6377.5f : 2.5f).ToString(CultureInfo.CurrentCulture),
                    175, 75, 255), Read(frames[0]));
                if (victory)
                    Assert.Equal(new Message("Misc.StartedVictoryXmas", null, 50, 255, 130), Read(frames[1]));
                else
                {
                    Assert.Equal(new Message("Misc.EndedVictoryHalloween", null, 50, 255, 130), Read(frames[1]));
                    Assert.Equal(new Message("Misc.EndedVictoryXmas", null, 50, 255, 130), Read(frames[2]));
                }
                Assert.Equal(!victory, oldSnapshot.Runtime.ForceHalloweenForToday);
                Assert.Equal(!victory, oldSnapshot.Runtime.ForceXMasForToday);
                Assert.Null(world.CaptureWorldBootstrap().Progress);
            }
            finally { Assert.True(await world.StopAsync(TimeSpan.FromSeconds(5), captureFinalSave: false)); }
        }
    }

    private readonly record struct Message(string Key, string? Literal, byte R, byte G, byte B);

    private static Message Read(byte[] frame)
    {
        using var stream = new MemoryStream(frame);
        using var reader = new BinaryReader(stream);
        Assert.Equal(frame.Length, reader.ReadUInt16());
        Assert.Equal(82, reader.ReadByte());
        Assert.Equal(1, reader.ReadUInt16());
        Assert.Equal(255, reader.ReadByte());
        Assert.Equal(2, reader.ReadByte());
        string key = reader.ReadString();
        byte count = reader.ReadByte();
        Assert.InRange(count, (byte)0, (byte)1);
        string? literal = null;
        if (count == 1)
        {
            Assert.Equal(0, reader.ReadByte());
            literal = reader.ReadString();
        }
        var result = new Message(key, literal, reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
        Assert.Equal(stream.Length, stream.Position);
        return result;
    }
}
