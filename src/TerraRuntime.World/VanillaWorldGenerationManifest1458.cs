using System.Text;
using System.Text.Json;

namespace TerraRuntime.World;

internal readonly record struct VanillaGenerationPassResult1458(
    string Name, int DurationMs, int RandNext, uint? Hash, bool Skipped);

/// <summary>Immutable SaveWorldFlags representation of the completed vanilla generation report.</summary>
internal sealed class VanillaWorldGenerationManifest1458
{
    internal const int MaxSerializedBytes = 64 * 1024;
    private readonly VanillaGenerationPassResult1458[] results;

    internal VanillaWorldGenerationManifest1458(ReadOnlySpan<VanillaGenerationPassResult1458> results)
    {
        if (results.IsEmpty || results.Length > 109)
            throw new ArgumentOutOfRangeException(nameof(results));
        foreach (var result in results)
            if (string.IsNullOrEmpty(result.Name) || result.Name.Length > 128 || result.DurationMs < 0)
                throw new ArgumentException("Invalid vanilla generation result.", nameof(results));
        this.results = results.ToArray();
    }

    internal string Serialize()
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteStartArray("GenPassResults");
            foreach (var result in results)
            {
                json.WriteStartObject();
                json.WriteString("Name", result.Name);
                json.WriteNumber("DurationMs", result.DurationMs);
                json.WriteNumber("RandNext", result.RandNext);
                if (result.Hash is uint hash) json.WriteNumber("Hash", hash);
                else json.WriteNull("Hash");
                json.WriteBoolean("Skipped", result.Skipped);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteString("Version", "v1.4.5.8");
            json.WriteString("GitSHA", string.Empty);
            if (results[^1].Hash is uint finalHash) json.WriteNumber("FinalHash", finalHash);
            else json.WriteNull("FinalHash");
            json.WriteEndObject();
        }
        if (buffer.Length > MaxSerializedBytes)
            throw new InvalidDataException("Vanilla generation manifest exceeds its persistence budget.");
        return Encoding.UTF8.GetString(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)));
    }
}
