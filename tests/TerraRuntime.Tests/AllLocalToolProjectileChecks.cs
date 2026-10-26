using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Projectiles;
using Xunit;

namespace TerraRuntime.Tests.Tools1458;

internal static partial class AllLocalToolSourceChecks
{
    private static HeldToolProjectileState1458 Held(JsonElement e) => new(
        V(e, "position", 0), V(e, "position", 1), V(e, "velocity", 0), V(e, "velocity", 1),
        Bit(e, "scale"), Bit(e, "rotation"), I(e, "direction"), I(e, "spriteDirection"),
        I(e, "timeLeft"), I(e, "soundDelay"), I(e, "frame"), I(e, "frameCounter"),
        e.GetProperty("localAI")[0].GetSingle(), e.GetProperty("localAI")[1].GetSingle(),
        B(e, "active"), B(e, "netUpdate"));

    private static void EqualHeld(JsonElement expected, in HeldToolProjectileState1458 actual)
    {
        var e = Held(expected);
        EqualFloat(e.X, actual.X); EqualFloat(e.Y, actual.Y);
        EqualFloat(e.VelocityX, actual.VelocityX); EqualFloat(e.VelocityY, actual.VelocityY);
        EqualFloat(e.Scale, actual.Scale); EqualFloat(e.Rotation, actual.Rotation);
        EqualFloat(e.LocalAi0, actual.LocalAi0); EqualFloat(e.LocalAi1, actual.LocalAi1);
        Assert.Equal(e.Direction, actual.Direction); Assert.Equal(e.SpriteDirection, actual.SpriteDirection);
        Assert.Equal(e.TimeLeft, actual.TimeLeft); Assert.Equal(e.SoundDelay, actual.SoundDelay);
        Assert.Equal(e.Frame, actual.Frame); Assert.Equal(e.FrameCounter, actual.FrameCounter);
        Assert.Equal(e.Active, actual.Active); Assert.Equal(e.NetUpdate, actual.NetUpdate);
    }

    private static void CheckHeld(string platform, string repeat,
        Dictionary<int, JsonElement> tools, bool windows)
    {
        using var document = Read("Held", platform, repeat);
        int direct = 0, full = 0, rejectedNonfinite = 0;
        Assert.Equal(828, document.RootElement.GetArrayLength());
        var rowFailures = new List<string>();
        foreach (var row in document.RootElement.EnumerateArray())
        {
            try
            {
            string kind = row.GetProperty("kind").GetString()!;
            if (kind == "actual-item-check-then-projectile-updates")
            {
                // The combined original loop is retained as a separate source
                // boundary; the App actor proof owns its connected execution.
                continue;
            }
            Assert.True(kind is "direct-ai" or "full-projectile-update");
            bool complete = kind == "full-projectile-update";
            int id = I(row, "item");
            var metadata = tools[id];
            var before = row.GetProperty("before");
            var owner = row.GetProperty("beforeOwner");
            var randomBefore = row.GetProperty("beforeRandom");
            string mode = row.GetProperty("mode").GetString()!;
            foreach (var step in row.GetProperty("steps").EnumerateArray())
            {
                context = $"{id}/{kind}/{B(row, "local")}/{mode}/{I(step, "tick")}";
                var random = Random(randomBefore);
                var state = Held(before);
                float mx = V(owner, "mountedCenter", 0), my = V(owner, "mountedCenter", 1);
                float aimX = mode == "zero-aim" ? (int)mx : 470f;
                float aimY = mode == "zero-aim" ? (int)my : 430f;
                if (F(owner, "gravDir") == -1f) aimY = 600f - aimY;
                var facts = new HeldToolOwnerFacts1458(mx, my, B(row, "local"), B(owner, "channel"),
                    B(owner, "noItems"), B(owner, "CCed"), I(metadata, "pick"), F(metadata, "shootSpeed"),
                    aimX, aimY, false, 0, 0, 5, 3, I(metadata, "tileBoost"), windows);
                if (complete) state = state with { SoundDelay = Math.Max(0, state.SoundDelay - 1), NetUpdate = false };
                bool finite = float.IsFinite(state.X) && float.IsFinite(state.Y) &&
                    float.IsFinite(state.VelocityX) && float.IsFinite(state.VelocityY);
                bool accepted = VanillaHeldToolProjectile1458.TryStepAi(new(I(before, "type")),
                    I(before, "width"), I(before, "height"), state, facts, random.Next, out var result);
                if (!finite)
                {
                    // Source direct-AI repeats on NaN are diagnostics; the
                    // bounded kernel refuses nonfinite entry before drawing.
                    Assert.False(accepted);
                    AssertRandom(randomBefore, random);
                    rejectedNonfinite++;
                }
                else
                {
                    Assert.True(accepted);
                    state = result.State;
                    if (complete)
                    {
                        if (!B(owner, "active")) state = state with { Active = false, TimeLeft = 0 };
                        state = state with
                        {
                            X = state.X + state.VelocityX,
                            Y = state.Y + state.VelocityY,
                            Direction = state.VelocityX < 0f ? -1 : 1
                        };
                        if (state.Active) state = state with { TimeLeft = state.TimeLeft - 1 };
                        // Update consumes the source net intent through its publication stage.
                        if (state.Active) state = state with { NetUpdate = false };
                    }
                    EqualHeld(step.GetProperty("state"), state);
                    Assert.Equal(result.OwnerDirection, I(step.GetProperty("owner"), "direction"));
                    Assert.Equal(2, I(step.GetProperty("owner"), "itemAnimation"));
                    Assert.Equal(2, I(step.GetProperty("owner"), "itemTime"));
                    Assert.Equal(3, I(step.GetProperty("owner"), "itemTimeMax"));
                    EqualFloat(result.OwnerItemRotation, F(step.GetProperty("owner"), "itemRotation"));
                    AssertRandom(step.GetProperty("random"), random);
                    Assert.Equal(I(step, "next"), random.Clone().Next());
                }
                before = step.GetProperty("state");
                owner = step.GetProperty("owner");
                randomBefore = step.GetProperty("random");
                if (complete) full++; else direct++;
            }

            }
            catch (Exception error) { if (rowFailures.Count < 24) rowFailures.Add(context + ": " + error); }
        }
        Assert.True(rowFailures.Count == 0, string.Join(Environment.NewLine, rowFailures));
        Assert.True(direct > 1000 && full > 1000);
        Assert.True(rejectedNonfinite > 0);
    }

    private static ThrownAxeState1458 Axe(JsonElement e) => new(V(e, "position", 0), V(e, "position", 1),
        V(e, "velocity", 0), V(e, "velocity", 1), Bit(e, "rotation"), I(e, "direction"),
        I(e, "timeLeft"), I(e, "soundDelay"), e.GetProperty("ai")[0].GetSingle(),
        e.GetProperty("ai")[1].GetSingle(), B(e, "tileCollide"), B(e, "active"), B(e, "netUpdate"));

    private static void EqualAxe(JsonElement e, in ThrownAxeState1458 actual)
    {
        var expected = Axe(e);
        EqualFloat(expected.X, actual.X); EqualFloat(expected.Y, actual.Y);
        EqualFloat(expected.VelocityX, actual.VelocityX); EqualFloat(expected.VelocityY, actual.VelocityY);
        EqualFloat(expected.Rotation, actual.Rotation); EqualFloat(expected.Ai0, actual.Ai0);
        EqualFloat(expected.Ai1, actual.Ai1);
        Assert.Equal(expected.Direction, actual.Direction); Assert.Equal(expected.TimeLeft, actual.TimeLeft);
        Assert.Equal(expected.SoundDelay, actual.SoundDelay); Assert.Equal(expected.TileCollide, actual.TileCollide);
        Assert.Equal(expected.Active, actual.Active); Assert.Equal(expected.NetUpdate, actual.NetUpdate);
    }

    private static void CheckExtension(string platform, string repeat, bool windows)
    {
        using var document = Read("Extension", platform, repeat);
        int launches = 0, axes = 0, health = 0, pooled = 0;
        foreach (var row in document.RootElement.EnumerateArray())
        {
            string kind = row.GetProperty("kind").GetString()!;
            context = kind;
            if (kind == "launch")
            {
                int id = I(row, "item");
                context = $"launch/{id}/{I(row, "inputPrefix")}/{F(row, "meleeMultiplier")}/{B(row, "left")}";
                Assert.True(VanillaToolCatalog1458.TryResolve(new(id), new(I(row, "actualPrefix")), false,
                    windows, out var tool));
                Assert.True(VanillaToolCatalog1458.TryGetLaunchStats(tool, 1f, F(row, "meleeMultiplier"),
                    false, false, windows, out var launch));
                Assert.Single(row.GetProperty("projectiles").EnumerateArray());
                var born = row.GetProperty("projectiles")[0];
                Assert.Equal(I(born, "type"), launch.Projectile.Value);
                Assert.Equal(I(born, "damage"), launch.Damage);
                EqualFloat(Bit(born, "knockBack"), launch.KnockBack);
                if (id != 5519)
                {
                    Assert.True(VanillaHeldToolProjectile1458.TryFinalize(new(I(born, "type")),
                        V(born, "position", 0), V(born, "position", 1), V(born, "velocity", 0),
                        V(born, "velocity", 1), I(born, "direction"), out var finalized));
                    EqualHeld(born, finalized);
                }
                else
                {
                    Assert.True(VanillaThrownAxeProjectile1458.TryFinalize(V(born, "position", 0),
                        V(born, "position", 1), V(born, "velocity", 0), V(born, "velocity", 1),
                        I(born, "direction"), out var finalized));
                    EqualAxe(born, finalized);
                }
                var random = Random(row.GetProperty("beforeRandom"));
                random.Next(0, 3); // This complete empty-target launch has only the common crit roll.
                AssertRandom(row.GetProperty("afterRandom"), random);
                Assert.Equal(I(row, "afterNext"), random.Clone().Next());
                launches++;
            }
            else if (kind == "axe1052")
            {
                // Full movement/cut/wet/world effects belong to the App actor;
                // the independent direct-AI rows isolate this pure AI3 kernel.
                if (B(row, "full")) continue;
                var previous = row.GetProperty("before");
                var owner = row.GetProperty("beforeOwner");
                var previousRandom = row.GetProperty("beforeRandom");
                foreach (var step in row.GetProperty("steps").EnumerateArray())
                {
                    context = $"1052/{row.GetProperty("mode").GetString()}/{B(row, "local")}/{I(step, "tick")}";
                    var facts = new ThrownAxeOwnerFacts1458(V(owner, "position", 0), V(owner, "position", 1),
                        20, 42, true, B(row, "local"), 1f, windows);
                    Assert.True(VanillaThrownAxeProjectile1458.TryStepAi(Axe(previous), facts, out var result));
                    EqualAxe(step.GetProperty("after"), result.State);
                    var random = Random(previousRandom);
                    AssertRandom(step.GetProperty("random"), random);
                    Assert.Equal(I(step, "next"), random.Clone().Next());
                    previous = step.GetProperty("after");
                    owner = step.GetProperty("owner");
                    previousRandom = step.GetProperty("random");
                    axes++;
                }
            }
            else if (kind == "cactus-damage-var")
            {
                var random = Random(row.GetProperty("beforeRandom"));
                Assert.True(VanillaToolSelfHurt1458.TryPrepare(ToolSelfHurtKind1458.Cactus, 100,
                    BitConverter.Int32BitsToSingle(I(row, "luckBits")), 1, random.Next, random.NextDouble, out var request));
                Assert.Equal(I(row, "result"), request.Damage);
                Assert.Equal(VanillaPlayerImmunityChannel1458.TileContact, request.ImmunityChannel);
                AssertRandom(row.GetProperty("afterRandom"), random);
                Assert.Equal(I(row, "afterNext"), random.Clone().Next());
                health++;
            }
            else if (kind == "altar-active-attempt")
            {
                Assert.True(VanillaToolCatalog1458.TryGet(new(I(row, "tool")), out var tool));
                bool hurt = tool.Hammer < 80 || !B(row, "hard");
                if (hurt)
                {
                    Assert.True(VanillaToolSelfHurt1458.TryPrepare(ToolSelfHurtKind1458.Altar,
                        I(row, "life"), 0f, 1, static (_, _) => throw new InvalidOperationException(),
                        static () => throw new InvalidOperationException(), out var request));
                    Assert.Equal(I(row, "life") / 2, request.Damage);
                    // This is a pre-Hurt request. Original Hurt's minimum makes
                    // life1/request0 lethal; never skip the downstream call.
                    Assert.Equal(I(row, "life") - Math.Max(1, request.Damage), I(row, "afterLife"));
                }
                else Assert.Equal(I(row, "life"), I(row, "afterLife"));
                health++;
            }
            else if (kind == "pooled-direction")
            {
                var born = row.GetProperty("projectile");
                Assert.False(B(row.GetProperty("retired"), "active"));
                Assert.Equal(I(row, "direction"), I(born, "direction"));
                Assert.True(VanillaHeldToolProjectile1458.TryFinalize(new(I(born, "type")),
                    V(born, "position", 0), V(born, "position", 1), V(born, "velocity", 0),
                    V(born, "velocity", 1), I(row.GetProperty("retired"), "direction"), out var finalized));
                EqualHeld(born, finalized);
                pooled++;
            }
            else throw new InvalidDataException($"Unknown extension kind {kind}");
        }
        Assert.Equal(192, launches); Assert.True(axes > 100); Assert.Equal(33, health); Assert.Equal(4, pooled);
    }
}
