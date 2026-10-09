using System.Buffers;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Core;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class BulletSourceCandidateAuthority1458Tests
{
    [Fact]
    public void Both_source_platforms_complete_all_512_bullet_uses_with_source_ammo_rng_and_owned_births()
    {
        int cases = 0;
        foreach (string resource in new[] { "BulletPrefixedShootLinux1458", "BulletPrefixedShootWindows1458" })
        {
            using var source = Read(resource);
            Assert.Equal(256, source.RootElement.GetProperty("profiles").GetArrayLength());
            foreach (var row in source.RootElement.GetProperty("profiles").EnumerateArray())
            {
                using var f = Create(row);
                var packets = Packets(row);
                var before = f.Random.Clone();
                int active = f.Store.ActiveCount;
                int equipmentNotifications = 0;
                f.Events.OnEquipment = () => equipmentNotifications++;
                for (int index = 0; index < packets.Length; index++)
                {
                    Assert.True(f.Authority.TryApply(new ClientProjectileUpdateRuntimeCommand(f.Connection, packets[index])));
                    if (index == packets.Length - 1) continue;
                    Assert.Equal(active, f.Store.ActiveCount);
                    Assert.Equal(20, f.Ammo());
                    Assert.Equal(0, f.Authority.PromotedClientProjectileSpawns);
                    Assert.Equal(0, f.Outbound.QueuedFrames);
                    Assert.True(f.Random.HasSameState(before));
                }
                Assert.True(packets.Length == f.Authority.PromotedClientProjectileSpawns,
                    $"resource={resource}, weapon={I(row, "weapon")}, prefix={I(row, "prefix")}, promoted={f.Authority.PromotedClientProjectileSpawns}");
                Assert.Equal(I(row.GetProperty("ammoAfter"), "stack"), f.Ammo());
                EqualRandom(row.GetProperty("after"), f.Random);
                Assert.Equal(I(row, "nextAfter"), f.Random.Clone().Next());
                Assert.Equal(packets.Length, f.Outbound.QueuedFrames);
                var queue = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue).GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Outbound)!;
                int published = 0;
                while (queue.TryRead(out var frame))
                {
                    var buffer = new ReadOnlySequence<byte>(frame.Bytes);
                    Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref buffer, out var parsed));
                    Assert.Equal(TerrariaProjectileDecodeResult.Decoded, TerrariaProjectileDecoder.TryDecodeUpdate(in parsed, out var peer));
                    EqualBody(packets[published++], peer);
                }
                Assert.Equal(packets.Length, published);
                Assert.True(f.Replication.WireIdentities.TryResolve(packets[^1].Key, out var last));
                Assert.True(f.Store.TryGet(last, out var retained));
                Assert.True(f.Store.IsCombatTrusted(last));
                Assert.Equal(packets[^1].Damage, retained.Damage);
                Assert.Equal(packets[^1].ProjectileType, retained.Type.Value);
                Assert.Equal(1, equipmentNotifications);
                cases++;
            }
        }
        Assert.Equal(512, cases);
    }

    [Fact]
    public void Genuine_Broken_Megashark_24_and_25_damage_are_accepted_but_invented_26_is_not()
    {
        foreach (string resource in new[] { "BulletPrefixedShootLinux1458", "BulletPrefixedShootWindows1458" })
        {
            using var source = Read(resource);
            var row = source.RootElement.GetProperty("profiles").EnumerateArray().Single(x => I(x, "weapon") == 533 && I(x, "prefix") == 39);
            var packet = Assert.Single(Packets(row));
            Assert.Equal(resource.Contains("Linux") ? 25 : 24, packet.Damage);
            using var positive = Create(row);
            Submit(positive, packet);
            Assert.Equal(1, positive.Authority.PromotedClientProjectileSpawns);
            Assert.Equal(I(row.GetProperty("ammoAfter"), "stack"), positive.Ammo());
            EqualRandom(row.GetProperty("after"), positive.Random);
            using var negative = Create(row);
            var before = negative.Random.Clone();
            int active = negative.Store.ActiveCount;
            Submit(negative, packet with { Damage = 26 });
            Assert.Equal(0, negative.Authority.PromotedClientProjectileSpawns);
            Assert.Equal(active, negative.Store.ActiveCount);
            Assert.Equal(20, negative.Ammo());
            Assert.True(negative.Random.HasSameState(before));
            Assert.Equal(0, negative.Outbound.QueuedFrames);
        }
    }

    [Fact]
    public void Late_child_or_owner_drift_refuses_the_whole_proposal_without_partial_births_or_rng()
    {
        using var source = Read("BulletPrefixedShootWindows1458");
        var row = source.RootElement.GetProperty("profiles").EnumerateArray().Single(x => I(x, "weapon") == 4703 && I(x, "prefix") == 42);
        var packets = Packets(row);
        foreach (int mutation in new[] { 0, 1, 2, 3 })
        {
            using var f = Create(row);
            var before = f.Random.Clone();
            int active = f.Store.ActiveCount;
            Submit(f, packets[0]);
            Assert.Equal(0, f.Authority.PromotedClientProjectileSpawns);
            var second = packets[1];
            if (mutation == 0) second = second with { Damage = checked((short)(second.Damage + 1)) };
            if (mutation == 1) second = second with { VelocityX = second.VelocityX + 1f };
            if (mutation == 2) f.SetItem(54, 97, 19);
            if (mutation == 3)
            {
                f.Random.Next();
                before = f.Random.Clone();
            }
            Submit(f, second);
            Assert.Equal(active, f.Store.ActiveCount);
            Assert.Equal(0, f.Authority.PromotedClientProjectileSpawns);
            Assert.Equal(mutation == 2 ? 19 : 20, f.Ammo());
            Assert.Equal(0, f.Outbound.QueuedFrames);
            Assert.True(f.Random.HasSameState(before));
            Assert.True(f.Authority.RejectedClientProjectileProvenance > 0);
        }
    }

    [Fact]
    public void Weapon_switch_uses_the_last_accepted_source_item_time()
    {
        using var source = Read("BulletPrefixedShootLinux1458");
        var rows = source.RootElement.GetProperty("profiles").EnumerateArray().ToArray();
        var slow = rows.Single(x => I(x, "weapon") == 219 && I(x, "prefix") == 0);
        var fast = rows.Single(x => I(x, "weapon") == 98 && I(x, "prefix") == 0);
        int slowTime = I(slow.GetProperty("weaponStats"), "useTime");
        int fastTime = I(fast.GetProperty("weaponStats"), "useTime");
        Assert.Equal(14, slowTime);
        Assert.Equal(8, fastTime);
        foreach (bool startsSlow in new[] { true, false })
        {
            var first = startsSlow ? slow : fast;
            var second = startsSlow ? fast : slow;
            using var f = Create(first);
            Submit(f, Assert.Single(Packets(first)));
            Assert.Equal(1, f.Authority.PromotedClientProjectileSpawns);
            f.SetItem(0, checked((short)I(second, "weapon")), 1);
            var report = Assert.Single(Packets(second)) with { Key = new(0, 702, 1) };
            if (startsSlow)
            {
                f.Tick = 100 + fastTime;
                var before = f.Random.Clone();
                short ammo = f.Ammo();
                Submit(f, report);
                Assert.Equal(1, f.Authority.PromotedClientProjectileSpawns);
                Assert.Equal(ammo, f.Ammo());
                Assert.True(f.Random.HasSameState(before));
            }
            f.Tick = 100 + (startsSlow ? slowTime : fastTime);
            Submit(f, report);
            Assert.Equal(2, f.Authority.PromotedClientProjectileSpawns);
            Assert.Equal(18, f.Ammo());
            Assert.True(f.Replication.WireIdentities.TryResolve(report.Key, out var handle));
            Assert.True(f.Store.IsCombatTrusted(handle));
        }
    }

    [Fact]
    public void Indistinguishable_complete_profiles_preserve_the_shorter_source_period()
    {
        using var linuxSource = Read("BulletPrefixedShootLinux1458");
        using var windowsSource = Read("BulletPrefixedShootWindows1458");
        var linux = linuxSource.RootElement.GetProperty("profiles").EnumerateArray().ToArray();
        var windows = windowsSource.RootElement.GetProperty("profiles").EnumerateArray().ToArray();
        var neutral = linux.Single(x => I(x, "weapon") == 219 && I(x, "prefix") == 0);
        int profiles = 0;
        foreach (var row in windows.Where(x => I(x, "weapon") == 4703))
        {
            var other = linux.Single(x => I(x, "weapon") == 4703 && I(x, "prefix") == I(row, "prefix"));
            int windowsTime = I(row.GetProperty("weaponStats"), "useTime");
            int linuxTime = I(other.GetProperty("weaponStats"), "useTime");
            if (windowsTime == linuxTime) continue;
            using var f = Create(row);
            var packets = Packets(row);
            foreach (var packet in packets) Submit(f, packet);
            Assert.Equal(packets.Length, f.Authority.PromotedClientProjectileSpawns);
            EqualRandom(row.GetProperty("after"), f.Random);
            f.SetItem(0, 219, 1);
            f.Tick = 100 + Math.Min(windowsTime, linuxTime);
            var before = f.Random.Clone();
            var next = Assert.Single(Packets(neutral)) with { Key = new(0, 901, 1) };
            Submit(f, next);
            Assert.True(packets.Length + 1 == f.Authority.PromotedClientProjectileSpawns,
                $"prefix={I(row, "prefix")}, Windows={windowsTime}, Linux={linuxTime}, promoted={f.Authority.PromotedClientProjectileSpawns}, rejects={f.Authority.RejectedClientProjectileProvenance}");
            Assert.Equal(I(row.GetProperty("ammoAfter"), "stack") - 1, f.Ammo());
            Assert.True(f.Random.HasSameState(before));
            profiles++;
        }
        Assert.Equal(7, profiles);
    }

    private static ClientBulletVolley1458Tests.Fixture Create(JsonElement row) =>
        new(checked((short)I(row, "weapon")), "one-free", checked((byte)I(row, "prefix")), 97, 20, I(row, "seed"));
    private static void Submit(ClientBulletVolley1458Tests.Fixture f, TerrariaProjectileUpdateState packet) =>
        Assert.True(f.Authority.TryApply(new ClientProjectileUpdateRuntimeCommand(f.Connection, packet)));
    private static TerrariaProjectileUpdateState[] Packets(JsonElement row) => row.GetProperty("shots").EnumerateArray().Select((shot, index) =>
        new TerrariaProjectileUpdateState(new(0, checked((ushort)(701 + index)), 1), checked((short)I(shot, "type")),
            F(shot, "position", "X"), F(shot, "position", "Y"), F(shot, "velocity", "X"), F(shot, "velocity", "Y"),
            0, 0, 0, 0, checked((short)I(shot, "damage")), shot.GetProperty("knockBack").GetSingle(), 0)).ToArray();
    private static void EqualBody(TerrariaProjectileUpdateState source, TerrariaProjectileUpdateState actual)
    {
        Assert.Equal(source.Key, actual.Key);
        Assert.Equal(source.ProjectileType, actual.ProjectileType);
        Assert.Equal(source.Damage, actual.Damage);
        Assert.Equal(source.OriginalDamage, actual.OriginalDamage);
        Assert.Equal(source.PositionX, actual.PositionX);
        Assert.Equal(source.PositionY, actual.PositionY);
        Assert.Equal(source.KnockBack, actual.KnockBack);
        // The established packet27 aim reconstruction owns a source representation interval;
        // it cannot identify which client arithmetic produced otherwise indistinguishable ULPs.
        Assert.InRange(MathF.Abs(source.VelocityX - actual.VelocityX), 0f, 0.0005f);
        Assert.InRange(MathF.Abs(source.VelocityY - actual.VelocityY), 0f, 0.0005f);
        Assert.Equal(source.Ai0, actual.Ai0);
        Assert.Equal(source.Ai1, actual.Ai1);
        Assert.Equal(source.Ai2, actual.Ai2);
    }
    private static void EqualRandom(JsonElement source, VanillaUnifiedRandom1458 random)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Assert.Equal(source.GetProperty("state").EnumerateArray().Select(x => x.GetInt32()),
            (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(random)!);
        Assert.Equal(source.GetProperty("cursor").GetUInt32(), (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.GetValue(random)!);
    }
    private static int I(JsonElement value, string name) => value.GetProperty(name).GetInt32();
    private static float F(JsonElement value, string parent, string name) => value.GetProperty(parent).GetProperty(name).GetSingle();
    private static JsonDocument Read(string resource)
    {
        using var stream = typeof(BulletSourceCandidateAuthority1458Tests).Assembly.GetManifestResourceStream(resource)!;
        Assert.NotNull(stream);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
}
