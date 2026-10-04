using System.IO.Compression;
using System.Collections.Concurrent;
using System.Text.Json;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaMothronWholeAi1458Tests
{
    private static readonly ConcurrentDictionary<bool, WorldTileStore> Worlds = new();
    // Original whole NPC.AI + IdleSounds, pinned Linux binary SHA256 4b87890ac53d40f61db5f928693a379acf4ccbd8ed3b47eb32fb096f145df034.
    // Stone type1 is genuinely solid in the independent bootstrap. NaN source outcomes remain exact here;
    // the authoritative planner separately rejects them before advancing the live stream.
    public static IEnumerable<object[]> Cases()
    {
        using var stream = typeof(VanillaMothronWholeAi1458Tests).Assembly.GetManifestResourceStream("MothronWholeAi1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.EnumerateArray()) yield return [row.Clone()];
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void Complete_source_AI_fields_peer_mutation_spawn_and_next_random_match(JsonElement row)
    {
        var tiles = Worlds.GetOrAdd(row.GetProperty("solid").GetBoolean(), static solid =>
        {
            var world = new WorldTileStore(new WorldDimensions(600, 500));
            for (int x = 0; x < 600; x++) world.Set(x, 80, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            for (int x = 60; x <= 70; x++)
                for (int y = 61; y <= 65; y++) world.Set(x, y, new WorldTile { Type = 1, Flags = solid ? WorldTileFlags.Active : 0 });
            return world;
        });
        int type = row.GetProperty("type").GetInt32();
        float F(string key) => Number(row.GetProperty(key));
        bool B(string key) => row.GetProperty(key).GetBoolean();
        int seed = row.GetProperty("seed").GetInt32();
        bool expert = B("expert");
        var random = new SystemVanillaNpcRandom(seed);
        var state = new VanillaMothronAiState1458
        {
            BaseDamage = type == 477 ? expert ? 160 : 80 : 0,
            HatchLifeMax = expert ? 1400 : 700,
            HatchDamage = expert ? 100 : 50,
            KnockbackMultiplier = expert ? .9f : 1f,
            Rotation = .2f,
            TimeLeft = 750,
            OldVx = F("vx"),
            OldVy = F("vy"),
            Type = type,
            X = F("x"),
            Y = F("y"),
            Vx = F("vx"),
            Vy = F("vy"),
            Width = row.GetProperty("width").GetInt32(),
            Height = row.GetProperty("height").GetInt32(),
            A0 = F("phase"),
            A1 = F("clock"),
            A2 = .5f,
            NoGravity = type != 478,
            CollideX = F("vx") < 0f,
            CollideY = F("vx") > 0f,
            Life = type == 477 ? expert ? 12000 : 6000 : type == 478 ? 100 : expert ? 1400 : 700,
            LifeMax = type == 477 ? expert ? 12000 : 6000 : type == 478 ? 200 : expert ? 1400 : 700,
            Damage = type == 477 ? expert ? 160 : 80 : type == 478 ? 0 : expert ? 100 : 50,
            Defense = type == 479 ? 14 : 30,
            Knockback = 1f
        };
        float playerX = state.X + state.Width * .5f + F("dx");
        float playerY = state.Y + state.Height * .5f + F("dy");
        state.FacingX = (int)(playerX - 10f) + 10;
        state.FacingY = (int)(playerY - 21f) + 21;
        float peerVx = B("nearPeer") ? 1f : 0f, peerVy = B("nearPeer") ? 2f : 0f;
        if (type == 477)
        {
            state.A1 = state.A0 is 4.1f or 4.2f ? 65f : state.A0 == 3.2f ? state.Vx < 0f ? -1f : 1f : state.A1;
            state.A2 = state.A0 is 4.1f or 4.2f ? 80f : .5f;
            state.A3 = state.A0 == 4.2f ? F("clock") : 0f;
            for (int x = 60; x <= 70; x++)
                for (int y = 61; y <= 65; y++)
                    tiles.Set(x, y, new WorldTile { Type = 1, Flags = B("solid") ? WorldTileFlags.Active : 0 });
            VanillaMothronParentAi1458.TryStep(state, expert, B("eclipse"), B("hit"), playerX, playerY,
                row.GetProperty("actorSlot").GetInt32(), new TestWorld(tiles), random, B("nearPeer") ? 1 : 0);
        }
        else if (type == 478)
            VanillaMothronYoungAi1458.StepEgg(state, expert, B("hit"), playerX, playerY, random);
        else
        {
            (float X, float Y)? repelled = null;
            if (B("nearPeer") && B("eclipse") && state.A0 is 0f or 1f)
            {
                var delta = VanillaMothronYoungAi1458.Normalize(10f, 10f, -.1f);
                repelled = (state.Vx + delta.X, state.Vy + delta.Y);
                peerVx -= delta.X;
                peerVy -= delta.Y;
            }
            VanillaMothronYoungAi1458.StepBaby(state, expert, B("eclipse"), playerX, playerY, B("solid"), repelled);
        }


        void Check(string name, float actual)
        {
            float expected = F(name);
            if (!actual.Equals(expected))
                Assert.Fail($"{name}: {actual:R} != {expected:R}");
        }
        void Flag(string name, bool actual)
        {
            if (actual != B(name))
                Assert.Fail($"{name}: {actual} != {B(name)}");
        }
        Check("outType", state.Type);
        Check("outX", state.X);
        Check("outY", state.Y);
        Check("outVx", state.Vx);
        Check("outVy", state.Vy);
        Check("outWidth", state.Width);
        Check("outHeight", state.Height);
        Check("life", state.Life);
        Check("lifeMax", state.LifeMax);
        Check("damage", state.Damage);
        Check("defense", state.Defense);
        Check("direction", state.Direction);
        Check("directionY", state.DirectionY);
        Check("sprite", state.Sprite);
        Check("rotation", state.Rotation);
        Check("knockback", state.Knockback);
        Check("timeLeft", state.TimeLeft);
        Check("peerVx", peerVx);
        Check("peerVy", peerVy);
        Flag("netUpdate", state.Force);
        Flag("noGravity", state.NoGravity);
        Flag("noTile", state.NoTile);
        Flag("dontTakeDamage", state.Invulnerable);
        Flag("collideX", state.CollideX);
        Flag("collideY", state.CollideY);
        float[] actualAi = [state.A0, state.A1, state.A2, state.A3];
        int index = 0;
        foreach (var value in row.GetProperty("outAi").EnumerateArray())
        {
            if (!actualAi[index].Equals(Number(value)))
                Assert.Fail($"ai[{index}]: {actualAi[index]:R} != {Number(value):R}");
            index++;
        }
        foreach (var value in row.GetProperty("outLocal").EnumerateArray())
            if (Number(value) != 0f)
                Assert.Fail("nonzero local");
        JsonElement[] children = row.GetProperty("spawned").EnumerateArray().ToArray();
        if (state.Egg is { Slot: < 200 } egg)
        {
            if (children.Length != 1 || children[0].GetProperty("slot").GetInt32() != egg.Slot ||
                Number(children[0].GetProperty("x")) != egg.X || Number(children[0].GetProperty("y")) != egg.Y)
                Assert.Fail("spawn origin/slot");
        }
        else if (children.Length != 0)
            Assert.Fail("missing spawn");
        Assert.Equal(row.GetProperty("nextRandom").GetInt32(), random.NextInt32(0, int.MaxValue));
    }
    private static float Number(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? value.GetString() switch { "NaN" => float.NaN, "Infinity" => float.PositiveInfinity, "-Infinity" => float.NegativeInfinity, _ => throw new FormatException() }
        : value.GetSingle();
    private sealed class TestWorld(WorldTileStore tiles) : IVanillaMothronEnvironment1458
    {
        public int WidthTiles => 600;
        public int HeightTiles => 500;
        public double WorldSurfaceTiles => 140;
        public bool TryReadTile(int x, int y, out bool solid, out bool lava)
        {
            solid = lava = false;
            if ((uint)x >= 600 || (uint)y >= 500)
                return false;
            var tile = tiles.Get(x, y);
            solid = tile.IsActive && tile.Type == 1;
            return true;
        }
        public bool CanHit(float x, float y, float px, float py) => VanillaWorldCanHit.HasLineOfSight(tiles, x, y, 1, 1, px, py, 1, 1);
        public bool SolidCollision(float x, float y, int w, int h) => VanillaWorldSolidCollision.Intersects(tiles, x, y, w, h);
    }
}
