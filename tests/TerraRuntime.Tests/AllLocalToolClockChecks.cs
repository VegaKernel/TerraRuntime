using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Tiles;
using Xunit;

namespace TerraRuntime.Tests.Tools1458;

internal static partial class AllLocalToolSourceChecks
{
    private static void CheckActionClock(string platform, string repeat, bool windows)
    {
        using var document = Read("ActionClock", platform, repeat);
        Assert.Equal(16, document.RootElement.GetArrayLength());
        foreach (var row in document.RootElement.EnumerateArray())
        {
            int id = I(row, "id");
            context = $"action-clock/{id}/{I(row, "pickSpeedBits")}";
            Assert.True(VanillaToolCatalog1458.TryGet(new(id), out var tool));
            if (tool.Pick == 0 && id != 4711)
            {
                Assert.Equal(0, I(row.GetProperty("after"), "itemTime"));
                continue;
            }
            var source = row.GetProperty("before");
            var previous = new ToolClockState1458(new(I(source, "itemTime"), I(source, "itemTimeMax"),
                I(source, "itemAnimation"), I(source, "itemAnimationMax"), false, 0, 0),
                I(source, "toolTime"), false, false);
            Assert.True(VanillaToolUse1458.TryApplyActionTime(tool, previous,
                id == 4711 ? ToolActionKind1458.Shovel : ToolActionKind1458.Pick,
                BitConverter.Int32BitsToSingle(I(row, "pickSpeedBits")), windows, out var after));
            Assert.Equal(I(row.GetProperty("after"), "itemTime"), after.Selected.ItemTime);
            Assert.Equal(I(row.GetProperty("after"), "itemTimeMax"), after.Selected.ItemTimeMax);
        }
    }

    private static ToolClockState1458 Clock(JsonElement source) => new(
        new(I(source, "itemTime"), I(source, "itemTimeMax"), I(source, "itemAnimation"),
            I(source, "itemAnimationMax"), B(source, "releaseUseItem"), 0,
            I(source, "revolverCritChanceBonus")), I(source, "toolTime"),
        B(source, "poundRelease"), B(source, "channel"));

    private static void CheckClocks(string platform, string repeat,
        Dictionary<int, JsonElement> metadata, bool windows)
    {
        using var document = Read("ItemCheck", platform, repeat);
        Assert.Equal(302, document.RootElement.GetArrayLength());
        int steps = 0;
        var rowFailures = new List<string>();
        foreach (var row in document.RootElement.EnumerateArray())
        {
            try
            {
            int id = I(row, "item");
            bool local = B(row, "local");
            string mode = row.GetProperty("mode").GetString()!;
            Assert.True(VanillaToolCatalog1458.TryResolve(new(id), default, false, windows, out var tool));
            JsonElement previous = row.GetProperty("before").GetProperty("clock");
            JsonElement previousRandom = row.GetProperty("beforeRandom");
            JsonElement previousProjectiles = default;
            bool targetActive = mode is not ("no-target" or "wall");
            int targetWall = mode == "wall" ? 1 : 0;
            int targetType = I(metadata[id], "axe") > 0 ? 5 : 1;
            foreach (var step in row.GetProperty("steps").EnumerateArray())
            {
                int tick = I(step, "tick");
                context = $"{id}/{local}/{mode}/{tick}";
                bool control = mode == "release" && tick >= 3 ? false : B(previous, "controlUseItem");
                bool noItems = mode == "active-no-items" && tick >= 1 || B(previous, "noItems");
                bool allowed = id != 5519 || previousProjectiles.ValueKind == JsonValueKind.Undefined ||
                    !previousProjectiles.EnumerateArray().Any(p => I(p, "type") == 1052 && I(p, "owner") == 0);
                var facts = new ToolFrameFacts1458(local, control, B(previous, "lastItemUseAttemptSuccess"),
                    noItems, B(previous, "CCed"), false, false, 1f, SourceUseAllowed: allowed);
                var random = Random(previousRandom);
                int roll = facts.CrowdControlled ? 0 : random.Next(0, 3);
                Assert.True(VanillaToolUse1458.TryAdvanceFrame(tool, Clock(previous), facts, roll, out var frame));
                var expected = step.GetProperty("clock");
                Assert.Equal(I(expected, "itemAnimation"), frame.State.Selected.Animation);
                Assert.Equal(I(expected, "itemAnimationMax"), frame.State.Selected.AnimationMax);
                Assert.Equal(B(expected, "releaseUseItem"), frame.State.Selected.ReleaseUseItem);
                Assert.Equal(B(expected, "lastItemUseAttemptSuccess"), frame.LastUseSuccess);
                Assert.Equal(I(expected, "revolverCritChanceBonus"), frame.State.Selected.RevolverCritBonus);
                Assert.Equal(B(expected, "channel"), frame.State.Channel);
                Assert.Equal(!facts.CrowdControlled, frame.CritSampleRequired);
                var clocks = frame.State;
                if (frame.ShootBoundary) Assert.True(VanillaToolUse1458.TryApplyShootTime(tool, clocks, out clocks));
                Assert.True(VanillaToolUse1458.TryEnterToolStage(tool, clocks, frame.LocalToolStage, out clocks));
                Assert.Equal(I(expected, "toolTime"), clocks.ToolTime);

                // These fixtures configure a stone/tree target and no equipment.
                // This checks action clock assignments; it does not replay the
                // client WorldGen/frame/dust RNG or claim whole ItemCheck parity.
                bool inRange = mode != "out-of-range";
                bool boundary = frame.LocalToolStage && clocks.ToolTime == 0 &&
                    clocks.Selected.Animation > 0 && control;
                bool roleAxe = targetType == 5;
                bool roleHammer = VanillaToolDamage1458.IsAHammerTarget(new(targetType));
                var targetFacts = new ToolActionClockFacts1458(id == 4711 ? ToolActionKind1458.Shovel :
                    ToolActionKind1458.Pick, control, B(expected, "noBuilding"), inRange,
                    targetActive, roleHammer, clocks.Selected.ReleaseUseItem, 1f, 1f);
                if (frame.LocalToolStage)
                {
                    Assert.True(VanillaToolUse1458.TryPrepareTarget(clocks, targetFacts, out clocks, out bool eligible));
                    Assert.Equal(boundary && !targetFacts.NoBuilding && inRange, eligible);
                    if (eligible)
                    {
                        ToolActionKind1458? action = id == 4711 ? ToolActionKind1458.Shovel :
                            !targetActive ? mode == "wall" && targetWall > 0 && tool.Hammer > 0 ? ToolActionKind1458.Wall : null :
                            roleAxe && tool.Axe > 0 ? ToolActionKind1458.Axe :
                            !roleAxe && tool.Pick > 0 ? ToolActionKind1458.Pick : null;
                        if (action is { } kind)
                            Assert.True(VanillaToolUse1458.TryApplyActionTime(tool, clocks, kind, 1f, out clocks));
                    }
                    clocks = VanillaToolUse1458.CompleteTileStage(clocks, targetFacts);
                }
                Assert.Equal(I(expected, "itemTime"), clocks.Selected.ItemTime);
                Assert.Equal(I(expected, "itemTimeMax"), clocks.Selected.ItemTimeMax);
                Assert.Equal(B(expected, "poundRelease"), clocks.PoundRelease);
                // Remote ItemCheck has no owner-only action/visual branch in
                // these fixtures: its entire captured RNG is the common roll.
                if (!local) AssertRandom(step.GetProperty("random"), random);
                previous = expected;
                previousRandom = step.GetProperty("random");
                previousProjectiles = step.GetProperty("projectiles");
                if (mode != "out-of-range")
                {
                    targetActive = B(step.GetProperty("tile"), "active");
                    targetWall = I(step.GetProperty("tile"), "wall");
                    targetType = I(step.GetProperty("tile"), "type");
                }
                steps++;
            }

            }
            catch (Exception error) { if (rowFailures.Count < 24) rowFailures.Add(context + ": " + error); }
        }
        Assert.True(rowFailures.Count == 0, string.Join(Environment.NewLine, rowFailures));
        Assert.Equal(10734, steps);
    }
}
