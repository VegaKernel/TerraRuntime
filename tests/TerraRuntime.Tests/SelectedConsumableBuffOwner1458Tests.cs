using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Tests;

public sealed class SelectedConsumableBuffOwner1458Tests
{
    [Fact]
    public void Original_AddBuff_and_remote_ItemCheck_preserve_all_44_source_slots()
    {
        using Stream stream = typeof(SelectedConsumableBuffOwner1458Tests).Assembly.GetManifestResourceStream("ConsumableBuffAddition1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using JsonDocument document = JsonDocument.Parse(gzip);
        Assert.Equal("4b87890ac53d40f61db5f928693a379acf4ccbd8ed3b47eb32fb096f145df034",
            document.RootElement.GetProperty("sourceSha256").GetString());
        Assert.Equal(12, document.RootElement.GetProperty("rows").GetArrayLength());
        Assert.Equal(13, document.RootElement.GetProperty("coupled").GetArrayLength());
        foreach (string group in new[] { "rows", "coupled" })
            foreach (JsonElement row in document.RootElement.GetProperty(group).EnumerateArray())
            {
                var before = Read(row.GetProperty("before"));
                PlayerBuffState staged = before.Clone();
                var type = new BuffTypeId(row.GetProperty("type").GetInt32());
                bool immune = row.GetProperty("profile").GetString() == "immune";
                bool changed = staged.TryApplySelectedConsumable(type, row.GetProperty("duration").GetInt32(), immune);
                PlayerBuffState expected = Read(row.GetProperty("after"));
                Assert.True(staged.HasSameSlots(expected), $"{group}/{type.Value}/{row.GetProperty("profile").GetString()}");
                Assert.Equal(!before.HasSameSlots(expected) || !immune && before.Contains(type), changed);
                Assert.True(before.HasSameSlots(Read(row.GetProperty("before"))));
            }
    }

    [Fact]
    public void Snapshots_are_detached_exact_and_invalid_offers_leave_the_owner_unchanged()
    {
        var types = new BuffTypeId[PlayerBuffState.Capacity];
        var durations = new int[PlayerBuffState.Capacity];
        types[0] = types[2] = VanillaBuffIds.PotionSickness;
        durations[0] = 10; durations[2] = 60;
        PlayerBuffState owner = PlayerBuffState.FromSlots(types, durations);
        types[0] = default; durations[2] = 0;
        Assert.Equal(60, owner.GetLastActiveDuration(VanillaBuffIds.PotionSickness));
        PlayerBuffState staged = owner.Clone();
        Assert.True(staged.TryApplySelectedConsumable(VanillaBuffIds.PotionSickness, 3600, false));
        Assert.False(staged.HasSameSlots(owner));
        Assert.Equal(60, staged.GetLastActiveDuration(VanillaBuffIds.PotionSickness));
        staged.CaptureSlots(out var capturedTypes, out var capturedDurations);
        Assert.Equal(3600, capturedDurations[0]); Assert.Equal(60, capturedDurations[2]);
        capturedTypes[0] = default; capturedDurations[0] = 0;
        Assert.Equal(3600, staged.GetDuration(VanillaBuffIds.PotionSickness));
        foreach (var offer in new[] { (new BuffTypeId(2), 300, false), (new BuffTypeId(94), 0, false), (new BuffTypeId(94), 300, true) })
        {
            var unchanged = owner.Clone();
            Assert.False(owner.TryApplySelectedConsumable(offer.Item1, offer.Item2, offer.Item3));
            Assert.True(owner.HasSameSlots(unchanged));
        }
        Assert.Throws<ArgumentException>(() => PlayerBuffState.FromSlots([], []));
        var invalidTypes = new BuffTypeId[44]; var invalidDurations = new int[44];
        invalidTypes[0] = new BuffTypeId(VanillaBuffIds.Count);
        Assert.Throws<ArgumentException>(() => PlayerBuffState.FromSlots(invalidTypes, invalidDurations));
        invalidTypes[0] = default; invalidDurations[0] = -1;
        Assert.Throws<ArgumentException>(() => PlayerBuffState.FromSlots(invalidTypes, invalidDurations));
        var moon = new PlayerBuffState();
        Assert.True(moon.TryApplyMoonLeech(90));
        Assert.True(moon.Clone().HasSameSlots(moon));
        Assert.Equal(90, moon.GetDuration(VanillaBuffIds.MoonLeech));
    }

    private static PlayerBuffState Read(JsonElement value) => PlayerBuffState.FromSlots(
        value.GetProperty("types").EnumerateArray().Select(static x => new BuffTypeId(x.GetInt32())).ToArray(),
        value.GetProperty("times").EnumerateArray().Select(static x => x.GetInt32()).ToArray());
}
