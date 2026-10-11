using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;

namespace TerraRuntime.Tests;

internal static class ClosedSortCycleSource1458Checks
{
    internal static void Run()
    {
        using var input = typeof(ClosedSortCycleSource1458Checks).Assembly.GetManifestResourceStream("OriginalClosedSortCycle1458")!;
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var source = JsonDocument.Parse(gzip);
        RuntimePlayerInventoryItem Read(JsonElement item) => item[0].GetInt32() == 0 || item[1].GetInt32() <= 0
            ? default : new(new(item[0].GetInt32()), checked((short)item[1].GetInt32()), new(item[2].GetInt32()), item[3].GetBoolean() ? (byte)1 : (byte)0);
        RuntimePlayerInventoryItem[] Image(JsonElement state) => state.GetProperty("inventory").EnumerateArray().Select(Read)
            .Concat(state.GetProperty("banks").EnumerateArray().SelectMany(bank => bank.EnumerateArray()).Select(Read)).ToArray();
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            var corpus = source.RootElement.GetProperty(platform);
            foreach (var row in corpus.GetProperty("cycle").GetProperty("rows").EnumerateArray())
            {
                string name = row.GetProperty("spec").GetProperty("name").GetString()!;
                bool same = name.EndsWith("00", StringComparison.Ordinal) || name.EndsWith("01", StringComparison.Ordinal);
                var afterSort = row.GetProperty("afterSort"); var afterSync = row.GetProperty("afterSync");
                Xunit.Assert.Equal(same, Image(afterSort).AsSpan().SequenceEqual(Image(afterSync)) &&
                    Read(afterSort.GetProperty("mouse")) == Read(afterSync.GetProperty("mouse")));
                Xunit.Assert.Empty(row.GetProperty("operationFrames").EnumerateArray());
                Xunit.Assert.Equal(afterSort.GetProperty("random").GetRawText(), afterSync.GetProperty("random").GetRawText());
                Xunit.Assert.Equal(afterSort.GetProperty("next").GetInt32(), afterSync.GetProperty("next").GetInt32());
                if (!name.EndsWith("01", StringComparison.Ordinal)) continue;
                Xunit.Assert.Equal(corpus.GetProperty("sortAlone").GetProperty("syncFrames").EnumerateArray().Select(frame => frame.GetString()),
                    row.GetProperty("syncFrames").EnumerateArray().Select(frame => frame.GetString()));
                Xunit.Assert.Equal(12, row.GetProperty("syncFrames").GetArrayLength());
            }
        }
    }
}
