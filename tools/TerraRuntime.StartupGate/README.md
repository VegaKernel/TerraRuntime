# TerraRuntime.StartupGate

Executable performance-contract probe for the TerraRuntime Phase 5 startup path.

It measures canonical `.wld` loading, detailed `.runtime-world` reconstruction, liquid restore, prepared runtime state, bootstrap/index construction, `WorldReady`, managed allocations and GC deltas. The dedicated CI workflow combines this output with real host cold/warm listener startup to prove `NetworkReady`.

Usage:

```bash
dotnet run --project tools/TerraRuntime.StartupGate/TerraRuntime.StartupGate.csproj -c Release -- /path/to/world.wld
```
