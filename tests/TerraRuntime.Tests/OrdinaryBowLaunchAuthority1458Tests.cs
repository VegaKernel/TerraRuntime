using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class OrdinaryBowLaunchAuthority1458Tests
{
    [Fact]
    public void Both_original_platforms_complete_66_neutral_bow_uses_with_source_body_ammo_rng_and_encoded_27()
    {
        int cases = 0;
        foreach (string resource in new[] { "OrdinaryBowLaunch1458", "OrdinaryBowGeometry1458" })
        {
            using var source = Read(resource);
            foreach (string platform in new[] { "linux", "windows" })
            {
                Assert.Equal(resource == "OrdinaryBowLaunch1458" ? 18 : 15, source.RootElement.GetProperty(platform).GetArrayLength());
                foreach (JsonElement row in source.RootElement.GetProperty(platform).EnumerateArray())
                {
                    using var f = Create(row);
                    TerrariaProjectileUpdateState packet = Packet(row);
                    string originalHex = Assert.Single(row.GetProperty("encoded27").EnumerateArray()).GetProperty("hex").GetString()!;
                    Assert.True(TerrariaProjectileEncoder.TryEncodeUpdate(in packet, out byte[] encoded));
                    Assert.Equal(originalHex, Convert.ToHexString(encoded));
                    EqualRandom(row.GetProperty("before"), f.Random);
                    EqualInventory(row.GetProperty("beforeInventory"), f);
                    Submit(f, in packet);
                    Assert.Equal(1, f.Authority.PromotedClientProjectileSpawns);
                    Assert.Equal(I(row.GetProperty("ammoAfter"), "stack"), f.Ammo());
                    EqualInventory(row.GetProperty("afterInventory"), f);
                    EqualRandom(row.GetProperty("after"), f.Random);
                    Assert.Equal(I(row, "nextAfter"), f.Random.Clone().Next());
                    Assert.True(f.Replication.WireIdentities.TryResolve(packet.Key, out ProjectileHandle handle));
                    Assert.True(f.Store.TryGet(handle, out ProjectileSnapshot retained));
                    Assert.True(f.Store.IsCombatTrusted(handle));
                    EqualBody(packet, in retained);
                    Assert.True(f.Store.TryGetLifecycle(handle, out var lifecycle));
                    Assert.Equal(I(Shot(row), "timeLeft"), lifecycle.TimeLeft);
                    Assert.Equal(originalHex, Assert.Single(Drain(f)));
                    cases++;
                }
            }
        }
        Assert.Equal(66, cases);
    }

    [Fact]
    public void All_700_original_rollable_bow_prefix_uses_keep_source_damage_wire_rng_and_compatible_period()
    {
        using var source = Read("OrdinaryBowPrefixedShoot1458");
        int cases = 0;
        foreach (string platform in new[] { "linux", "windows" })
        {
            var rows = source.RootElement.GetProperty(platform);
            Assert.Equal(350, rows.GetArrayLength());
            foreach (JsonElement row in rows.EnumerateArray())
            {
                Assert.True(row.GetProperty("rollable").GetBoolean());
                Assert.Equal(I(row, "requestedPrefix"), I(row, "retainedPrefix"));
                Assert.Equal(I(row, "prefix") != 0, row.GetProperty("prefixReturned").GetBoolean());
                using var f = Create(row);
                TerrariaProjectileUpdateState packet = Packet(row);
                string originalHex = Assert.Single(row.GetProperty("encoded27").EnumerateArray()).GetProperty("hex").GetString()!;
                Assert.True(TerrariaProjectileEncoder.TryEncodeUpdate(in packet, out byte[] encoded));
                Assert.Equal(originalHex, Convert.ToHexString(encoded));
                EqualRandom(row.GetProperty("before"), f.Random);
                EqualInventory(row.GetProperty("beforeInventory"), f);
                Submit(f, in packet);
                Assert.True(f.Authority.PromotedClientProjectileSpawns == 1,
                    $"platform={platform}, weapon={I(row, "weapon")}, prefix={I(row, "prefix")}, direction={I(row, "aimDirection")}");
                EqualInventory(row.GetProperty("afterInventory"), f);
                EqualRandom(row.GetProperty("after"), f.Random);
                Assert.Equal(I(row, "nextAfter"), f.Random.Clone().Next());
                Assert.True(f.Replication.WireIdentities.TryResolve(packet.Key, out var handle));
                Assert.True(f.Store.TryGet(handle, out var accepted));
                Assert.True(f.Store.IsCombatTrusted(handle));
                EqualBody(packet, in accepted);
                Assert.Equal(originalHex, Assert.Single(Drain(f)));

                // Packet27 has no client OS marker. This source identity has identical body on both platforms
                // but a 24/23 period, so both source candidates remain possible and the shorter period wins.
                int period = I(row.GetProperty("weaponStats"), "useTime");
                if (I(row, "weapon") == 3480 && I(row, "prefix") == 46)
                {
                    Assert.Equal(platform == "linux" ? 24 : 23, period);
                    period = 23;
                }
                var next = packet with { Key = new(0, 702, 1) };
                f.Tick = 100 + period - 1;
                Submit(f, in next);
                Assert.Equal(1, f.Authority.PromotedClientProjectileSpawns);
                Assert.Equal(19, f.Ammo());
                Assert.Equal(0, f.Outbound.QueuedFrames);
                EqualRandom(row.GetProperty("after"), f.Random);
                f.Tick++;
                Submit(f, in next);
                Assert.Equal(2, f.Authority.PromotedClientProjectileSpawns);
                Assert.Equal(18, f.Ammo());
                EqualRandom(row.GetProperty("after"), f.Random);
                cases++;
            }
        }
        Assert.Equal(700, cases);
    }

    [Fact]
    public void Original_logical_publications_and_runtime_first_observer_see_committed_ammo_body_and_rng()
    {
        using var source = Read();
        var rows = source.RootElement.GetProperty("linuxLogicalPublication");
        Assert.Equal(6, rows.GetArrayLength());
        foreach (JsonElement row in rows.EnumerateArray())
        {
            // These are actual configured client-owner publications, unlike the separate encoder in the first Fact.
            // Runtime owns projectile/item acceptance; it does not recreate client controls13/rotation41/sync138.
            Assert.Equal(new[] { 13, 41, 27, 13 }, FrameIds(row.GetProperty("shootFrames")));
            Assert.Equal(new[] { 5, 138 }, FrameIds(row.GetProperty("syncFrames")));
            foreach (JsonElement publication in row.GetProperty("publications").EnumerateArray())
            {
                Assert.Equal(19, I(publication, "ammoStack"));
                Assert.Equal(40, I(publication, "ammoType"));
                Assert.Equal(row.GetProperty("afterShoot").GetProperty("state").ToString(),
                    publication.GetProperty("random").GetProperty("state").ToString());
                Assert.Equal(I(row.GetProperty("afterShoot"), "cursor"), I(publication.GetProperty("random"), "cursor"));
            }
            using var f = Create(row);
            TerrariaProjectileUpdateState packet = Packet(row);
            int observations = 0;
            f.Events.OnEquipment = () =>
            {
                observations++;
                Assert.Equal(19, f.Ammo());
                EqualRandom(row.GetProperty("after"), f.Random);
                Assert.True(f.Store.TryGetActive(50, out var accepted));
                Assert.True(f.Store.IsCombatTrusted(accepted.Handle));
                EqualBody(packet, in accepted);
                Assert.Equal(1, f.Authority.PromotedClientProjectileSpawns);
                Assert.Equal(0, f.Outbound.QueuedFrames);
            };
            Submit(f, in packet);
            Assert.Equal(1, observations);
            Assert.Single(Drain(f));
        }
    }

    [Fact]
    public void Exact_source_origin_damage_cadence_and_currentness_refuse_without_partial_adoption()
    {
        using var source = Read();
        JsonElement row = source.RootElement.GetProperty("linux").EnumerateArray()
            .First(x => I(x, "weapon") == 39 && I(x, "aimDirection") == 1);
        TerrariaProjectileUpdateState packet = Packet(row);
        foreach (int mutation in new[] { 0, 1, 2, 3, 4, 5 })
        {
            using var f = Create(row);
            var report = mutation switch
            {
                0 => packet with { PositionX = packet.PositionX + 16f }, // Inside the previous generic 192px allowance.
                1 => packet with { Damage = checked((short)(packet.Damage + 1)) },
                2 => packet with { VelocityX = packet.VelocityX + 1f },
                3 => packet with { ProjectileType = 2 },
                4 => packet with { BannerIdToRespondTo = 99 },
                _ => packet with { Ai0 = 0.0001f }
            };
            var before = Copy(f.Store);
            var random = f.Random.Clone();
            Submit(f, in report);
            Assert.Equal(0, f.Authority.PromotedClientProjectileSpawns);
            Assert.Equal(20, f.Ammo());
            Assert.Equal(before, Copy(f.Store));
            Assert.True(f.Random.HasSameState(random));
            Assert.Equal(0, f.Outbound.QueuedFrames);
        }

        using var cadence = Create(row);
        Submit(cadence, in packet);
        var next = packet with { Key = new(0, 702, 1) };
        int useTime = I(row.GetProperty("weaponStats"), "useTime");
        cadence.Tick = 100 + useTime - 1;
        Submit(cadence, in next);
        Assert.Equal(1, cadence.Authority.PromotedClientProjectileSpawns);
        Assert.Equal(19, cadence.Ammo());
        cadence.Tick++;
        Submit(cadence, in next);
        Assert.Equal(2, cadence.Authority.PromotedClientProjectileSpawns);
        Assert.Equal(18, cadence.Ammo());
        EqualRandom(row.GetProperty("after"), cadence.Random);

        foreach (int mutation in new[] { 0, 1, 2 })
        {
            using var late = Create(row);
            int callbacks = 0;
            var before = Copy(late.Store);
            var random = late.Random.Clone();
            var authority = new ProjectileAuthority(late.Store, late.Players, new RuntimeNpcStore(),
                new RuntimePlayerSnapshotLookup(late.Players, null), null, late.Replication, () =>
                {
                    if (callbacks++ == 0)
                    {
                        if (mutation == 0) late.SetItem(54, 40, 19);
                        else if (mutation == 1) late.Random.Next();
                        else
                        {
                            Assert.True(late.Players.TryGet(late.Connection, out var member));
                            member.PhysicsPhase = member.PhysicsPhase!.Value with { GravityDirection = -1f };
                        }
                    }
                    return late.Tick;
                }, projectileRandom: late.Random);
            Assert.True(authority.TryApply(new ClientProjectileUpdateRuntimeCommand(late.Connection, packet)));
            Assert.True(callbacks > 0);
            Assert.Equal(0, authority.PromotedClientProjectileSpawns);
            Assert.Equal(mutation == 0 ? 19 : 20, late.Ammo());
            Assert.Equal(before, Copy(late.Store));
            if (mutation == 1) random.Next();
            Assert.True(late.Random.HasSameState(random));
            Assert.Equal(0, late.Outbound.QueuedFrames);
        }
    }

    private static ClientBulletVolley1458Tests.Fixture Create(JsonElement row)
    {
        var fixture = new ClientBulletVolley1458Tests.Fixture(checked((short)I(row, "weapon")),
            "one-free", prefix: checked((byte)I(row, "prefix")), ammo: 40, seed: I(row, "seed"));
        // These source components have normal gravity and no mount. Keep old gun fixture inputs unchanged.
        Assert.True(fixture.Players.TryApply(new PlayerMovementRuntimeCommand(fixture.Connection,
            new PlayerMovementCommitRequest(fixture.Connection.Player.Slot,
                I(row, "direction") > 0 ? (byte)64 : (byte)0, 16, 0, 0, 0, 400f, 400f,
                false, 0f, 0f, false, 0, false, 0f, 0f, 0f, 0f, false, 0f, 0f))));
        return fixture;
    }

    private static void EqualInventory(JsonElement source, ClientBulletVolley1458Tests.Fixture fixture)
    {
        Assert.Equal(VanillaPlayerItemSlotCatalog.OrdinaryInventoryCount, source.GetArrayLength());
        for (short slot = 0; slot < VanillaPlayerItemSlotCatalog.OrdinaryInventoryCount; slot++)
        {
            Assert.True(fixture.Players.TryGetInventoryItem(fixture.Connection, slot, out var item));
            Assert.Equal(I(source[slot], "type"), item.ItemType.Value);
            Assert.Equal(I(source[slot], "prefix"), item.Prefix.Value);
            Assert.Equal(I(source[slot], "stack"), item.Stack);
        }
    }

    private static JsonElement Shot(JsonElement row) => Assert.Single(row.GetProperty("shots").EnumerateArray());

    private static TerrariaProjectileUpdateState Packet(JsonElement row)
    {
        JsonElement shot = Shot(row);
        JsonElement key = shot.GetProperty("key");
        return new(new(checked((byte)I(key, "Spawner")), checked((ushort)I(key, "Index")), checked((ushort)I(key, "Generation"))),
            checked((short)I(shot, "type")), F(shot, "position", "X"), F(shot, "position", "Y"),
            F(shot, "velocity", "X"), F(shot, "velocity", "Y"), 0, 0, 0, 0,
            checked((short)I(shot, "damage")), shot.GetProperty("knockBack").GetSingle(), 0);
    }

    private static void Submit(ClientBulletVolley1458Tests.Fixture f, in TerrariaProjectileUpdateState packet) =>
        Assert.True(f.Authority.TryApply(new ClientProjectileUpdateRuntimeCommand(f.Connection, packet)));

    private static void EqualBody(TerrariaProjectileUpdateState packet, in ProjectileSnapshot actual)
    {
        Assert.Equal(packet.ProjectileType, actual.Type.Value);
        Assert.Equal(packet.Key.Spawner, actual.Spawner);
        Assert.Equal(packet.PositionX, actual.PositionX);
        Assert.Equal(packet.PositionY, actual.PositionY);
        Assert.Equal(packet.VelocityX, actual.VelocityX);
        Assert.Equal(packet.VelocityY, actual.VelocityY);
        Assert.Equal(packet.Damage, actual.Damage);
        Assert.Equal(packet.KnockBack, actual.KnockBack);
        Assert.Equal(packet.OriginalDamage, actual.OriginalDamage);
        Assert.Equal(new ProjectileAiState(packet.Ai0, packet.Ai1, packet.Ai2), actual.Ai);
    }

    private static ProjectileSnapshot[] Copy(RuntimeProjectileStore store)
    {
        var states = new ProjectileSnapshot[store.Capacity];
        int count = store.CopyActive(states);
        return states[..count];
    }

    private static string[] Drain(ClientBulletVolley1458Tests.Fixture f)
    {
        var queue = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
            .GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Outbound)!;
        var frames = new List<string>();
        while (queue.TryRead(out var frame)) frames.Add(Convert.ToHexString(frame.Bytes.Span));
        return frames.ToArray();
    }

    private static int[] FrameIds(JsonElement frames) => frames.EnumerateArray()
        .Select(x => (int)Convert.FromHexString(x.GetString()!)[2]).ToArray();

    private static void EqualRandom(JsonElement source, VanillaUnifiedRandom1458 random)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Assert.Equal(source.GetProperty("state").EnumerateArray().Select(x => x.GetInt32()),
            (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(random)!);
        Assert.Equal(source.GetProperty("cursor").GetUInt32(),
            (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.GetValue(random)!);
    }

    private static int I(JsonElement value, string name) => value.GetProperty(name).GetInt32();
    private static float F(JsonElement value, string parent, string name) => value.GetProperty(parent).GetProperty(name).GetSingle();

    private static JsonDocument Read(string resource = "OrdinaryBowLaunch1458")
    {
        using var stream = typeof(OrdinaryBowLaunchAuthority1458Tests).Assembly.GetManifestResourceStream(resource)!;
        Assert.NotNull(stream);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
}
