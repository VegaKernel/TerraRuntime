using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class RangedParentNpcPhase1458Tests
{
    public static IEnumerable<object[]> OriginalParents() => Cases("RangedParentNpcPhaseLinux1458.v1");
    public static IEnumerable<object[]> OriginalCollisionBodies() => Cases("ScutlixCollisionBodyLinux1458.v1");

    private static IEnumerable<object[]> Cases(string resource)
    {
        using var stream = typeof(RangedParentNpcPhase1458Tests).Assembly.GetManifestResourceStream(resource)!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var capture in document.RootElement.GetProperty("captures").EnumerateArray())
            foreach (var row in capture.GetProperty("cases").EnumerateArray())
                yield return [capture.GetProperty("seed").GetInt32(), row.GetRawText()];
    }

    [Theory, MemberData(nameof(OriginalParents)), MemberData(nameof(OriginalCollisionBodies))]
    public void Actual_parent_phase_matches_original_history_including_unstepped_children_and_shared_entropy(int seed, string json)
    {
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        Assert.Equal(seed, I(row, "caseSeed"));
        using var host = new RangedNpcPhaseFixture1458(row);
        var birth = row.GetProperty("birth").GetProperty("state");
        var newborn = birth.GetProperty("npc");
        var actor = host.SpawnObserved(newborn,
            (int)(F(newborn.GetProperty("position")[0]) + I(newborn, "width") * .5f),
            (int)(F(newborn.GetProperty("position")[1]) + I(newborn, "height")));
        // Assert the actual creation before importing the explicitly authored source input.
        AssertNpc(newborn, in actor);
        GoldCritterHitEffect1458Tests.AssertRandom(birth.GetProperty("rng"), host.Random);
        var input = row.GetProperty("input");
        host.ImportObserved(actor.Handle, input.GetProperty("npc"));
        host.ImportStatus(actor.Handle, input.GetProperty("npc"));
        foreach (var peer in input.GetProperty("activeNpcPeers").EnumerateArray())
        {
            byte slot = (byte)I(peer, "slot");
            if (slot == actor.Handle.Slot) continue;
            var state = peer.GetProperty("state");
            var created = host.SpawnObserved(state,
                (int)(F(state.GetProperty("position")[0]) + I(state, "width") * .5f),
                (int)(F(state.GetProperty("position")[1]) + I(state, "height")), slot);
            host.ImportObserved(created.Handle, state);
            host.ImportStatus(created.Handle, state);
        }
        var wire = new List<string>();
        host.Drain();
        AssertState(input, host);
        foreach (var frame in row.GetProperty("frames").EnumerateArray())
        {
            Assert.Empty(frame.GetProperty("worldActions").EnumerateArray());
            AssertState(frame.GetProperty("before"), host);
            host.Step(actor.Handle);
            wire.AddRange(host.Drain().Select(v => Convert.ToHexString(v).ToLowerInvariant()));
            var after = frame.GetProperty("after");
            AssertState(after, host);
            Assert.Equal(after.GetProperty("notifications").EnumerateArray().Select(v => v.GetString()).ToArray(), wire);
        }
    }

    private static void AssertState(JsonElement expected, RangedNpcPhaseFixture1458 host)
    {
        var active = new NpcSnapshot[host.Npcs.Capacity];
        int count = host.Npcs.CopyActive(active);
        var peers = expected.GetProperty("activeNpcPeers");
        Assert.Equal(peers.GetArrayLength(), count);
        for (int i = 0; i < count; i++)
        {
            var peer = peers[i];
            Assert.Equal(I(peer, "slot"), active[i].Handle.Slot);
            var npc = active[i];
            var source = peer.GetProperty("state");
            AssertNpc(source, in npc);
            Assert.True(host.Status.CaptureInteraction(in npc, out var status));
            Assert.Equal(source.GetProperty("stinky").GetBoolean(), status.Stinky);
            for (int slot = 0; slot < 20; slot++)
            {
                Assert.Equal(source.GetProperty("rawBuffTypes")[slot].GetInt32(), status.Slots[slot].Type);
                Assert.Equal(source.GetProperty("rawBuffTimes")[slot].GetInt32(), status.Slots[slot].Duration);
            }
        }
        GoldCritterHitEffect1458Tests.AssertRandom(expected.GetProperty("rng"), host.Random);
        var projectiles = new ProjectileSnapshot[host.Projectiles.Capacity];
        int shotCount = host.Projectiles.CopyActive(projectiles);
        var shots = expected.GetProperty("projectiles");
        Assert.Equal(shots.GetArrayLength(), shotCount);
        for (int i = 0; i < shotCount; i++)
        {
            var source = shots[i]; var shot = projectiles[i];
            Assert.Equal(I(source, "slot"), shot.Handle.Slot);
            Assert.Equal(I(source, "type"), shot.Type.Value);
            Assert.Equal(I(source, "owner"), shot.Spawner);
            AssertBits(source.GetProperty("position")[0], shot.PositionX);
            AssertBits(source.GetProperty("position")[1], shot.PositionY);
            AssertBits(source.GetProperty("velocity")[0], shot.VelocityX);
            AssertBits(source.GetProperty("velocity")[1], shot.VelocityY);
            AssertBits(source.GetProperty("knockBack"), shot.KnockBack);
            Assert.Equal(I(source, "damage"), shot.Damage);
            Assert.Equal(I(source, "originalDamage"), shot.OriginalDamage);
            AssertBits(source.GetProperty("ai")[0], shot.Ai.Ai0);
            AssertBits(source.GetProperty("ai")[1], shot.Ai.Ai1);
            AssertBits(source.GetProperty("ai")[2], shot.Ai.Ai2);
        }
    }

    // Source-only diagnostics such as Main's target-frame stamp, npcSlots and
    // particle state have no retained runtime owner. The independent cache
    // comparator reports them explicitly; this test does not assert full parity.
    private static void AssertNpc(JsonElement expected, in NpcSnapshot actual)
    {
        Assert.True(actual.IsActive);
        Assert.Equal(I(expected, "type"), actual.Type);
        Assert.Equal(I(expected, "netId"), actual.NetId);
        Assert.Equal(I(expected, "target"), actual.Target);
        AssertBits(expected.GetProperty("position")[0], actual.PositionX);
        AssertBits(expected.GetProperty("position")[1], actual.PositionY);
        AssertBits(expected.GetProperty("velocity")[0], actual.VelocityX);
        AssertBits(expected.GetProperty("velocity")[1], actual.VelocityY);
        AssertAi(expected.GetProperty("ai"), actual.Ai);
        var s = actual.Simulation;
        AssertAi(expected.GetProperty("localAi"), s.LocalAi);
        AssertBits(expected.GetProperty("oldPosition")[0], s.OldPositionX);
        AssertBits(expected.GetProperty("oldPosition")[1], s.OldPositionY);
        AssertBits(expected.GetProperty("oldVelocity")[0], s.OldVelocityX);
        AssertBits(expected.GetProperty("oldVelocity")[1], s.OldVelocityY);
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(actual.TypeIdentity, actual.NetIdentity, out var definition));
        Assert.True(definition.TryResolveHitbox(in s, out var body));
        Assert.Equal((I(expected, "width"), I(expected, "height")), (body.Width, body.Height));
        AssertBits(expected.GetProperty("scale"), s.Scale);
        AssertBits(expected.GetProperty("knockBack"), s.KnockBackResist);
        AssertBits(expected.GetProperty("rotation"), s.Rotation);
        AssertBits(expected.GetProperty("money"), s.MoneyValue);
        AssertBits(expected.GetProperty("shimmer"), s.ShimmerTransparency);
        AssertBits(expected.GetProperty("teleportTime"), s.TeleportTime);
        Assert.Equal(expected.GetProperty("frameCounter").GetString(), BitConverter.DoubleToUInt64Bits(s.FrameCounter).ToString("x16"));
        Assert.Equal(I(expected, "direction"), s.DirectionX); Assert.Equal(I(expected, "directionY"), s.DirectionY);
        Assert.Equal(I(expected, "spriteDirection"), s.SpriteDirection);
        Assert.Equal(I(expected, "life"), s.Life); Assert.Equal(I(expected, "lifeMax"), s.LifeMax);
        Assert.Equal(I(expected, "baseLifeMax"), s.BaseLifeMax); Assert.Equal(I(expected, "timeLeft"), s.TimeLeft);
        Assert.Equal(I(expected, "frameIndex"), s.FrameIndex); Assert.Equal(I(expected, "alpha"), s.Alpha);
        Assert.Equal(I(expected, "homeX"), s.HomeTileX); Assert.Equal(I(expected, "homeY"), s.HomeTileY);
        Assert.Equal(B(expected, "homeless"), s.Homeless); Assert.Equal(B(expected, "townNpc"), s.TownNpc);
        Assert.Equal(B(expected, "friendly"), s.Friendly); Assert.Equal(B(expected, "chaseable"), s.Chaseable);
        Assert.Equal(B(expected, "immortal"), s.Immortal); Assert.Equal(B(expected, "dontTakeDamage"), s.DontTakeDamage);
        Assert.Equal(I(expected, "damage"), s.DamageOverride ?? s.BaseDamage);
        Assert.Equal(I(expected, "defense"), s.DefenseOverride ?? s.BaseDefense);
        Assert.Equal(I(expected, "baseDamage"), s.BaseDamage); Assert.Equal(I(expected, "baseDefense"), s.BaseDefense);
        Assert.Equal(I(expected, "immunity"), s.CatchableNpcTempImmunityCounter);
        Assert.Equal((short)I(expected, "release"), s.ReleaseOwner);
        Assert.Equal(I(expected, "breath"), s.Breath); Assert.Equal(I(expected, "breathCounter"), s.BreathCounter);
        Assert.Equal(I(expected, "wetCount"), s.WetCount);
        Assert.Equal(B(expected, "wet"), s.Wet); Assert.Equal(B(expected, "honeyWet"), s.HoneyWet);
        Assert.Equal(B(expected, "lavaWet"), s.LavaWet); Assert.Equal(B(expected, "shimmerWet"), s.ShimmerWet);
        Assert.Equal(B(expected, "collideX"), s.CollideX); Assert.Equal(B(expected, "collideY"), s.CollideY);
        Assert.Equal(B(expected, "noGravity"), s.NoGravity); Assert.Equal(B(expected, "noTileCollide"), s.NoTileCollide);
        Assert.Equal(B(expected, "justHit"), s.JustHit); Assert.Equal(B(expected, "stairFall"), s.StairFall);
        Assert.Equal(B(expected, "spawnedFromStatue"), s.SpawnedFromStatue);
        Assert.Equal(I(expected, "friendlyRegen"), s.FriendlyRegenerationCounter);
        Assert.Equal(I(expected, "lifeRegen"), s.LifeRegenCounter);
    }

    private static void AssertAi(JsonElement expected, in NpcAiState actual)
    {
        AssertBits(expected[0], actual.Ai0); AssertBits(expected[1], actual.Ai1);
        AssertBits(expected[2], actual.Ai2); AssertBits(expected[3], actual.Ai3);
    }

    private static void AssertBits(JsonElement expected, float? actual)
    {
        Assert.True(actual.HasValue);
        Assert.Equal(expected.GetString(), BitConverter.SingleToUInt32Bits(actual.GetValueOrDefault()).ToString("x8"));
    }

    private static float F(JsonElement value) => BitConverter.UInt32BitsToSingle(Convert.ToUInt32(value.GetString(), 16));
    private static int I(JsonElement value, string name) => value.GetProperty(name).GetInt32();
    private static bool B(JsonElement value, string name) => value.GetProperty(name).GetBoolean();
}
