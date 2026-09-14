using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Tests;

public sealed class MoonLeechTests
{
    private static readonly JsonElement[] Rows = ReadCases();
    public static TheoryData<int> Cases => new(Enumerable.Range(0, 160));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Movement_return_and_contact_state_match_original(int index)
    {
        JsonElement row = Rows[index];
        int mode = row.GetProperty("mode").GetInt32();
        var projectile = new ProjectileSnapshot(default, default, VanillaProjectileIds.MoonLeech, 0,
            row.GetProperty("px").GetSingle(), row.GetProperty("py").GetSingle(), 2, -3,
            new ProjectileAiState(row.GetProperty("sign").GetSingle(), 0, 0), 0, 0, 0, 0);
        var context = new VanillaProjectileBehaviorContext(false, 0, 0,
            PlayerSnapshots: new PlayerLookup(mode), ExpertMode: row.GetProperty("expert").GetBoolean(),
            NpcTargets: new HeadLookup(mode != 5),
            LocalAi: new ProjectileLocalAiState(row.GetProperty("age").GetSingle(), 0, 0));
        Assert.True(VanillaDefinitionCatalog.TryGet(projectile.Type, out var definition));
        Assert.True(VanillaProjectileBehaviorStepper.TryStep(in projectile, in definition, in context, out var actual));
        Assert.Equal(row.GetProperty("vx").GetSingle(), actual.VelocityX);
        Assert.Equal(row.GetProperty("vy").GetSingle(), actual.VelocityY);
        Assert.Equal(row.GetProperty("ai")[0].GetSingle(), actual.Ai0);
        Assert.Equal(row.GetProperty("ai")[1].GetSingle(), actual.Ai1Override ?? projectile.Ai.Ai1);
        Assert.Equal(row.GetProperty("ai")[2].GetSingle(), actual.Ai2Override ?? projectile.Ai.Ai2);
        Assert.Equal(row.GetProperty("local").EnumerateArray().Select(value => value.GetSingle()),
            new[] { actual.LocalAiOverride!.Value.Ai0, actual.LocalAiOverride.Value.Ai1, actual.LocalAiOverride.Value.Ai2 });
        Assert.Equal(!row.GetProperty("active").GetBoolean(), actual.Kill);
        Assert.Equal(row.GetProperty("x").GetSingle(), actual.PositionXOverride ?? projectile.PositionX);
        Assert.Equal(row.GetProperty("y").GetSingle(), actual.PositionYOverride ?? projectile.PositionY);
        var buffs = new PlayerBuffState();
        if (actual.PlayerBuff is { } application)
        {
            Assert.Equal(new PlayerHandle(new PlayerSlotId(0), new PlayerSessionGeneration(1)), application.Target);
            Assert.Equal(VanillaBuffIds.MoonLeech, application.Type);
            buffs.TryApplyMoonLeech(application.DurationTicks, immune: mode == 9);
        }
        Assert.Equal(row.GetProperty("buff").GetInt32(), buffs.GetDuration(VanillaBuffIds.MoonLeech));
    }

    private static JsonElement[] ReadCases()
    {
        // Numeric outputs of the unmodified 1.4.5.8 Linux server's Projectile.AI on CoreCLR.
        // No outer movement or lifetime decrement is run in this independent probe.
        using Stream stream = typeof(MoonLeechTests).Assembly.GetManifestResourceStream("MoonLeech1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var bytes = new MemoryStream();
        gzip.CopyTo(bytes);
        Assert.Equal("460000cef7d10aa92dcd7491e7baaee790c5a3b7ec358419b4dbd23f55fd148b",
            Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        using JsonDocument json = JsonDocument.Parse(bytes.ToArray());
        return json.RootElement.EnumerateArray().Select(row => row.Clone()).ToArray();
    }

    private sealed class PlayerLookup(int mode) : IRuntimePlayerSlotSnapshotLookup
    {
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot)
        {
            snapshot = new PlayerStateSnapshot(
                new PlayerHandle(slot, new PlayerSessionGeneration(1)), new PlayerStateRevision(1),
                0, 0, 0, 0, 0, 0, 1200, 1000, 0, 0, 0, 0, 0, 0, 0, 0, 0)
            { IsDead = mode == 6, GodMode = mode == 8 };
            return slot.Value == 0 && mode != 7;
        }
    }

    private sealed class HeadLookup(bool active) : IVanillaProjectileNpcTargetResolver
    {
        public bool TryFindClosestTargetWithLineOfSight(in ProjectileSnapshot projectile,
            in VanillaProjectileDefinition definition, float range, out int slot, out float x, out float y)
        { slot = -1; x = y = 0; return false; }
        public bool TryGetChaseableTargetCenter(int slot, out float x, out float y)
        { x = y = 0; return false; }
        public bool IsNpcSlotAddressable(int slot) => (uint)slot < 200;
        public bool TryGetActiveNpc(int slot, out NpcSnapshot npc)
        {
            npc = new NpcSnapshot(new NpcHandle(0, new NpcGeneration(1)), new NpcRevision(1),
                VanillaNpcIds.MoonLordHead.Value, checked((short)VanillaNpcIds.MoonLordHead.Value),
                1000, 1000, 0, 0, 0, default,
                new NpcSimulationState(0, 0, 0, 0, false, false, false, false, false, 1));
            return slot == 0 && active;
        }
    }
}
