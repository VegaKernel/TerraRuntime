using System.IO.Compression;
using System.Text.Json;
namespace TerraRuntime.Tests.DurableWorldChestCustody;

internal static class WorldChestCraftSource
{
    internal static JsonDocument Read(string name)
    {
        using var stream = typeof(WorldChestCraftSource).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing original numeric source: " + name);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
}
