using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;

namespace TerraRuntime.Tests;

public sealed class GroundCritterPresentation1458Tests
{
    private static readonly JsonElement[] Rows = Read();

    public static IEnumerable<object[]> Cases() => Rows.Select(row => new object[] {
        row.GetProperty("index").GetInt32(), row.GetProperty("type").GetInt32(), row.GetProperty("scene").GetString()! });

    [Theory]
    [MemberData(nameof(Cases))]
    public void Actual_source_frame_counter_and_sprite_match_without_changing_body_or_motion(int index, int type, string scene)
    {
        JsonElement row = Rows[index], input = row.GetProperty("input"), after = row.GetProperty("after");
        Assert.Equal(type, input.GetProperty("type").GetInt32());
        Assert.Equal(scene, row.GetProperty("scene").GetString());
        NpcStateUpdate physics = State(input);
        Assert.True(VanillaGroundCritterPresentation1458.TryFinish(in physics, out var actual));
        Assert.Equal(after.GetProperty("frameIndex").GetInt32(), actual.Simulation.FrameIndex);
        Assert.Equal(after.GetProperty("frameCounter").GetString(),
            BitConverter.DoubleToUInt64Bits(actual.Simulation.FrameCounter).ToString("x16", CultureInfo.InvariantCulture));
        Assert.Equal(after.GetProperty("spriteDirection").GetInt32(), actual.Simulation.SpriteDirection);
        // Every other represented runtime field must remain exactly the supplied physical state.
        Assert.Equal(physics with { Simulation = physics.Simulation with {
            FrameIndex = after.GetProperty("frameIndex").GetInt32(),
            FrameCounter = Double(after, "frameCounter"),
            SpriteDirection = after.GetProperty("spriteDirection").GetInt32() } }, actual);

        // Independent source receipt includes the physical 56-slot RNG and cursor,
        // and all captured NPC fields, rather than a next-value-only comparison.
        var beforeRandom = row.GetProperty("rngBefore");
        var afterRandom = row.GetProperty("rngAfter");
        Assert.Equal(beforeRandom.GetProperty("cursor").GetUInt32(), afterRandom.GetProperty("cursor").GetUInt32());
        int[] slots = beforeRandom.GetProperty("slots").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        Assert.Equal(56, slots.Length);
        Assert.Equal(slots, afterRandom.GetProperty("slots").EnumerateArray().Select(x => x.GetInt32()).ToArray());
        foreach (var property in input.EnumerateObject())
            if (property.Name is not ("frameIndex" or "frameCounter" or "spriteDirection"))
                Assert.Equal(property.Value.GetRawText(), after.GetProperty(property.Name).GetRawText());
    }

    private static NpcStateUpdate State(JsonElement value) => new(
        value.GetProperty("type").GetInt32(), value.GetProperty("netId").GetInt16(),
        Float(value, "position", 0), Float(value, "position", 1),
        Float(value, "velocity", 0), Float(value, "velocity", 1),
        value.GetProperty("target").GetUInt16(), Ai(value, "ai"),
        NpcSimulationState.Initial with {
            AiStyleOverride = value.GetProperty("aiStyle").GetInt32() == 3 ? new(3) : null,
            DirectionX = value.GetProperty("direction").GetInt32(),
            DirectionY = value.GetProperty("directionY").GetInt32(),
            SpriteDirection = value.GetProperty("spriteDirection").GetInt32(),
            FrameIndex = value.GetProperty("frameIndex").GetInt32(), FrameCounter = Double(value, "frameCounter"),
            Scale = Float(value, "scale"), LocalAi = Ai(value, "localAi"),
            OldPositionX = Float(value, "oldPosition", 0), OldPositionY = Float(value, "oldPosition", 1),
            OldVelocityX = Float(value, "oldVelocity", 0), OldVelocityY = Float(value, "oldVelocity", 1),
            HitboxOverride = new(value.GetProperty("width").GetInt32(), value.GetProperty("height").GetInt32()),
            Life = value.GetProperty("life").GetInt32(), LifeMax = value.GetProperty("lifeMax").GetInt32(),
            BaseLifeMax = value.GetProperty("baseLifeMax").GetInt32(), TimeLeft = value.GetProperty("timeLeft").GetInt32(),
            HomeTileX = value.GetProperty("homeX").GetInt32(), HomeTileY = value.GetProperty("homeY").GetInt32(),
            Homeless = value.GetProperty("homeless").GetBoolean(), Friendly = value.GetProperty("friendly").GetBoolean(),
            CatchableNpcTempImmunityCounter = value.GetProperty("immunity").GetInt32(), ReleaseOwner = value.GetProperty("release").GetInt16(),
            DefenseOverride = value.GetProperty("defense").GetInt32(), DamageOverride = value.GetProperty("damage").GetInt32(),
            BaseDefense = value.GetProperty("baseDefense").GetInt32(), BaseDamage = value.GetProperty("baseDamage").GetInt32(),
            Wet = value.GetProperty("wet").GetBoolean(), CollideX = value.GetProperty("collideX").GetBoolean(),
            CollideY = value.GetProperty("collideY").GetBoolean(), JustHit = value.GetProperty("justHit").GetBoolean(),
            Breath = value.GetProperty("breath").GetInt32(), FriendlyRegenerationCounter = value.GetProperty("friendlyRegen").GetInt32(),
            LifeRegenCounter = value.GetProperty("lifeRegen").GetInt32(), NoGravity = value.GetProperty("noGravity").GetBoolean(),
            NoTileCollide = value.GetProperty("noTileCollide").GetBoolean(), DontTakeDamage = value.GetProperty("dontTakeDamage").GetBoolean(),
            Immortal = value.GetProperty("immortal").GetBoolean(), Chaseable = value.GetProperty("chaseable").GetBoolean(),
            MoneyValue = Float(value, "money"), ShimmerTransparency = Float(value, "shimmer"),
            StairFall = value.GetProperty("stairFall").GetBoolean(), TownNpc = value.GetProperty("townNpc").GetBoolean(),
            Alpha = value.GetProperty("alpha").GetInt32(), Rotation = Float(value, "rotation"), KnockBackResist = Float(value, "knockBack")
        });

    private static NpcAiState Ai(JsonElement value, string name) =>
        new(Float(value, name, 0), Float(value, name, 1), Float(value, name, 2), Float(value, name, 3));
    private static float Float(JsonElement value, string name, int? index = null) => BitConverter.UInt32BitsToSingle(
        uint.Parse((index is int i ? value.GetProperty(name)[i] : value.GetProperty(name)).GetString()!, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    private static double Double(JsonElement value, string name) => BitConverter.UInt64BitsToDouble(
        ulong.Parse(value.GetProperty(name).GetString()!, NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    private static JsonElement[] Read()
    {
        using var resource = typeof(GroundCritterPresentation1458Tests).Assembly.GetManifestResourceStream("GroundCritterFindFrameLinux1458")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var bytes = new MemoryStream(); gzip.CopyTo(bytes);
        Assert.Equal("df8b6bb9f29f3cbdec0e6f981c14476e057efb68b6d8cc1ad800ee1c9ff84ebc",
            Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        using var document = JsonDocument.Parse(bytes.ToArray());
        var rows = document.RootElement.GetProperty("cases").EnumerateArray().Select(row => row.Clone()).ToArray();
        Assert.Equal(542, rows.Length);
        Assert.Equal(34, rows.Select(row => row.GetProperty("type").GetInt32()).Distinct().Count());
        return rows;
    }
}
