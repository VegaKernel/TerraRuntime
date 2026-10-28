using System.Text.Json;
using System.Text.Json.Serialization;
using SourceProbe;

var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, WriteIndented = true };
if (args.Length == 2 && args[0] == "--value-candidate-self-test")
{
    var checks = ScalarValueChecks.Run();
    using var output = new FileStream(args[1], FileMode.CreateNew, FileAccess.Write);
    JsonSerializer.Serialize(output, checks, options);
    Console.WriteLine(JsonSerializer.Serialize(checks, options));
    return;
}
if (args.Length == 2 && args[0] == "--candidate-self-test")
{
    var checks = ScalarChecks.Run();
    using var output = new FileStream(args[1], FileMode.CreateNew, FileAccess.Write);
    JsonSerializer.Serialize(output, checks, options);
    Console.WriteLine(JsonSerializer.Serialize(checks, options));
    return;
}
if (args.Length == 3 && args[0] == "--adapt-scalar")
{
    string cache = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../.cache"));
    string destination = Path.GetDirectoryName(Path.GetFullPath(args[2]))!;
    if (!destination.StartsWith(cache + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        throw new ArgumentException("Generated original-derived bodies must remain below this tool worktree's .cache");
    if ((File.GetAttributes(cache) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Linked candidate cache is unsupported");
    for (string? path = destination; path is not null && path != cache; path = Path.GetDirectoryName(path))
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Linked candidate output is unsupported");
    using var requestInput = File.OpenRead(args[1]);
    using var bounded = new MemoryStream();
    byte[] chunk = new byte[8192]; int count;
    while ((count = requestInput.Read(chunk, 0, Math.Min(chunk.Length, 1024 * 1024 + 1 - (int)bounded.Length))) > 0)
    {
        bounded.Write(chunk, 0, count);
        if (bounded.Length > 1024 * 1024) throw new ArgumentException("Request exceeds1MiB");
    }
    byte[] raw = bounded.ToArray();
    using var envelope = JsonDocument.Parse(raw);
    object observation;
    if (envelope.RootElement.GetProperty("schemaVersion").GetInt32() == 2)
    {
        var value = JsonSerializer.Deserialize<ValueRequest>(raw, options) ?? throw new ArgumentException("Missing value request");
        var loaded = CompilationLoader.Load(value.Analysis.SourceRoot, value.Analysis.Assembly, value.Analysis.Sources);
        observation = ScalarValueVerification.Run(ScalarValueCandidate.Plan(loaded, value), destination);
    }
    else
    {
        var scalar = JsonSerializer.Deserialize<ScalarRequest>(raw, options) ?? throw new ArgumentException("Missing scalar request");
        var loaded = CompilationLoader.Load(scalar.Analysis.SourceRoot, scalar.Analysis.Assembly, scalar.Analysis.Sources);
        observation = ScalarVerification.Run(ScalarCandidate.Plan(loaded, scalar), destination);
    }
    using var output = new FileStream(args[2], FileMode.CreateNew, FileAccess.Write);
    JsonSerializer.Serialize(output, observation, options);
    Console.WriteLine("Scalar candidate compiled and checked against original; no production admission");
    return;
}
if (args.Length != 2) throw new ArgumentException("Expected request.json and new observation.json");
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
