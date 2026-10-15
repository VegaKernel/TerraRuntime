using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;


namespace TerraRuntime.Tests.DurableWorldChestCustody;

internal static class SandboxTrustContractChecks
{
    internal static int Run()
    {
        int checks = 0;
void Check(bool condition) { checks++; if (!condition) throw new Exception($"Check {checks} failed."); }
string assetRoot = Path.Combine(Path.GetTempPath(), "TerraRuntime.Tests", "SandboxTrust", Guid.NewGuid().ToString("N"));
var parser = new SandboxCommandParser(assetRoot, 8400, 2400);
foreach (string command in new[] { "sb1", "sb2", "sb" })
{
    foreach (bool trust in new[] { false, true })
    {
        Check(parser.TryParse($"{command} clone file source.wld" + (trust ? " --import-world-custody" : ""), out var op, out _));
        var source = (SandboxWorldSource.WorldFile)((SandboxOperation.Create)op!).Request.Source;
        Check(source.TrustInitialVanillaStock == trust);
        Check(source.AssetPath == Path.Combine(assetRoot, "source.wld"));
    }
    Check(!parser.TryParse($"{command} clone gen flat --import-world-custody", out _, out _));
    Check(!parser.TryParse($"{command} clone file source.wld --unknown", out _, out _));
    Check(!parser.TryParse($"{command} clone file source.wld --import-world-custody --import-world-custody", out _, out _));
    Check(!parser.TryParse($"{command} clone file ../escape.wld --import-world-custody", out _, out _));
    Check(parser.TryParse($"{command} clone gen flat", out var generated, out _));
    Check(((SandboxOperation.Create)generated!).Request.Source is SandboxWorldSource.Generated);
}
Check(!new SandboxWorldSource.WorldFile("source.wld").TrustInitialVanillaStock);
Check(parser.TryBuildWorldFileRequest("typed", WorldIsolationLevel.InProcess, "source.wld", out var request, out _));
Check(!((SandboxWorldSource.WorldFile)request.Source).TrustInitialVanillaStock);
var descriptor = new SandboxWorkerDescriptor(Guid.NewGuid(), Guid.NewGuid(), SandboxWorkerSourceKind.WorldFile,
    Path.GetFullPath(Path.Combine(assetRoot,"source.wld")), null, 0, 0, 0, 0, 0, null, new string('a',64), 8);
foreach (bool trust in new[] { false, true })
{
    var value = descriptor with { TrustInitialVanillaStock = trust };
    var decoded = SandboxWorkerProtocol.DecodeDescriptor(SandboxWorkerProtocol.Encode(value));
    Check(decoded.TrustInitialVanillaStock == trust);
    Check(((SandboxWorldSource.WorldFile)decoded.ValidateAndCreateSource()).TrustInitialVanillaStock == trust);
}
var gen = new SandboxWorkerDescriptor(Guid.NewGuid(), Guid.NewGuid(), SandboxWorkerSourceKind.Generated,
    "terraruntime:flat", "Trust test", 1458, 300, 200, WorldGenerationGameMode.Classic, WorldGenerationEvil.Corruption, null, null, 8);
Check(gen.ValidateAndCreateSource() is SandboxWorldSource.Generated);
try { SandboxWorkerProtocol.Encode(gen with { TrustInitialVanillaStock = true }); throw new Exception("Expected rejection."); }
catch (InvalidDataException) { checks++; }
Console.WriteLine($"TRUST-CONTRACT-CHECKS {checks}");

        return checks;
    }
}
