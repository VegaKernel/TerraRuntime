using System.Buffers;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using Xunit;

namespace TerraRuntime.Tests.Tools1458.ActorRuntime;

internal static partial class ToolActorBoundaryChecks1458
{
    private static JsonElement SourceLocalCatch(JsonElement rows)
    {
        var row = rows.EnumerateArray().Single(value => value.GetProperty("kind").GetString() == "axe1052" &&
            value.GetProperty("local").GetBoolean() && value.GetProperty("full").GetBoolean() &&
            value.GetProperty("mode").GetString() == "near-owner");
        var after = row.GetProperty("steps")[0];
        Assert.False(after.GetProperty("after").GetProperty("active").GetBoolean());
        Assert.Equal(0, after.GetProperty("after").GetProperty("timeLeft").GetInt32());
        Assert.True(JsonElement.DeepEquals(row.GetProperty("beforeRandom"), after.GetProperty("random")));
        Assert.Single(row.GetProperty("frames").EnumerateArray());
        Assert.Equal(29, Convert.FromHexString(row.GetProperty("frames")[0].GetString()!)[2]);
        return row;
    }

    private static bool QualifiesOwnedSourceCatch(ToolActorRuntimeFixture1458 f,
        in ProjectileSnapshot actor, JsonElement originalRows)
    {
        if (actor.Type.Value != 1052 || actor.Ai.Ai0 != 1f ||
            !f.Players.TryGet(f.Connection.Player, out var member)) return false;
        // AI003 tests integer physical22x22 against the owner's physical body.
        // This authorized local branch reads no mouse and is intentionally
        // composed with the dedicated movement role used by the terrain oracle.
        int x = (int)actor.PositionX, y = (int)actor.PositionY;
        int ownerX = (int)member.PositionX, ownerY = (int)member.PositionY;
        if (x >= ownerX + 20 || x + 22 <= ownerX || y >= ownerY + 42 || y + 22 <= ownerY) return false;
        _ = SourceLocalCatch(originalRows); // independent executable, not a kernel-generated expectation
        return true;
    }

    private static void AssertOwnedSourceCatch(ToolActorRuntimeFixture1458 f,
        in ProjectileSnapshot before, VanillaUnifiedRandom1458 beforeRandom)
    {
        Assert.False(f.Shots.TryGet(before.Handle, out _));
        var beforeHandle = before.Handle;
        var ended = f.Sink.Events.Last(value => value.State.Handle == beforeHandle);
        Assert.Equal(ProjectileStateCommitKind.Despawn, ended.Kind);
        Assert.Equal(before.Type, ended.State.Type); Assert.Equal(before.Ai.Ai0, ended.State.Ai.Ai0);
        // Source29 saves the AI-entry position even though the inactive tail
        // continues after Kill. No client vector supplies this origin.
        Assert.Equal(BitConverter.SingleToInt32Bits(before.PositionX), BitConverter.SingleToInt32Bits(ended.State.PositionX));
        Assert.Equal(BitConverter.SingleToInt32Bits(before.PositionY), BitConverter.SingleToInt32Bits(ended.State.PositionY));
        Assert.True(f.Random.HasSameState(beforeRandom));
    }

    private static void SourceLocalAxeCatchRows()
    {
        int scenarios = 0;
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using var originals = Read("AllToolsExtension" + platform + "First1458");
            var row = SourceLocalCatch(originals.RootElement);
            using var f = new ToolActorRuntimeFixture1458(platform == "Windows");
            var actor = f.Birth(Launch(originals.RootElement, 5519));
            using var terrain = Read("AllToolActorTerrain" + platform + "First1458");
            var constructor = terrain.RootElement.EnumerateArray().Single(value =>
                value.GetProperty("item").GetInt32() == 5519 && value.GetProperty("mode").GetString() == "air")
                .GetProperty("before");
            Assert.Equal(1052, constructor.GetProperty("type").GetInt32());
            // ExtensionProgram clears every scene tile and calls SetDefaults;
            // its narrower PState producer does not emit liquid fields. Bind
            // the actual original fresh1052 liquid image explicitly instead of
            // treating an absent field as known false.
            foreach (string field in new[] { "wet", "lavaWet", "honeyWet", "shimmerWet" })
                Assert.False(constructor.GetProperty(field).GetBoolean());
            ConfigureSourceBefore(f, f.Member, ref actor, row, constructor);
            var sourceBefore = row.GetProperty("before");
            var after = row.GetProperty("steps")[0];
            f.Sink.Events.Clear();
            var outbound = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(32, 16384, 32));
            var observer = new ConnectionHandle(GameCommandSourceId.FromConnection(99502), f.Connection.Player);
            Assert.True(f.Replication.TryRegister(observer.Source, outbound));
            var spawn = new PlayerSpawnCommitRequest(observer.Player.Slot, 25, 25, 0, 0, 0, 0, 0);
            f.Replication.PlayerSpawned(observer, in spawn); ReadFallingFrames(outbound);
            var random = f.Random.Clone();
            Assert.True(f.Authority.TryTickState());
            AssertOwnedSourceCatch(f, in actor, random);
            SameRandom(after.GetProperty("random"), f.Random);
            Assert.Equal(after.GetProperty("next").GetInt32(), f.Random.Clone().Next());
            var ended = f.Sink.Events.Last().State;
            var expected = after.GetProperty("after");
            Assert.Equal(expected.GetProperty("velocity").GetProperty("bits")[0].GetInt32(), BitConverter.SingleToInt32Bits(ended.VelocityX));
            Assert.Equal(expected.GetProperty("velocity").GetProperty("bits")[1].GetInt32(), BitConverter.SingleToInt32Bits(ended.VelocityY));
            Assert.Equal(expected.GetProperty("ai").EnumerateArray().Select(value => BitConverter.SingleToInt32Bits(value.GetSingle())),
                new[] { BitConverter.SingleToInt32Bits(ended.Ai.Ai0), BitConverter.SingleToInt32Bits(ended.Ai.Ai1), BitConverter.SingleToInt32Bits(ended.Ai.Ai2) });
            var frames = ReadFallingFrames(outbound); Assert.Single(frames);
            Assert.Equal(29, frames[0].MessageId);
            var golden = DecodeFallingFrame(Convert.FromHexString(row.GetProperty("frames")[0].GetString()!));
            Assert.Equal(golden.Payload.Slice(4).ToArray(), frames[0].Payload.Slice(4).ToArray());
            Assert.Equal(sourceBefore.GetProperty("position").GetProperty("bits")[0].GetInt32(), BitConverter.SingleToInt32Bits(ended.PositionX));
            scenarios++;
        }
        Assert.Equal(2, scenarios);
    }
}
