using System.IO.Compression;
using System.Text.Json;
using global::Multiplicity.Packets;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;

namespace TerraRuntime.Tests;

public sealed class NpcSourceProjectileBirth1458Tests
{
    public static IEnumerable<object[]> OriginalBirths()
    {
        using var stream = typeof(NpcSourceProjectileBirth1458Tests).Assembly
            .GetManifestResourceStream("NpcSourceProjectileBirthLinux1458.v1")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.GetProperty("cases").EnumerateArray())
            yield return [row.GetProperty("projectileType").GetInt32(), row.GetProperty("sourceCase").GetRawText()];
    }

    [Theory, MemberData(nameof(OriginalBirths))]
    public void Actual_NPC_parent_birth_preserves_the_original_banner_and_literal_packet27(int projectileType, string json)
    {
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        using var host = new RangedNpcPhaseFixture1458(row);
        var component = row.GetProperty("component");
        var before = component.ValueKind == JsonValueKind.Null ? row.GetProperty("input") : component.GetProperty("before");
        var newborn = row.GetProperty("birth").GetProperty("state").GetProperty("npc");
        var actor = host.SpawnObserved(newborn, CenterX(newborn), BottomY(newborn));
        host.AssertBirth(row.GetProperty("birth"));
        actor = host.ImportObserved(actor.Handle, before.GetProperty("npc"));
        host.ImportStatus(actor.Handle, before.GetProperty("npc"));
        var peers = new List<NpcSnapshot>();
        foreach (var entry in before.GetProperty("activeNpcPeers").EnumerateArray())
        {
            byte slot = entry.GetProperty("slot").GetByte();
            if (slot == actor.Handle.Slot) continue;
            var state = entry.GetProperty("state");
            var peer = host.SpawnObserved(state, CenterX(state), BottomY(state), slot,
                (ushort)state.GetProperty("target").GetInt32());
            peer = host.ImportObserved(peer.Handle, state);
            host.ImportStatus(peer.Handle, state);
            peers.Add(peer);
        }
        RangedNpcPhaseFixture1458.AssertRandom(before.GetProperty("rng"), host.Random);
        host.Drain();
        var emitted = new List<string>();
        if (component.ValueKind != JsonValueKind.Null)
        {
            // This source438 history first executes its existing391 mount once.
            // The independent source did not restore RNG or fabricate facing.
            Assert.Equal(438, projectileType);
            var mount = Assert.Single(peers, peer => peer.Type == 391);
            host.Step(mount.Handle);
            emitted.AddRange(host.Drain().Select(Convert.ToHexStringLower));
            Assert.Equal(component.GetProperty("after").GetProperty("notifications").EnumerateArray()
                .Select(x => x.GetString()).ToArray(), emitted);
        }
        RangedNpcPhaseFixture1458.AssertRandom(row.GetProperty("input").GetProperty("rng"), host.Random);
        bool observedBirth = false;
        foreach (var frame in row.GetProperty("frames").EnumerateArray())
        {
            Assert.Empty(frame.GetProperty("worldActions").EnumerateArray());
            RangedNpcPhaseFixture1458.AssertRandom(frame.GetProperty("before").GetProperty("rng"), host.Random);
            host.Step(actor.Handle);
            emitted.AddRange(host.Drain().Select(Convert.ToHexStringLower));
            var after = frame.GetProperty("after");
            RangedNpcPhaseFixture1458.AssertRandom(after.GetProperty("rng"), host.Random);
            Assert.Equal(after.GetProperty("notifications").EnumerateArray().Select(x => x.GetString()).ToArray(), emitted);
            if (!after.GetProperty("projectiles").EnumerateArray().Any(x => x.GetProperty("type").GetInt32() == projectileType)) continue;

            var snapshots = new ProjectileSnapshot[host.Projectiles.Capacity];
            int count = host.Projectiles.CopyActive(snapshots);
            var shot = Assert.Single(snapshots.AsSpan(0, count).ToArray(), x => x.Type.Value == projectileType);
            string originalHex = Assert.Single(after.GetProperty("notifications").EnumerateArray()
                .Select(x => x.GetString()!), hex => Convert.FromHexString(hex)[2] == 27);
            byte[] originalBytes = Convert.FromHexString(originalHex);
            var packet = Assert.IsType<ProjectileNew>(TerrariaPacket.Deserialize((ReadOnlyMemory<byte>)originalBytes));
            Assert.True(packet.BannerIdToRespondTo > 0);
            Assert.Equal(packet.BannerIdToRespondTo, shot.BannerIdToRespondTo);
            Assert.Equal(packet.Damage, shot.Damage);
            Assert.Equal(packet.OriginalDamage, shot.OriginalDamage);
            Assert.Equal(0, (int)shot.OriginalDamage);
            Assert.Equal((byte)255, shot.Spawner);
            Assert.True(host.Projectiles.TryGetServerNpcSource(shot.Handle, out var source));
            Assert.Equal(actor.Handle, source);
            Assert.True(host.Projectiles.TryGetLifecycle(shot.Handle, out var life));
            Assert.False(life.NpcProjectile);
            Assert.False(host.RandomInputImported);
            observedBirth = true;
            break;
        }
        Assert.True(observedBirth);
    }

    private static int CenterX(JsonElement state) =>
        (int)(F(state.GetProperty("position")[0]) + state.GetProperty("width").GetInt32() * .5f);
    private static int BottomY(JsonElement state) =>
        (int)(F(state.GetProperty("position")[1]) + state.GetProperty("height").GetInt32());
    private static float F(JsonElement value) => BitConverter.UInt32BitsToSingle(Convert.ToUInt32(value.GetString(), 16));
}
