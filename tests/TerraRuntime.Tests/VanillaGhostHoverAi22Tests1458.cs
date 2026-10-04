using System.IO.Compression;
using System.Collections.Concurrent;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaGhostHoverAi22Tests1458
{
    // Independent original TerrariaServer.exe SHA256:
    // 4b87890ac53d40f61db5f928693a379acf4ccbd8ed3b47eb32fb096f145df034.
    // Captures invoke IdleSounds+whole NPC.AI or complete NPC.UpdateNPC, never runtime helpers.
    private static readonly ConcurrentDictionary<(int Type, int Terrain, bool Visible, bool Outer), WorldTileStore> Worlds = new();
    public static IEnumerable<object[]> Cases()
    {
        foreach (string file in new[] { "GhostHoverIdleAi1458", "GhostHoverGates1458", "GhostHoverOuter1458", "GhostHoverDeep1458", "GhostHoverBody1458" })
        {
            using var resource = typeof(VanillaGhostHoverAi22Tests1458).Assembly.GetManifestResourceStream(file)!;
            using var gzip = new GZipStream(resource, CompressionMode.Decompress);
            using var document = JsonDocument.Parse(gzip);
            foreach (var row in document.RootElement.EnumerateArray())
            {
                if (row.TryGetProperty("targetState", out var state) && state.GetInt32() is not (0 or 5))
                    continue;
                yield return [row.Clone()];
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Source_idle_AI_and_optional_outer_physics_match(JsonElement row)
    {
        int I(string name) => row.GetProperty(name).GetInt32();
        float F(string name) => row.GetProperty(name).GetSingle();
        bool B(string name) => row.GetProperty(name).GetBoolean();
        bool outer = row.TryGetProperty("startY", out _) && !row.TryGetProperty("bodyOnly", out _);
        bool visible = !row.TryGetProperty("visible", out var sight) || sight.GetBoolean();
        int type = I("type");
        int targetState = row.TryGetProperty("targetState", out var state) ? state.GetInt32() : 0;
        float startY = row.TryGetProperty("startY", out var startYValue) ? startYValue.GetSingle() : 1000f;
        float startX = row.TryGetProperty("startX", out var startXValue) ? startXValue.GetSingle() : 1000f;
        float dy = row.TryGetProperty("dy", out var dyValue) ? dyValue.GetSingle() : -100f;
        var random = new SystemVanillaNpcRandom(I("seed"));
        var stepper = new VanillaNpcTargetingAiStepper(new Rejecting(), random: random);
        var tiles = Worlds.GetOrAdd((type, I("terrain"), visible, outer), CreateWorld);
        var world = new VanillaNpcWorldMotionAiStepper(stepper, tiles, 140d);
        stepper.SetWorldConditions(dayTime: true, slimeRainActive: false, eclipseActive: B("eclipse"));
        stepper.SetCandidates([new VanillaNpcTargetCandidate(0, startX + I("width") * .5f + F("dx"),
            (outer ? 1000f : startY) + I("height") * .5f + dy, 0, true, false, false, targetState == 5)]);
        var before = new NpcSnapshot(new NpcHandle(199, new NpcGeneration(1)), new NpcRevision(1),
            (short)type, (short)type, startX, startY, F("vx"), F("vy"), 0,
            new NpcAiState(1000, 1000, F("stuck"), F("attack")), NpcSimulationState.Initial with
            {
                Life = type == 122 ? 220 : type == 82 ? 160 : 700,
                LifeMax = type == 122 ? 220 : type == 82 ? 160 : 700,
                TimeLeft = 750,
                HitboxOverride = new NpcHitboxDimensions(I("width"), I("height")),
                Confused = row.TryGetProperty("confused", out var confusion) && confusion.GetBoolean(),
                LocalAi = new NpcAiState(0, F("localClock"), 0, 0),
                DirectionX = 1, DirectionY = 1, SpriteDirection = 1,
                NoGravity = true, NoTileCollide = type != 122,
                CollideX = (I("collide") & 1) != 0, CollideY = (I("collide") & 2) != 0,
                OldVelocityX = F("vx"), OldVelocityY = F("vy"),
                JustHit = B("hit"), OldPositionX = 999, OldPositionY = 1000
            });
        Assert.True(world.TryStepState(in before, out var initial));
        var accepted = new NpcSnapshot(before.Handle, new NpcRevision(2), initial.Type, initial.NetId,
            initial.PositionX, initial.PositionY, initial.VelocityX, initial.VelocityY, initial.Target, initial.Ai, initial.Simulation);
        Span<NpcAiProjectileIntent> shots = stackalloc NpcAiProjectileIntent[5];
        Assert.True(stepper.TryPlanBeforeWorldMotion(in before, in accepted, shots, out int count, out var planned));
        Assert.True(outer ? world.TryApplyWorldMotion(in before, in planned, out var next) :
            world.TryApplyTerrainMotion(in before, in planned, out next));
        Assert.Equal(F("outX"), next.PositionX);
        Assert.Equal(F("outY"), next.PositionY);
        Assert.Equal(F("outVx"), next.VelocityX);
        Assert.Equal(F("outVy"), next.VelocityY);
        var ai = row.GetProperty("outAi");
        var local = row.GetProperty("outLocal");
        Assert.Equal(new NpcAiState(ai[0].GetSingle(), ai[1].GetSingle(), ai[2].GetSingle(), ai[3].GetSingle()), next.Ai);
        Assert.Equal(new NpcAiState(local[0].GetSingle(), local[1].GetSingle(), local[2].GetSingle(), local[3].GetSingle()), next.Simulation.LocalAi);
        Assert.Equal(I("direction"), next.Simulation.DirectionX);
        Assert.Equal(I("directionY"), next.Simulation.DirectionY);
        // Full UpdateNPC runs presentation-only FindFrame after physics; pure AI comparison retains its sprite assertion.
        if (!outer)
            Assert.Equal(I("sprite"), next.Simulation.SpriteDirection);
        Assert.Equal(I("target"), next.Target);
        Assert.Equal(B("collideX"), next.Simulation.CollideX);
        Assert.Equal(B("collideY"), next.Simulation.CollideY);
        var finiteShots = row.GetProperty("shots").EnumerateArray().Where(s =>
            s.GetProperty("vx").ValueKind == JsonValueKind.Number && s.GetProperty("vy").ValueKind == JsonValueKind.Number).ToArray();
        Assert.Equal(finiteShots.Length, count);
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(84, shots[i].Type.Value);
            Assert.Equal(finiteShots[i].GetProperty("x").GetSingle(), shots[i].PositionX);
            Assert.Equal(finiteShots[i].GetProperty("y").GetSingle(), shots[i].PositionY);
            Assert.Equal(finiteShots[i].GetProperty("vx").GetSingle(), shots[i].VelocityX);
            Assert.Equal(finiteShots[i].GetProperty("vy").GetSingle(), shots[i].VelocityY);
            Assert.Equal(25, shots[i].Damage);
        }
        if (!outer)
        {
            var final = new NpcSnapshot(before.Handle, new NpcRevision(3), next.Type, next.NetId, next.PositionX,
                next.PositionY, next.VelocityX, next.VelocityY, next.Target, next.Ai, next.Simulation);
            Assert.Equal(B("netUpdate"), stepper.RequiresForcedUpdateAfterCompletion(in before, in final));
        }
        Assert.Equal(I("nextRandom"), random.NextInt32(0, int.MaxValue));
    }

    private static WorldTileStore CreateWorld((int Type, int Terrain, bool Visible, bool Outer) key)
    {
        var tiles = new WorldTileStore(new WorldDimensions(200, 500));
        int height = key.Type == 122 ? 20 : 44;
        int bottom = (1000 + height) / 16;
        for (int y = bottom; y < bottom + (key.Type == 122 ? 8 : 3); y++)
            Write(65, y);
        if (key.Outer)
            for (int x = 60; x <= 68; x++) Write(x, 64);
        if (!key.Visible)
            for (int y = 0; y < 500; y++)
                tiles.Set(66, y, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        return tiles;

        void Write(int x, int y) => tiles.Set(x, y, new WorldTile
        {
            Type = (ushort)(key.Terrain == 2 ? 19 : 1),
            Flags = key.Terrain is 1 or 2 || (key.Terrain == 4 && y == bottom + 7) ? WorldTileFlags.Active : 0,
            LiquidAmount = (byte)(key.Terrain == 3 || (key.Terrain == 5 && y == bottom + 7) ? 255 : 0)
        });
    }

    private sealed class Rejecting : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return false;
        }
    }
}
