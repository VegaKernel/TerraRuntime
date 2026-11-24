# VanillaPorter (experimental)

VanillaPorter is a standalone Roslyn developer tool. It is **not** part of the TerraRuntime shipping projects.

It analyzes one method in locally owned/decompiled C# reference source and emits a syntax-only dependency report. For an intentionally narrow subset of static void methods using simple field access, arithmetic assignments and if/else, it transforms explicit member mappings into a separate generated C# candidate. It refuses unknown member access, calls, loops, globals, and other unsupported syntax. The generated candidate is **not** a vanilla-parity claim.

```bash
dotnet run --project tools/TerraRuntime.VanillaPorter -- scan --file tools/TerraRuntime.VanillaPorter/examples/ai-sample.cs --method Step
dotnet run --project tools/TerraRuntime.VanillaPorter -- scaffold --file tools/TerraRuntime.VanillaPorter/examples/ai-sample.cs --method Step --mapping tools/TerraRuntime.VanillaPorter/examples/ai-mapping.json
dotnet run --project tools/TerraRuntime.VanillaPorter -- verify --file tools/TerraRuntime.VanillaPorter/examples/ai-sample.cs --method Step --mapping tools/TerraRuntime.VanillaPorter/examples/ai-mapping.json
```

The `verify` command compiles a self-contained synthetic source and generated candidate via Roslyn and compares 15 inputs. It explicitly refuses to treat Terraria source as this synthetic test. No local official Terraria assembly is needed for these tests.

To explore real AI_003:

```bash
dotnet run --project tools/TerraRuntime.VanillaPorter -- scan --source decompiled/1458 --npc-ai 3
```

If there are multiple or no candidate source methods, selection fails. Inspect the source and specify `--file` and `--method` explicitly. Scan can inspect unsupported methods and list calls/conditions; scaffold will **refuse** them.

Generated output is written to ignored `artifacts/vanillaporter`. No decompiled official source, assets or copied algorithms are committed. The only committed source example is independently written and synthetic.

**Limitations:** analysis is syntactic, not binding-aware; it does not resolve a call graph or check integer/float semantics automatically. The scaffold deliberately has no production integration. All real gameplay ports require independent vanilla fixtures and review.
