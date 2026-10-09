using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Tests;

public sealed class RemoteItemRotation1458Tests
{
    [Fact]
    public void Original_remote_attempt_resets_rotation_before_failed_use_and_preserves_nonattempts()
    {
        using var stream = typeof(RemoteItemRotation1458Tests).Assembly
            .GetManifestResourceStream("RemoteItemRotation1458")!;
        Assert.NotNull(stream);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var source = JsonDocument.Parse(gzip);
        int profiles = 0, comparisons = 0, failedAttempts = 0;
        foreach (var row in source.RootElement.GetProperty("rows").EnumerateArray())
        {
            profiles++;
            Assert.Equal(2, I(row, "netMode"));
            Assert.Equal(255, I(row, "myPlayer"));
            Assert.True(B(row, "inventoryUnchanged"));
            Assert.Equal(row.GetProperty("beforeProjectiles").GetRawText(),
                row.GetProperty("afterProjectiles").GetRawText());
            Assert.Equal(row.GetProperty("beforeOutside").GetRawText(),
                row.GetProperty("afterOutside").GetRawText());
            foreach (var step in row.GetProperty("steps").EnumerateArray())
            {
                var before = step.GetProperty("beforePlayer");
                var after = step.GetProperty("player");
                var item = new ItemTypeId(I(row, "weapon"));
                var state = new PlayerSelectedConsumableState1458(I(before, "itemTime"),
                    I(before, "itemTimeMax"), I(before, "animation"), I(before, "animationMax"),
                    B(before, "release"), Math.Max(0, I(before, "potionDelay") - 1), I(before, "crit"));
                bool control = B(step, "use"), success = B(before, "lastUseSuccess");
                bool cursed = before.GetProperty("buffTypes").EnumerateArray().Any(x => x.GetInt32() == 23);
                bool reset, began;
                if (VanillaRemoteMeleeItemCheck1458.IsSupported(item))
                {
                    var facts = new PlayerRemoteMeleeItemFacts1458(item, control, success, cursed, false, false);
                    Assert.True(VanillaRemoteMeleeItemCheck1458.TryStep(state, in facts, Sample, out var result));
                    reset = result.ResetItemRotation; began = result.BeganActualUse;
                }
                else if (VanillaRemotePassiveItemCheck1458.IsSupported(item))
                {
                    var facts = new PlayerRemotePassiveItemFacts1458(item, control, success, cursed, false, false);
                    Assert.True(VanillaRemotePassiveItemCheck1458.TryStep(state, in facts, Sample, out var result));
                    reset = result.ResetItemRotation; began = result.BeganActualUse;
                }
                else if (VanillaSelectedConsumableCatalog1458.TryGet(item, out _))
                {
                    var facts = new PlayerSelectedConsumableFacts1458(item, I(before, "life"),
                        I(after, "lifeMaximum"), I(before, "mana"), I(after, "maximum"),
                        control, success, cursed, false, true, false);
                    Assert.True(VanillaSelectedConsumable1458.TryStep(state, in facts, Sample, () => .5, out var result));
                    reset = result.ResetItemRotation; began = result.BeganUse;
                }
                else
                {
                    Assert.True(VanillaRemoteRangedItemCheck1458.IsSupported(item));
                    Assert.True(I(row.GetProperty("itemFacts"), "shoot") > 0);
                    var facts = new PlayerRemoteRangedItemFacts1458(item, true, control, success, cursed, false, false);
                    Assert.True(VanillaRemoteRangedItemCheck1458.TryStep(state, in facts, Sample, out _));
                    reset = false; began = false;
                }
                // Compare only the rotation writer. Whole original RNG and wire records remain intact;
                // these delegates do not claim to reproduce other Player.Update phases.
                float expected = after.GetProperty("itemRotation").GetSingle();
                Assert.Equal(expected, reset ? 0f : before.GetProperty("itemRotation").GetSingle());
                Assert.Empty(step.GetProperty("frames").EnumerateArray());
                Assert.Equal(2, I(step, "actualNetMode"));
                Assert.Equal(255, I(step, "actualMyPlayer"));
                if (I(step, "tick") == 0)
                {
                    Assert.Equal(.25f, before.GetProperty("itemRotation").GetSingle());
                    string mode = row.GetProperty("mode").GetString()!;
                    Assert.Equal("291F0000803E" + (mode == "ongoing" ? "0300" : "0000"),
                        step.GetProperty("command").GetString());
                    if (item.Value is 3508 or 1 or 28 or 110 &&
                        (mode is "failed" or "cursed" || item.Value == 28 && mode == "delay"))
                    {
                        Assert.True(reset); Assert.False(began); Assert.Equal(0, I(after, "animation"));
                        failedAttempts++;
                    }
                }
                comparisons++;
            }
        }
        Assert.Equal(43, profiles);
        Assert.Equal(129, comparisons);
        Assert.Equal(9, failedAttempts);
    }

    private static int Sample(int minimum, int maximum) => minimum;
    private static int I(JsonElement value, string name) => value.GetProperty(name).GetInt32();
    private static bool B(JsonElement value, string name) => value.GetProperty(name).GetBoolean();
}
