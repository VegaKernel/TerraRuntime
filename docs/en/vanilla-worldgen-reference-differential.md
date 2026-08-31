# Vanilla world-generation reference differential

TerraRuntime uses an executable differential gate against the pinned official TerrariaServer 1.4.5.8 world generator. The gate is intentionally separate from unit tests: it answers whether a complete persisted `terraruntime:vanilla` world stays inside known structural bounds when both generators receive the same canonical world size and seed.

## Canonical reference case

The CI workflow `.github/workflows/vanilla-worldgen-reference-differential.yml` uses:

- Terraria 1.4.5.8 world format `326`;
- Small world, `4200 x 1200` tiles;
- seed `8675309`;
- normal difficulty and corruption;
- the official dedicated server archive `terraria-server-1458.zip`;
- pinned `TerrariaServer.exe` SHA-256 `d87e3faf08637f6be8882c63e7f11fb7e792b0230006309618473ece0f863e1e`.

The official and TerraRuntime worlds are loaded through TerraRuntime's own world loader and compared by `tools/TerraRuntime.WorldCompare`.

## What is compared

The report records dimensions and format version, spawn and dungeon anchors, world-surface and rock-layer positions, evil type, active-tile/wall/liquid totals, normalized tile and wall histogram distances, persisted chest/sign/NPC/tile-entity counts, and a deterministic SHA-256 fingerprint of the decoded tile grid.

`--enforce` turns the report into a regression gate. It requires, among other checks, identical world dimensions and format, the dungeon on the same side for the same seed, bounded structural ratios and histogram distances, reasonable layer/anchor deltas, required biome/structure materials, persisted chests, and a starting town NPC. A failed budget exits non-zero and fails CI.

The seed also has a unit-level `WorldGen.Reset` checkpoint. For `8675309`, the source-backed bootstrap must select the right dungeon side with reset dungeon location `3364`; the first RNG value visible to Terrain after Reset is pinned as well. This catches call-order drift before an expensive reference-world run.

## Evidence levels

Do not treat one green label as proof of full vanilla parity. The project uses three different claims:

1. **Implemented** means the runtime has a concrete pass or subsystem for the behavior.
2. **Tested** means local/unit/integration contracts exercise that implementation.
3. **Reference-proven** means the official differential gate checked a real TerrariaServer-generated world for the pinned case.

The differential budgets are regression bounds, not byte-for-byte equivalence. The structural SHA-256 is recorded as evidence and change detection, but it is not currently required to equal the official fingerprint. As individual passes become source-faithful, their budgets should be tightened rather than widening the gate to accommodate regressions.

## Artifacts

Every workflow run uploads the comparison JSON, official server log/config, official `.wld`, and TerraRuntime candidate `.wld` for short-term inspection. This lets a red gate be audited from concrete worlds rather than from a single summary number.
