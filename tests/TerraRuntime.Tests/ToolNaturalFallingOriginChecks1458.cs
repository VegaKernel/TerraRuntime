using System.Buffers;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;
using Xunit;

namespace TerraRuntime.Tests.Tools1458.ActorRuntime;

public sealed class AllToolNaturalFallingOrigin1458Tests
{
    [Fact]
    public void Natural_falling_origins_preserve_constructor_publication_and_owned_landing() =>
        ToolActorBoundaryChecks1458.NaturalFallingOrigins();
}

internal static partial class ToolActorBoundaryChecks1458
{
    internal static void NaturalFallingOrigins()
    {
        int episodes = 0;
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using var first = Read("NaturalFalling" + platform + "First1458");
            using var repeat = Read("NaturalFalling" + platform + "Repeat1458");
            Assert.True(JsonElement.DeepEquals(first.RootElement, repeat.RootElement));
            foreach (var row in first.RootElement.EnumerateArray())
                foreach (bool legacyQueue in new[] { false, true })
                {
                    using var f = new ToolActorRuntimeFixture1458(platform == "Windows", sourcePlayerZero: true);
                    for (int x = 0; x < 100; x++) for (int y = 0; y < 100; y++) f.Tiles.Set(x, y, default);
                    foreach (var cell in row.GetProperty("beforeMutationCells").EnumerateArray())
                        f.Tiles.Set(cell.GetProperty("x").GetInt32(), cell.GetProperty("y").GetInt32(), FallingCell(cell));
                    RestoreRandom(row.GetProperty("beforeRandom"), f.Random);
                    var output = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(1024, 262144, 1024));
                    var observer = new ConnectionHandle(GameCommandSourceId.FromConnection(99601), f.Connection.Player);
                    Assert.True(f.Replication.TryRegister(observer.Source, output));
                    Assert.True(f.ItemReplication.TryRegister(observer.Source, output));
                    Assert.True(f.TileReplication.TryRegister(observer.Source, output, new PlayerSectionStreamingState(f.Tiles.Dimensions)));
                    var spawn = new PlayerSpawnCommitRequest(observer.Player.Slot, 0, 0, 0, 0, 0, 0, 0);
                    f.Replication.PlayerSpawned(observer, in spawn); f.ItemReplication.PlayerSpawned(observer, in spawn);
                    f.TileReplication.PlayerSpawned(observer, in spawn);
                    ReadFallingFrames(output);
                    var world = f.Composition.WorldTileAuthority;
                    int birthCallbacks = 0;
                    f.Sink.BeforeReplication = (kind, born) =>
                    {
                        if (kind != ProjectileStateCommitKind.Spawn) return;
                        birthCallbacks++;
                        Assert.False(f.Tiles.Get(25, 25).IsActive);
                        Assert.False(f.Tiles.Get(25, 26).IsActive);
                        Assert.True(f.Shots.TryGet(born.Handle, out var live));
                        SameFallingBody(row.GetProperty("initial"), in live);
                        Assert.True(f.Shots.IsCombatTrusted(born.Handle));
                        Assert.True(f.Authority.FallingBlocks.Contains(born.Handle));
                    };
                    if (legacyQueue)
                    {
                        // The legacy wakeup starts after this source support mutation.
                        // No projectile constructor, cancellation or replacement is injected.
                        f.Tiles.EnableFallingBlockUpdates();
                        f.Tiles.Set(25, 26, FallingCell(row.GetProperty("beforeCells").EnumerateArray()
                            .Single(cell => cell.GetProperty("x").GetInt32() == 25 && cell.GetProperty("y").GetInt32() == 26)));
                        world.TickFallingBlocks(f.Authority);
                    }
                    else
                    {
                        Assert.True(world.TryCaptureToolTreeWorld(out var context)); Assert.NotNull(context);
                        Assert.True(f.Players.TryCaptureToolTreeActors(out var actors)); Assert.NotNull(actors);
                        var overlay = new ToolWorldTileOverlay1458(f.Tiles);
                        Assert.True(world.TryCreateToolMutationPlan(overlay, out var plan)); Assert.NotNull(plan);
                        using (plan)
                        {
                            Assert.True(world.TryCreateToolSourcePass(overlay, plan.SourceRandom, false, context.HardMode,
                                context.Facts, actors.Actors, plan, out var source)); Assert.NotNull(source);
                            Assert.True(source.TryKillTile(25, 26, false, false, true, out _));
                            var end = plan.CapturePublicationOffset();
                            Assert.True(plan.TrySeal()); Assert.True(context.IsCurrent); Assert.True(actors.IsCurrent);
                            Assert.True(source.IsCurrentOwned); Assert.True(plan.IsCurrentOwned);
                            Assert.True(plan.TryAdoptWholeUnpublished(out var publication)); Assert.NotNull(publication);
                            Assert.True(publication.TryPublishThrough(in end));
                        }
                    }
                    f.Sink.BeforeReplication = null;
                    Assert.Equal(1, birthCallbacks);
                    var image = new ProjectileSnapshot[f.Shots.Capacity]; Assert.Equal(1, f.Shots.CopyActive(image));
                    var actor = image[0];
                    Assert.True(f.Shots.IsCombatTrusted(actor.Handle)); Assert.True(f.Authority.FallingBlocks.Contains(actor.Handle));
                    SameFallingBody(row.GetProperty("initial"), in actor);
                    SameRandom(row.GetProperty("originRandom"), f.Random);
                    SameFallingCells(f, row.GetProperty("beforeCells"));
                    var frames = new List<TerraRuntime.Protocol.TerrariaFrame>(ReadFallingFrames(output));
                    foreach (var phase in row.GetProperty("phases").EnumerateArray())
                    {
                        Assert.True(f.Authority.TryTickState());
                        var expected = phase.GetProperty("projectile");
                        bool alive = expected.GetProperty("active").GetBoolean();
                        Assert.Equal(alive, f.Shots.TryGet(actor.Handle, out var current));
                        if (alive)
                        {
                            SameFallingBody(expected, in current);
                            Assert.True(f.Shots.TryGetLifecycle(actor.Handle, out var life));
                            Assert.Equal(expected.GetProperty("timeLeft").GetInt32(), life.TimeLeft);
                        }
                        SameRandom(phase.GetProperty("random"), f.Random);
                        Assert.Equal(phase.GetProperty("next").GetInt32(), f.Random.Clone().Next());
                        SameFallingCells(f, phase.GetProperty("cells")); SameFallingDrops(f, phase.GetProperty("drops"));
                        frames.AddRange(ReadFallingFrames(output));
                    }
                    Assert.False(f.Authority.FallingBlocks.Contains(actor.Handle));
                    var golden = row.GetProperty("frames").EnumerateArray()
                        .Select(value => DecodeFallingFrame(Convert.FromHexString(value.GetString()!))).ToArray();
                    Assert.Equal(golden.Select(frame => frame.MessageId), frames.Select(frame => frame.MessageId));
                    for (int index = 0; index < frames.Count; index++)
                    {
                        int keys = frames[index].MessageId is 27 or 29 ? 4 : 0;
                        Assert.Equal(golden[index].Payload.Slice(keys).ToArray(), frames[index].Payload.Slice(keys).ToArray());
                    }
                    episodes++;
                }
        }
        Assert.Equal(176, episodes);
        NaturalOriginCallbacks();
        SiblingWorldBirthCallbacks();
    }

    private static void NaturalOriginCallbacks()
    {
        foreach (string mode in new[] { "throw", "reentry", "same-generation", "replacement" })
        {
            using var f = new ToolActorRuntimeFixture1458(true, sourcePlayerZero: true);
            for (int x = 20; x <= 30; x++) for (int y = 20; y <= 31; y++) f.Tiles.Set(x, y, default);
            f.Tiles.Set(25, 25, new WorldTile { Type = 53, Flags = WorldTileFlags.Active });
            f.Tiles.EnableFallingBlockUpdates();
            f.Tiles.Set(25, 26, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            f.Tiles.Set(25, 26, new WorldTile { Type = 1 });
            var world = f.Composition.WorldTileAuthority;
            int calls = 0; ProjectileSnapshot born = default, revised = default, replacement = default;
            var random = f.Random.Clone();
            f.Sink.BeforeReplication = (kind, state) =>
            {
                if (kind != ProjectileStateCommitKind.Spawn || state.Type.Value != 31 || calls++ != 0) return;
                born = state;
                Assert.False(f.Tiles.Get(25, 25).IsActive);
                Assert.True(f.Shots.TryGet(state.Handle, out var live));
                Assert.Equal(.5f, live.VelocityY); Assert.Equal(state.PositionY + 2f, live.PositionY);
                Assert.True(f.Authority.FallingBlocks.Contains(state.Handle));
                if (mode == "throw") throw new InvalidOperationException("natural birth observer");
                if (mode == "reentry") world.TickFallingBlocks(f.Authority);
                if (mode == "same-generation")
                {
                    var change = new ProjectileStateUpdate(live.Type, live.Spawner, live.PositionX + 1f, live.PositionY,
                        live.VelocityX, live.VelocityY, live.Ai, 0, live.Damage, live.KnockBack, live.OriginalDamage);
                    Assert.True(f.Shots.TryUpdate(live.Handle, in change, out revised));
                }
                if (mode == "replacement")
                {
                    Assert.True(f.Shots.TryRemove(live.Handle, out _));
                    var change = new ProjectileStateUpdate(new(1), byte.MaxValue, 900f, 900f, 1f, 0f, default, 0, 0, 0f, 0);
                    Assert.True(f.Shots.TrySpawnVanilla(in change, out replacement));
                }
            };
            if (mode == "throw") Assert.Throws<InvalidOperationException>(() => world.TickFallingBlocks(f.Authority));
            else world.TickFallingBlocks(f.Authority);
            f.Sink.BeforeReplication = null;
            Assert.Equal(1, calls); Assert.True(f.Random.HasSameState(random));
            Assert.False(f.Tiles.Get(25, 25).IsActive);
            if (mode == "same-generation") { Assert.True(f.Shots.TryGet(born.Handle, out var actual)); Assert.Equal(revised, actual); }
            if (mode == "replacement")
            {
                Assert.False(f.Shots.TryGet(born.Handle, out _));
                Assert.True(f.Shots.TryGet(replacement.Handle, out var actual)); Assert.Equal(replacement, actual);
                f.Authority.FallingBlocks.ForgetDisplaced(f.Shots);
                Assert.False(f.Authority.FallingBlocks.Contains(born.Handle));
                Assert.False(f.Authority.FallingBlocks.Contains(replacement.Handle));
            }
            if (mode is "same-generation" or "replacement")
            {
                var output = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(32, 16384, 32));
                var observer = new ConnectionHandle(GameCommandSourceId.FromConnection(99602), f.Connection.Player);
                Assert.True(f.Replication.TryRegister(observer.Source, output));
                var spawn = new PlayerSpawnCommitRequest(observer.Player.Slot, 0, 0, 0, 0, 0, 0, 0);
                f.Replication.PlayerSpawned(observer, in spawn);
                var late = ReadFallingFrames(output); Assert.Single(late);
                var payload = late[0].Payload.ToArray();
                var expected = mode == "replacement" ? replacement : revised;
                Assert.Equal(expected.Type.Value, System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(payload.AsSpan(20, 2)));
                Assert.Equal(BitConverter.SingleToInt32Bits(expected.PositionX), System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(4, 4)));
                Assert.Equal(BitConverter.SingleToInt32Bits(expected.PositionY), System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(8, 4)));
            }
            // No birth retry after a throw or replacement of the accepted generation.
            int births = f.Sink.Events.Count(entry => entry.Kind == ProjectileStateCommitKind.Spawn);
            world.TickFallingBlocks(f.Authority);
            Assert.Equal(births, f.Sink.Events.Count(entry => entry.Kind == ProjectileStateCommitKind.Spawn));
        }
    }
    private static void SiblingWorldBirthCallbacks()
    {
        // Configured prepared-source siblings exercise the SAME publication
        // boundary. Their full AI/kill behavior is not asserted by this test.
        foreach (int type in new[] { 28, 75, 736, 727 })
            foreach (bool replacementMode in new[] { false, true })
            {
                using var f = new ToolActorRuntimeFixture1458(true, sourcePlayerZero: true);
                var world = f.Composition.WorldTileAuthority;
                var overlay = new ToolWorldTileOverlay1458(f.Tiles);
                Assert.True(world.TryCreateToolMutationPlan(overlay, out var plan)); Assert.NotNull(plan);
                using (plan)
                {
                    var input = new ProjectileStateUpdate(new(type), byte.MaxValue, 400f, 400f, 0f, 0f, default, 0, 0, 0f, 0);
                    Assert.True(plan.TryStageSourceProjectile(f.Shots, in input, true, out var staged));
                    var end = plan.CapturePublicationOffset(); Assert.True(plan.TrySeal());
                    Assert.True(plan.TryAdoptWholeUnpublished(out var publication)); Assert.NotNull(publication);
                    int calls = 0; ProjectileSnapshot accepted = default;
                    f.Sink.BeforeReplication = (kind, value) =>
                    {
                        if (kind != ProjectileStateCommitKind.Spawn || value.Handle != staged.Handle || calls++ != 0) return;
                        Assert.True(f.Shots.TryGet(value.Handle, out var live));
                        if (replacementMode)
                        {
                            Assert.True(f.Shots.TryRemove(live.Handle, out _));
                            var next = new ProjectileStateUpdate(new(1), byte.MaxValue, 900f, 900f, 1f, 0f, default, 0, 0, 0f, 0);
                            Assert.True(f.Shots.TrySpawnVanilla(in next, out accepted));
                        }
                        else
                        {
                            var next = new ProjectileStateUpdate(live.Type, live.Spawner, live.PositionX + 1f, live.PositionY,
                                live.VelocityX, live.VelocityY, live.Ai, 0, live.Damage, live.KnockBack, live.OriginalDamage);
                            Assert.True(f.Shots.TryUpdate(live.Handle, in next, out accepted));
                        }
                    };
                    Assert.True(publication.TryPublishThrough(in end)); f.Sink.BeforeReplication = null;
                    Assert.Equal(1, calls); Assert.True(f.Shots.TryGet(accepted.Handle, out var retained)); Assert.Equal(accepted, retained);
                    var output = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(32, 16384, 32));
                    var observer = new ConnectionHandle(GameCommandSourceId.FromConnection(99603), f.Connection.Player);
                    Assert.True(f.Replication.TryRegister(observer.Source, output));
                    var spawn = new PlayerSpawnCommitRequest(observer.Player.Slot, 0, 0, 0, 0, 0, 0, 0);
                    f.Replication.PlayerSpawned(observer, in spawn);
                    var late = ReadFallingFrames(output); Assert.Single(late); var bytes = late[0].Payload.ToArray();
                    Assert.Equal(accepted.Type.Value, System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(20, 2)));
                    Assert.Equal(BitConverter.SingleToInt32Bits(accepted.PositionX), System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4, 4)));
                }
            }
    }

}
