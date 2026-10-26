using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;
using Xunit;

namespace TerraRuntime.Tests.Tools1458.ActorRuntime;

internal static partial class ToolActorBoundaryChecks1458
{
    private static void FallingSourceRows()
    {
        int scenarios = 0;
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using var source = Read("AllToolFallingLanding" + platform + "First1458");
            using var repeated = Read("AllToolFallingLanding" + platform + "Repeat1458");
            Assert.True(JsonElement.DeepEquals(source.RootElement, repeated.RootElement));
            foreach (var row in source.RootElement.EnumerateArray())
            {
                using var f = new ToolActorRuntimeFixture1458(platform == "Windows", sourcePlayerZero: true);
                // Original Program25 retains active player0 at constructor0,0.
                // It can reserve a landing drop via source FindOwner/packet22.
                Assert.Equal(0, f.Connection.Player.Slot.Value);
                Assert.Equal(0f, f.Member.PositionX); Assert.Equal(0f, f.Member.PositionY);
                // Real generic birth is followed by the independently captured
                // configured AI10/landing beforeimage; not a WorldGen birth oracle.
                for (int x = 0; x < 45; x++)
                    for (int y = 0; y < 50; y++) f.Tiles.Set(x, y, default);
                foreach (var cell in row.GetProperty("beforeCells").EnumerateArray())
                    f.Tiles.Set(cell.GetProperty("x").GetInt32(), cell.GetProperty("y").GetInt32(), FallingCell(cell));
                var type = new ProjectileTypeId(row.GetProperty("type").GetInt32());
                Assert.True(TerraRuntime.Gameplay.Projectiles.VanillaFallingBlock1458.TryGetTile(type, out var tileType));
                Assert.True(f.Authority.TrySpawnFallingBlock(tileType, 24, 24, out var actor));
                Assert.True(f.Authority.FallingBlocks.Contains(actor.Handle));
                Assert.Equal(0, actor.OriginalDamage);
                f.Authority.CancelFallingSpawn(actor.Handle);
                var before = row.GetProperty("initial");
                var outbound = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(1024, 262144, 1024));
                var observer = new ConnectionHandle(GameCommandSourceId.FromConnection(99501), f.Connection.Player);
                Assert.True(f.Replication.TryRegister(observer.Source, outbound));
                // Original RemoteClient constructor has an empty TileSections table.
                // NetMessage20 is SectionRange-filtered;17/27/29 remain broadcast.
                var sourceSections = new PlayerSectionStreamingState(f.Tiles.Dimensions);
                Assert.Equal(0, sourceSections.SentSectionCount);
                Assert.True(f.TileReplication.TryRegister(observer.Source, outbound, sourceSections));
                Assert.True(f.ItemReplication.TryRegister(observer.Source, outbound));
                var spawn = new PlayerSpawnCommitRequest(observer.Player.Slot, 24, 24, 0, 0, 0, 0, 0);
                f.Replication.PlayerSpawned(observer, in spawn);
                f.TileReplication.PlayerSpawned(observer, in spawn);
                f.ItemReplication.PlayerSpawned(observer, in spawn);
                // The independent landing harness uses direct NewProjectile
                // center(400,400), velocity(0,2.5), then configures its initial
                // body Y+2/velocity.5. Retain that real constructor publication;
                // the legacy world-specific producer above is a separate neighbor.
                var constructor = new ProjectileStateUpdate(type, byte.MaxValue,
                    FallingSingle(before, "position", "x"), FallingSingle(before, "position", "y") - 2f,
                    0f, 2.5f, default, 0, checked((short)before.GetProperty("damage").GetInt32()), 0f, 0);
                Assert.True(f.Shots.TrySpawnVanilla(in constructor, out actor));
                Assert.True(f.Shots.TryMarkCombatTrusted(actor.Handle));
                f.Authority.FallingBlocks.Register(actor.Handle);
                var initial = new ProjectileStateUpdate(type, byte.MaxValue,
                    FallingSingle(before, "position", "x"), FallingSingle(before, "position", "y"),
                    FallingSingle(before, "velocity", "x"), FallingSingle(before, "velocity", "y"),
                    new(FallingBits(before.GetProperty("ai")[0]), FallingBits(before.GetProperty("ai")[1]),
                        FallingBits(before.GetProperty("ai")[2])),
                    0, checked((short)before.GetProperty("damage").GetInt32()), 0f,
                    checked((short)before.GetProperty("originalDamage").GetInt32()));
                Assert.Equal(before.GetProperty("originalDamage").GetInt32(), actor.OriginalDamage);
                Assert.True(f.Shots.TryGetLifecycle(actor.Handle, out var life));
                life = life with { TimeLeft = before.GetProperty("timeLeft").GetInt32() };
                Assert.True(f.Shots.TryPrepareSimulation(in actor, out var setup)); Assert.NotNull(setup);
                Assert.True(setup!.TryStageMotion(in initial, in life));
                Assert.True(setup.TryAdoptUnpublished(out actor, out bool setupExpired)); Assert.False(setupExpired);
                RestoreRandom(row.GetProperty("beforeRandom"), f.Random);
                f.Sink.Events.Clear();
                var frames = new List<TerrariaFrame>(ReadFallingFrames(outbound));
                foreach (var phase in row.GetProperty("phases").EnumerateArray())
                {
                    Assert.True(f.Authority.TryTickState());
                    var expected = phase.GetProperty("projectile");
                    bool alive = expected.GetProperty("active").GetBoolean();
                    Assert.Equal(alive, f.Shots.TryGet(actor.Handle, out var actual));
                    if (alive)
                    {
                        SameFallingBody(expected, in actual);
                        Assert.True(f.Shots.TryGetLifecycle(actor.Handle, out var actualLife));
                        Assert.Equal(expected.GetProperty("timeLeft").GetInt32(), actualLife.TimeLeft);
                        Assert.Equal(expected.GetProperty("wet").GetBoolean(), actualLife.Liquid.Wet);
                    }
                    SameRandom(phase.GetProperty("random"), f.Random);
                    Assert.Equal(phase.GetProperty("next").GetInt32(), f.Random.Clone().Next());
                    SameFallingCells(f, phase.GetProperty("cells"));
                    SameFallingDrops(f, phase.GetProperty("drops"));
                    frames.AddRange(ReadFallingFrames(outbound));
                }
                Assert.False(f.Authority.FallingBlocks.Contains(actor.Handle));
                // Packet keys are runtime-generation identities, not original
                // whoAmI values. Compare all remaining source numeric bytes.
                var expectedFrames = row.GetProperty("frames").EnumerateArray()
                    .Select(value => DecodeFallingFrame(Convert.FromHexString(value.GetString()!))).ToArray();
                Assert.Equal(expectedFrames.Select(value => value.MessageId), frames.Select(value => value.MessageId));
                for (int index = 0; index < frames.Count; index++)
                {
                    int keyBytes = frames[index].MessageId is 27 or 29 ? 4 : 0;
                    Assert.Equal(expectedFrames[index].Payload.Slice(keyBytes).ToArray(),
                        frames[index].Payload.Slice(keyBytes).ToArray());
                }
                scenarios++;
            }
        }
        Assert.Equal(220, scenarios);
    }

    private static int FallingRawBits(JsonElement value) => unchecked((int)value.GetUInt32());
    private static float FallingBits(JsonElement value) => BitConverter.Int32BitsToSingle(FallingRawBits(value));
    private static float FallingSingle(JsonElement row, string vector, string lane) => FallingBits(row.GetProperty(vector).GetProperty(lane));
    private static WorldTile FallingCell(JsonElement cell)
    {
        int header = cell.GetProperty("header").GetInt32();
        var result = new WorldTile
        {
            Type = checked((ushort)cell.GetProperty("type").GetInt32()),
            Wall = checked((ushort)cell.GetProperty("wall").GetInt32()),
            LiquidAmount = checked((byte)cell.GetProperty("liquid").GetInt32()),
            FrameX = checked((short)cell.GetProperty("frameX").GetInt32()),
            FrameY = checked((short)cell.GetProperty("frameY").GetInt32()),
            TileColor = (byte)(header & 31),
            Flags = ((header & 32) != 0 ? WorldTileFlags.Active : 0) |
                ((header & 64) != 0 ? WorldTileFlags.Inactive : 0)
        };
        Assert.True(result.TrySetSourceShape((header & 1024) != 0, (byte)((header >> 12) & 7)));
        return result;
    }
    private static void SameFallingBody(JsonElement expected, in ProjectileSnapshot actual)
    {
        Assert.Equal(expected.GetProperty("type").GetInt32(), actual.Type.Value);
        Assert.Equal(expected.GetProperty("owner").GetInt32(), actual.Spawner);
        Assert.Equal(FallingRawBits(expected.GetProperty("position").GetProperty("x")), BitConverter.SingleToInt32Bits(actual.PositionX));
        Assert.Equal(FallingRawBits(expected.GetProperty("position").GetProperty("y")), BitConverter.SingleToInt32Bits(actual.PositionY));
        Assert.Equal(FallingRawBits(expected.GetProperty("velocity").GetProperty("x")), BitConverter.SingleToInt32Bits(actual.VelocityX));
        Assert.Equal(FallingRawBits(expected.GetProperty("velocity").GetProperty("y")), BitConverter.SingleToInt32Bits(actual.VelocityY));
        Assert.Equal(expected.GetProperty("ai").EnumerateArray().Select(FallingRawBits),
            new[] { BitConverter.SingleToInt32Bits(actual.Ai.Ai0), BitConverter.SingleToInt32Bits(actual.Ai.Ai1), BitConverter.SingleToInt32Bits(actual.Ai.Ai2) });
        Assert.Equal(expected.GetProperty("damage").GetInt32(), actual.Damage);
        Assert.Equal(expected.GetProperty("originalDamage").GetInt32(), actual.OriginalDamage);
    }
    private static void SameFallingCells(ToolActorRuntimeFixture1458 f, JsonElement cells)
    {
        foreach (var cell in cells.EnumerateArray())
        {
            var expected = FallingCell(cell);
            var actual = f.Tiles.Get(cell.GetProperty("x").GetInt32(), cell.GetProperty("y").GetInt32());
            Assert.Equal(expected.Type, actual.Type); Assert.Equal(expected.IsActive, actual.IsActive);
            Assert.Equal(expected.IsHalfBrick, actual.IsHalfBrick); Assert.Equal(expected.SourceSlope, actual.SourceSlope);
            Assert.Equal(expected.Wall, actual.Wall); Assert.Equal(expected.TileColor, actual.TileColor);
            Assert.Equal(expected.LiquidAmount, actual.LiquidAmount);
            // Non-frame-important cosmetic frame coordinates are deliberately
            // not stored as a claim of rendering parity; their RNG is asserted.
        }
    }
    private static void SameFallingDrops(ToolActorRuntimeFixture1458 f, JsonElement expected)
    {
        var actual = new WorldItemSnapshot[401]; int count = f.Items.CopyActive(actual);
        Assert.Equal(expected.GetArrayLength(), count);
        for (int index = 0; index < count; index++)
        {
            var value = expected[index]; var item = actual[index];
            Assert.Equal(value.GetProperty("type").GetInt32(), item.ItemNetId);
            Assert.Equal(value.GetProperty("stack").GetInt32(), item.Stack);
            Assert.Equal(FallingRawBits(value.GetProperty("position").GetProperty("x")), BitConverter.SingleToInt32Bits(item.PositionX));
            Assert.Equal(FallingRawBits(value.GetProperty("position").GetProperty("y")), BitConverter.SingleToInt32Bits(item.PositionY));
            Assert.Equal(FallingRawBits(value.GetProperty("velocity").GetProperty("x")), BitConverter.SingleToInt32Bits(item.VelocityX));
            Assert.Equal(FallingRawBits(value.GetProperty("velocity").GetProperty("y")), BitConverter.SingleToInt32Bits(item.VelocityY));
        }
    }
    private static List<TerrariaFrame> ReadFallingFrames(TerrariaConnectionOutboundQueue outbound)
    {
        var result = new List<TerrariaFrame>();
        while (FallingQueue(outbound).TryRead(out var frame)) result.Add(DecodeFallingFrame(frame.Bytes));
        return result;
    }
    private static TerrariaFrame DecodeFallingFrame(ReadOnlyMemory<byte> bytes)
    {
        var input = new ReadOnlySequence<byte>(bytes);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref input, out var decoded));
        Assert.Equal(0, input.Length); return decoded;
    }
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_queue")]
    private static extern ref BoundedOutboundQueue FallingQueue(TerrariaConnectionOutboundQueue owner);
}
