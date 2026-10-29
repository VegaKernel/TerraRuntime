using System.IO.Compression;
using System.Text.Json;

namespace TerraRuntime.Tests;

public sealed class GroundTransformFishPresentation1458Tests
{
    public static IEnumerable<object[]> OriginalFrames()
    {
        using var stream = typeof(GroundTransformFishPresentation1458Tests).Assembly
            .GetManifestResourceStream("GroundTransformFishFramesLinux1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.GetProperty("cases").EnumerateArray())
            yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(OriginalFrames))]
    public void Actual_fish_endpoint_pose_uses_the_resolved_identity(string json) =>
        new GroundTransformPresentation1458Tests()
            .Actual_source_pose_includes_swimmer_direction_and_falling_walker_rotation(json);
}
