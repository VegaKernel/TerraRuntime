from pathlib import Path

path = Path("tests/TerraRuntime.Tests/OptimizedWorldGenerationProviderTests.cs")
text = path.read_text(encoding="utf-8")
name = "Optimized_generator_creates_canonical_small_world_without_crashing"
if name in text:
    raise SystemExit(f"{name} already exists")

marker = """    private static void AssertSpawnHasGround(\n        RuntimeWorldGenerationWorkspace workspace,\n        WorldGenerationPoint spawn)\n"""
if text.count(marker) != 1:
    raise SystemExit(f"canonical optimized crash-regression insertion point: expected 1 occurrence, found {text.count(marker)}")

test = """    [Fact]\n    public void Optimized_generator_creates_canonical_small_world_without_crashing()\n    {\n        var request = new WorldGenerationRequest(\n            OptimizedWorldGenerationProvider.GeneratorId,\n            \"Optimized canonical small\",\n            Seed: 0x0F7145EDUL,\n            WidthTiles: 4200,\n            HeightTiles: 1200);\n        var pipeline = new RuntimeWorldCreationPipeline(BuiltInWorldGeneratorSource.Instance);\n\n        RuntimeWorldCreationPipelineResult result = pipeline.CreateCandidate(\n            in request,\n            cancellationToken: TestContext.Current.CancellationToken);\n\n        Assert.True(\n            result.Succeeded,\n            $\"{result.Status} gen={result.Generation.Status} fin={result.Finalization?.Status} validation={result.Finalization?.Validation} err={result.Generation.Execution?.Error}\");\n        Assert.NotNull(result.Candidate);\n        Assert.Equal(4200, result.Candidate!.WidthTiles);\n        Assert.Equal(1200, result.Candidate.HeightTiles);\n        Assert.Equal(8, OptimizedProgressionWorldValidator.Validate(\n            result.Candidate,\n            result.Candidate,\n            in request,\n            TestContext.Current.CancellationToken).ReachableTargetCount);\n    }\n\n"""

path.write_text(text.replace(marker, test + marker), encoding="utf-8")
