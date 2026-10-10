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

public sealed class OrdinaryExtraArrowLaunch1458Tests
{
    [Fact]
    public void Frozen_2800_other_arrow_launches_preserve_raw_body_stock_rng_and_period()
    {
        using var source = Read("OrdinaryExtraArrowLaunch1458");
        int cases = 0;
        var failures = new List<string>();
        foreach (string platform in new[] { "linux", "windows" })
        {
            Assert.Equal(1400, source.RootElement.GetProperty(platform).GetArrayLength());
            foreach (JsonElement row in source.RootElement.GetProperty(platform).EnumerateArray())
            {
                cases++;
                Exception? error = Record.Exception(() => Replay(row, platform));
                if (error is not null)
                    failures.Add($"{platform}: weapon={I(row, "weapon")}, prefix={I(row, "prefix")}, ammo={I(row, "ammo")}, direction={I(row, "aimDirection")}: {error.Message}");
            }
        }
        Assert.Equal(2800, cases);
        Assert.True(failures.Count == 0, $"{failures.Count} mismatched rows; first: {failures.FirstOrDefault()}");
    }
    private static void Replay(JsonElement row, string platform)
    {
        Assert.True(row.GetProperty("rollable").GetBoolean());
        Assert.Equal(I(row, "requestedPrefix"), I(row, "retainedPrefix"));
        using var f = Create(row);
        var packet = Packet(row);
        string original = Assert.Single(row.GetProperty("encoded27").EnumerateArray()).GetProperty("hex").GetString()!;
        Assert.True(TerrariaProjectileEncoder.TryEncodeUpdate(in packet, out byte[] encoded));
        Assert.Equal(original, Convert.ToHexString(encoded));
        EqualInventory(row.GetProperty("beforeInventory"), f);
        EqualRandom(row.GetProperty("before"), f.Random);
        int observations = 0;
        f.Events.OnEquipment = () =>
        {
            observations++;
            EqualInventory(row.GetProperty("afterInventory"), f);
            EqualRandom(row.GetProperty("after"), f.Random);
            Assert.True(f.Store.TryGetActive(50, out var born));
            Assert.True(f.Store.IsCombatTrusted(born.Handle));
            EqualBody(packet, in born);
            Assert.Equal(1, f.Authority.PromotedClientProjectileSpawns);
            Assert.Equal(0, f.Outbound.QueuedFrames);
        };
        Submit(f, in packet);
        Assert.Equal(1, f.Authority.PromotedClientProjectileSpawns);
        Assert.Equal(1, observations);
        EqualInventory(row.GetProperty("afterInventory"), f);
        EqualRandom(row.GetProperty("after"), f.Random);
        Assert.Equal(I(row, "nextAfter"), f.Random.Clone().Next());
        Assert.True(f.Replication.WireIdentities.TryResolve(packet.Key, out var handle));
        Assert.True(f.Store.TryGet(handle, out var accepted));
        Assert.True(f.Store.IsCombatTrusted(handle));
        EqualBody(packet, in accepted);
        Assert.True(f.Store.TryGetLifecycle(handle, out var life));
        Assert.Equal(I(Shot(row), "timeLeft"), life.TimeLeft);
        Assert.Equal(original, Assert.Single(Drain(f)));
        int period = I(row.GetProperty("weaponStats"), "useTime");
        if (I(row, "weapon") == 3480 && I(row, "prefix") == 46)
        {
            Assert.Equal(platform == "linux" ? 24 : 23, period);
            period = 23;
        }
        var next = packet with { Key = new(0, 702, 1) };
        f.Events.OnEquipment = null;
        f.Tick = 100 + period - 1;
        Submit(f, in next);
        Assert.Equal(1, f.Authority.PromotedClientProjectileSpawns);
        Assert.Equal(I(row.GetProperty("ammoAfter"), "stack"), f.Ammo());
        Assert.Equal(0, f.Outbound.QueuedFrames);
        EqualRandom(row.GetProperty("after"), f.Random);
        f.Tick++;
        Submit(f, in next);
        Assert.Equal(2, f.Authority.PromotedClientProjectileSpawns);
        Assert.Equal(I(row, "ammo") == 3103 ? 20 : 18, f.Ammo());
        EqualRandom(row.GetProperty("after"), f.Random);
    }
    private static ClientBulletVolley1458Tests.Fixture Create(JsonElement row)
    {
        var fixture = new ClientBulletVolley1458Tests.Fixture(checked((short)I(row, "weapon")),
            "one-free", prefix: checked((byte)I(row, "prefix")), ammo: checked((short)I(row, "ammo")), seed: I(row, "seed"));
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
        using var stream = typeof(OrdinaryExtraArrowLaunch1458Tests).Assembly.GetManifestResourceStream(resource)!;
        Assert.NotNull(stream);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
}
