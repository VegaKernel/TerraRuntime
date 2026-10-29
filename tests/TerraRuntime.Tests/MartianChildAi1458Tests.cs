using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class MartianChildAi1458Tests
{
    [Fact]
    public void Child_ai_matches_independent_initialized_original_at_construction_attack_attachment_and_idle_edges()
    {
        using var resource = typeof(MartianChildAi1458Tests).Assembly.GetManifestResourceStream("MartianChildAiLinux1458")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        int count = 0;
        foreach (var row in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            var before = row.GetProperty("before");
            var actor = Actor(before);
            var body = new VanillaNpcHitboxSize(I(before, "width"), I(before, "height"));
            var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
            ImportRng(row.GetProperty("rngBefore"), random);
            var shared = new SystemVanillaNpcRandom(random);
            NpcSnapshot after;
            VanillaMartianChildShot1458? shot = null;
            if (actor.Type == 606)
                Assert.True(VanillaMaggotAi1458.TryStep(in actor, shared, out after, out _));
            else if (actor.Type == 384)
            {
                var parent = Parent(row.GetProperty("parent"));
                after = VanillaMartianChildAi1458.FollowBubble(in actor, in body, in parent);
            }
            else
            {
                var players = row.GetProperty("players").EnumerateArray().Select(Player).ToArray();
                if (actor.Type == 387)
                {
                    ushort target = actor.Target < 255 ? actor.Target : (ushort)0;
                    if (VanillaNpcTargeting.TrySelectClosestPlayerTarget(actor.PositionX + body.Width * .5f,
                            actor.PositionY + body.Height * .5f, actor.Simulation.DirectionX,
                            players.Select(p => p.Candidate).ToArray(), out var selected)) target = selected.PlayerSlot;
                    actor = actor with { Target = target };
                    var player = players.Single(p => p.Slot == target);
                    Assert.True(VanillaMartianChildAi1458.TryStepTurret(in actor, in body, in player, shared,
                        out after, out _, out shot));
                }
                else
                {
                    var parent = Parent(row.GetProperty("parent"));
                    var player = players.Single(p => p.Slot == parent.Target);
                    bool visible = row.GetProperty("canHit").GetBoolean();
                    Assert.True(VanillaMartianChildAi1458.TryStepRider(in actor, in body, in parent, in player,
                        visible, shared, out after, out _, out shot));
                }
            }
            Assert.True(A(row.GetProperty("after").GetProperty("ai")) == after.Ai,
                $"{row.GetProperty("type")}/{row.GetProperty("scene")}/{row.GetProperty("seed")}/{row.GetProperty("index")}: AI differs.");
            AssertActor(row.GetProperty("after"), in after);
            AssertRng(row.GetProperty("rngAfter"), random);
            var oldShots = row.GetProperty("shotsBefore");
            var newShots = row.GetProperty("shotsAfter");
            Assert.Equal(oldShots.GetArrayLength() + (shot.HasValue ? 1 : 0), newShots.GetArrayLength());
            if (shot is { } fired)
            {
                var expected = newShots[newShots.GetArrayLength() - 1];
                Assert.Equal(I(expected, "type"), fired.Type.Value);
                Assert.Equal(I(expected, "damage"), fired.Damage);
                AssertBits(F(expected.GetProperty("position")[0]) + I(expected, "width") * .5f, fired.CenterX);
                AssertBits(F(expected.GetProperty("position")[1]) + I(expected, "height") * .5f, fired.CenterY);
                AssertBits(F(expected.GetProperty("velocity")[0]), fired.VelocityX);
                AssertBits(F(expected.GetProperty("velocity")[1]), fired.VelocityY);
            }
            count++;
        }
        Assert.Equal(102, count);
    }

    private static NpcSnapshot Actor(JsonElement x) => new(new(0, new(1)), new(1), I(x, "type"),
        (short)I(x, "netId"), F(x.GetProperty("position")[0]), F(x.GetProperty("position")[1]),
        F(x.GetProperty("velocity")[0]), F(x.GetProperty("velocity")[1]), (ushort)I(x, "target"),
        A(x.GetProperty("ai")), NpcSimulationState.Initial with
        {
            DirectionX = I(x, "direction"), DirectionY = I(x, "directionY"), SpriteDirection = I(x, "spriteDirection"),
            OldVelocityX = F(x.GetProperty("oldVelocity")[0]), OldVelocityY = F(x.GetProperty("oldVelocity")[1]),
            OldPositionX = F(x.GetProperty("oldPosition")[0]), OldPositionY = F(x.GetProperty("oldPosition")[1]),
            CollideX = B(x, "collideX"), CollideY = B(x, "collideY"), Wet = B(x, "wet"),
            NoGravity = B(x, "noGravity"), NoTileCollide = B(x, "noTileCollide"), Scale = F(x.GetProperty("scale")),
            Life = I(x, "life"), LifeMax = I(x, "lifeMax"), TimeLeft = I(x, "timeLeft"), JustHit = B(x, "justHit"),
            LocalAi = A(x.GetProperty("localAi")), FrameCounter = D(x.GetProperty("frameCounter")), FrameIndex = I(x, "frameIndex"),
            Alpha = I(x, "alpha"), DontTakeDamage = B(x, "dontTakeDamage"), Rotation = F(x.GetProperty("rotation")),
            Friendly = B(x, "friendly"), CatchableNpcTempImmunityCounter = I(x, "immunity"), ReleaseOwner = (short)I(x, "release"),
            SpawnDifficulty = 1f
        });

    private static VanillaMartianChildParent1458 Parent(JsonElement x) => new(
        F(x.GetProperty("position")[0]), F(x.GetProperty("position")[1]),
        F(x.GetProperty("velocity")[0]), F(x.GetProperty("velocity")[1]), I(x, "width"), I(x, "height"),
        (ushort)I(x, "target"), I(x, "timeLeft"), I(x, "direction"), I(x, "spriteDirection"));

    private static VanillaNpcRawPlayer1458 Player(JsonElement x) => new((byte)I(x, "slot"),
        B(x, "active"), B(x, "dead"), B(x, "ghost"), F(x.GetProperty("position")[0]), F(x.GetProperty("position")[1]),
        I(x, "width"), I(x, "height"), I(x, "aggro"), B(x, "noAggro"), I(x, "itemAnimation"));

    internal static void AssertActor(JsonElement expected, in NpcSnapshot actual)
    {
        Assert.Equal((I(expected, "type"), I(expected, "netId"), I(expected, "target")),
            (actual.Type, (int)actual.NetId, (int)actual.Target));
        AssertBits(F(expected.GetProperty("position")[0]), actual.PositionX);
        AssertBits(F(expected.GetProperty("position")[1]), actual.PositionY);
        AssertBits(F(expected.GetProperty("velocity")[0]), actual.VelocityX);
        AssertBits(F(expected.GetProperty("velocity")[1]), actual.VelocityY);
        Assert.Equal(A(expected.GetProperty("ai")), actual.Ai);
        Assert.Equal(A(expected.GetProperty("localAi")), actual.Simulation.LocalAi);
        Assert.Equal((I(expected, "direction"), I(expected, "directionY"), I(expected, "spriteDirection")),
            (actual.Simulation.DirectionX, actual.Simulation.DirectionY, actual.Simulation.SpriteDirection));
        Assert.Equal(I(expected, "life"), actual.Simulation.Life);
        Assert.Equal(I(expected, "lifeMax"), actual.Simulation.LifeMax);
        Assert.Equal(I(expected, "timeLeft"), actual.Simulation.TimeLeft);
        Assert.Equal(I(expected, "alpha"), actual.Simulation.Alpha);
        Assert.Equal(I(expected, "frameIndex"), actual.Simulation.FrameIndex);
        Assert.Equal(D(expected.GetProperty("frameCounter")), actual.Simulation.FrameCounter);
        Assert.Equal(B(expected, "dontTakeDamage"), actual.Simulation.DontTakeDamage);
        Assert.Equal(B(expected, "justHit"), actual.Simulation.JustHit);
        Assert.Equal(B(expected, "friendly"), actual.Simulation.Friendly);
        Assert.Equal(I(expected, "immunity"), actual.Simulation.CatchableNpcTempImmunityCounter);
        Assert.Equal((short)I(expected, "release"), actual.Simulation.ReleaseOwner);
        AssertBits(F(expected.GetProperty("rotation")), actual.Simulation.Rotation!.Value);
        Assert.Equal((B(expected, "collideX"), B(expected, "collideY"), B(expected, "wet"),
                B(expected, "noGravity"), B(expected, "noTileCollide")),
            (actual.Simulation.CollideX, actual.Simulation.CollideY, actual.Simulation.Wet,
                actual.Simulation.NoGravity, actual.Simulation.NoTileCollide));
    }

    private static void ImportRng(JsonElement expected, VanillaUnifiedRandom1458 random)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.SetValue(random, expected.GetProperty("cursor").GetUInt32());
        expected.GetProperty("slots").EnumerateArray().Select(x => x.GetInt32()).ToArray()
            .CopyTo((int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(random)!, 0);
        AssertRng(expected, random);
    }

    private static void AssertRng(JsonElement expected, VanillaUnifiedRandom1458 random)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        Assert.Equal(expected.GetProperty("cursor").GetUInt32(),
            (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.GetValue(random)!);
        Assert.Equal(expected.GetProperty("slots").EnumerateArray().Select(x => x.GetInt32()).ToArray(),
            (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(random)!);
    }

    private static bool B(JsonElement x, string name) => x.GetProperty(name).GetBoolean();
    private static int I(JsonElement x, string name) => x.GetProperty(name).GetInt32();
    private static float F(JsonElement x) => BitConverter.UInt32BitsToSingle(uint.Parse(x.GetString()!, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    private static double D(JsonElement x) => BitConverter.UInt64BitsToDouble(ulong.Parse(x.GetString()!, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    private static NpcAiState A(JsonElement x) => new(F(x[0]), F(x[1]), F(x[2]), F(x[3]));
    private static void AssertBits(float expected, float actual) => Assert.Equal(BitConverter.SingleToUInt32Bits(expected), BitConverter.SingleToUInt32Bits(actual));
}
