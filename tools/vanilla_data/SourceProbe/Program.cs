using System.Text.Json;
using System.Text.Json.Serialization;
using SourceProbe;

if (args.Length != 2) throw new ArgumentException("Expected request.json and new observation.json");
var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, WriteIndented = true };
if (args[0] == "--self-test")
{
    var checks = InventoryChecks.Run();
    using var output = new FileStream(args[1], FileMode.CreateNew, FileAccess.Write);
    JsonSerializer.Serialize(output, checks, options);
    Console.WriteLine(JsonSerializer.Serialize(checks, options));
    return;
}
var requestFile = new FileInfo(args[0]);
if (requestFile.Length > 1024 * 1024) throw new ArgumentException("Request exceeds 1 MiB");
byte[] requestBytes;
using (var input = new FileStream(args[0], FileMode.Open, FileAccess.Read, FileShare.Read))
{
    using var bounded = new MemoryStream();
    byte[] chunk = new byte[64 * 1024];
    int count;
    while ((count = input.Read(chunk, 0, Math.Min(chunk.Length, 1024 * 1024 + 1 - (int)bounded.Length))) > 0)
    {
        bounded.Write(chunk, 0, count);
        if (bounded.Length > 1024 * 1024) throw new ArgumentException("Request exceeds 1 MiB");
    }
    requestBytes = bounded.ToArray();
}
var request = JsonSerializer.Deserialize<AnalysisRequest>(requestBytes, options)
    ?? throw new ArgumentException("Missing request");
if (request.SchemaVersion != 1 || request.Entries.Length is < 1 or > 32 ||
    request.MaxDepth is < 0 or > 4 || request.MaxMethods is < 1 or > 128)
    throw new ArgumentException("Request bounds differ");
var context = CompilationLoader.Load(request.SourceRoot, request.Assembly, request.Sources);
var result = SemanticInventory.Analyze(context, request);
using (var stream = new FileStream(args[1], FileMode.CreateNew, FileAccess.Write))
    JsonSerializer.Serialize(stream, result, options);
Console.WriteLine(JsonSerializer.Serialize(new { result.Accepted, methods = result.Methods.Count,
    result.SourceClosureComplete }, options));

namespace SourceProbe
{
    internal sealed record Entry(string Path, string Type, string Method, string[] ParameterTypes);
    internal sealed record AnalysisRequest(int SchemaVersion, string SourceRoot, string Assembly,
        string[] Sources, Entry[] Entries, int MaxDepth = 1, int MaxMethods = 32);
}
