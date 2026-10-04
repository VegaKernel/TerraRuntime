using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;
public sealed class FloatingEyeAi1458Tests
{
    // Actual original IdleSounds->NPC.AI, NPC.UpdateNPC and SetDefaults from pinned Linux1.4.5.8.
    // Assembly SHA2564b87890ac53d40f61db5f928693a379acf4ccbd8ed3b47eb32fb096f145df034.
    public static IEnumerable<object[]> WholeCases() => Read("FloatingEyeWholeAi1458");
    public static IEnumerable<object[]> DefaultCases() => Read("FloatingEyeDefaults1458");
    internal static IEnumerable<object[]> Read(string name)
    {
        using var stream = typeof(FloatingEyeAi1458Tests).Assembly.GetManifestResourceStream(name)!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray())
            yield return[row.Clone()];
    }

    [Theory]
    [MemberData(nameof(WholeCases))]
    public void Whole_original_AI_keeps_source_state_order_actual_bodies_and_shared_random(JsonElement row)
    {
        float F(string key) => row.GetProperty(key).GetSingle();
        int I(string key) => row.GetProperty(key).GetInt32();
        bool B(string key) => row.GetProperty(key).GetBoolean();
        var random = new SystemVanillaNpcRandom(I("seed"));
        int direction = F("vx") < 0f ? -1 : 1;
        int directionY = F("vy") < 0f ? -1 : 1;
        var target = new VanillaFlyingEyeTarget1458(0, F("px"), F("py"), I("pw"), I("ph"));
        var state = new VanillaFlyingEyeAi1458
        {
            Type = I("type"),
            X = 1000.75f,
            Y = 1000.25f,
            Width = I("width"),
            Height = I("height"),
            Vx = F("vx"),
            Vy = F("vy"),
            OldVx = F("vx") * 2f,
            OldVy = F("vy") * 2f,
            Direction = direction,
            DirectionY = directionY,
            OldDirection = direction,
            OldDirectionY = directionY,
            Target = 0,
            OldTarget = 0,
            Current = target,
            Closest = target,
            Life = I("life"),
            LifeMax = I("lifeMax"),
            TimeLeft = 750,
            Alpha = 7,
            Scale = F("scale"),
            Clock = F("clock"),
            Phase = F("phase"),
            CollideX = (I("collisionBits") & 1) != 0,
            CollideY = (I("collisionBits") & 2) != 0,
            NoTileCollide = (I("collisionBits") & 4) != 0,
            Wet = B("wet"),
            DayTime = B("day"),
            WorldSurfacePixels = 2240d,
            TargetInGraveyard = B("grave"),
            ShimmerTransparent = B("shimmer")
        };
        state.Step(new Environment(B("grave"), B("sight"), B("bodySolid")), random);
        Assert.Equal(F("outVx"), state.Vx);
        Assert.Equal(F("outVy"), state.Vy);
        Assert.Equal(row.GetProperty("ai")[0].GetSingle(), state.Clock);
        Assert.Equal(row.GetProperty("ai")[1].GetSingle(), state.Phase);
        Assert.Equal(I("direction"), state.Direction);
        Assert.Equal(I("directionY"), state.DirectionY);
        Assert.Equal(I("target"), state.Target);
        Assert.Equal(I("timeLeft"), state.TimeLeft);
        Assert.Equal(I("alpha"), state.Alpha);
        Assert.Equal(F("rotation"), state.Rotation);
        Assert.Equal(B("noTile"), state.NoTileCollide);
        Assert.Equal(B("outWet"), state.Wet);
        Assert.Equal(B("netUpdate"), state.Force);
        Assert.Equal(I("next"), random.SourceRandom.Next());
    }

    [Theory]
    [MemberData(nameof(DefaultCases))]
    public void Original_canonical_and_negative_eye_defaults_scale_before_variant_stats(JsonElement row)
    {
        var type = new NpcTypeId(row.GetProperty("type").GetInt32());
        var net = new NpcNetId((short)row.GetProperty("netId").GetInt32());
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(type, net, out var definition));
        bool good = row.GetProperty("good").GetBoolean();
        var context = new VanillaNpcSpawnContext(row.GetProperty("mode").GetInt32() + 1f + (good ? 1f : 0f), row.GetProperty("playerCount").GetInt32(), good)
        {
            HardMode = row.GetProperty("hard").GetBoolean(),
            DownedPlantera = row.GetProperty("plantera").GetBoolean()
        };
        Assert.True(VanillaNpcSpawnDefaults.TryResolve(in definition, in context, false, out var defaults));
        Assert.Equal(row.GetProperty("width").GetInt32(), defaults.Hitbox.Width);
        Assert.Equal(row.GetProperty("height").GetInt32(), defaults.Hitbox.Height);
        Assert.Equal(row.GetProperty("lifeMax").GetInt32(), defaults.LifeMax);
        Assert.Equal(row.GetProperty("damage").GetInt32(), defaults.Damage);
        Assert.Equal(row.GetProperty("defense").GetInt32(), defaults.Defense);
        Assert.Equal(row.GetProperty("scale").GetSingle(), defaults.Scale);
        Assert.Equal(row.GetProperty("knockback").GetSingle(), defaults.KnockBackResist);
        Assert.Equal(row.GetProperty("value").GetSingle(), defaults.VerifiedMoneyValue);
    }

    internal sealed class Environment(bool grave, bool sight, bool solid) : IVanillaFlyingEyeEnvironment
    {
        public bool IsGraveyardAt(float centerX, float centerY) => grave;
        public bool CanHit(float sourceX, float sourceY, int width, int height, float targetX, float targetY, int targetWidth, int targetHeight) => sight;
        public bool SolidCollision(float x, float y, int width, int height) => solid;
    }
}
