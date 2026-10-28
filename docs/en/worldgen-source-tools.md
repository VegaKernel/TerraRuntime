# World generation source tools

`tools/vanilla.py worldgen` provides a source registration catalog, a bounded replay of one compiled Reset checkpoint, and fresh original/runtime execution of one isolated Grass Wall pass. These workflows do not generate a complete world or prove complete pass parity.

```sh
python tools/vanilla.py worldgen capture --source-root /path/to/decompiled/1458 --output .cache/worldgen-tools/catalog.json
python tools/vanilla.py worldgen check --reference .cache/worldgen-tools/catalog.json --source-root /path/to/decompiled/1458 --report .cache/worldgen-tools/check.json
python tools/vanilla.py worldgen mutate --reference .cache/worldgen-tools/catalog.json --report .cache/worldgen-tools/mutations.json
python tools/vanilla.py worldgen replay-reset --tests-dll tests/TerraRuntime.Tests/bin/Release/net11.0/TerraRuntime.Tests.dll --output-dir .cache/worldgen-tools/reset-replay
python tools/vanilla.py worldgen run-pass --assembly /path/to/TerrariaServer.exe --output-dir .cache/worldgen-tools/grass-wall --seed 1458
python tools/vanilla.py worldgen compare-pass --official .cache/worldgen-tools/grass-wall/official.json --runtime .cache/worldgen-tools/grass-wall/runtime.json --report .cache/worldgen-tools/grass-wall-comparison.json
```

Use `py -3` instead of `python` on Windows if needed. Capture/output/report paths must be new. `--repo` on catalog check/mutate selects another checkout. The replay requires an existing compiled test artifact; it does not build one.

## Registration catalog

Capture reads the official 1.4.5.8 source, checks release/version, records raw source and tool hashes and extracts all 109 lexical registrations before special-seed filtering. It resolves pass names, order, outer registration conditions, legacy/typed weights and the dual-dungeon disabled-name list. Comments and literals cannot create fake declarations; unknown outer syntax or conditional compilation is refused instead of guessed. Delegate implementations are identified by hashes, not copied into the capture.

Check compares the runtime `PassCatalog1458` name sequence, registration/method fingerprints and dual-dungeon filter. The runtime catalog does not store individual weights or the full registration predicates, so those are explicitly **unrepresented** or **recorded but not executed**. Generated cells and RNG are **not executed** by this check. Without `--source-root`, current source freshness is explicitly not rechecked; with it, changed source inputs reject the reference. The capture is a provenance record, not an attestation of behavior.

Mutation checks require a green baseline and then corrupt five named metadata cases in memory: first-pair order, final name, body fingerprint, filter entry and the pass-array declaration. A red baseline never becomes a green mutation result. Production catalog files are never modified.

The initial real-source audit on 2026-10-28 intentionally returns a diagnostic failure: all 109 names/order, registration-expression/name hashes and dual-dungeon filtering agree, but the current source `AddPasses` method hashes to `1e073711995e1eb38578d7d23e991cf5005fefaf573188ebbdcec261f945347f`, while the production catalog records `72e757af1fb0a7b565d397bbb0f9fd1d32a2960838ba636c593e122645ab9672`. The unchanged earlier source reader independently reproduces the new hash. A fresh CI-style decompile with the historical pinned Windows assembly/ILSpy version also reproduces it. The retained log of [historical CI run 33268133716](https://github.com/VegaKernel/TerraRuntime/actions/runs/33268133716) really printed the old hash; its full method-body artifact has expired. Other logged methods show decompiler output differences beyond whitespace, but they do not establish equivalence of the unavailable historical `AddPasses` body. Simple whitespace/comment transformations did not bridge the hashes. The exact cause remains unresolved: no runtime constant was changed and agreement of names is not treated as body equivalence.

## Fresh isolated-pass comparison

`run-pass` builds the current offline `PassProbe` with warnings as errors, validates the official executable and dependencies against the admitted reference, and captures both engines in separate processes. It selects the actual registered official `SurfaceDirtWallsToGrassWalls` / `Grass Wall` pass and calls `GenPass.Apply`; the runtime capture calls the current `GrassWallPass1458.Apply`. Neither pass implementation is copied into the tool.

Both engines receive the same explicit synthetic 160×112 predecessor with a stone/wall patch, world context and RNG state. The observations contain all 17,920 cells before and after in the complete named 16-byte `WorldTile` projection, plus the physical RNG cursor and all 56 state slots. In 1.4.5.8, `WorldGen.genRand` aliases `Main.rand`; the second RNG cursor is derived, not an additional stored field. The comparator rejects different input cells, context, scenario or RNG before comparing every output field and reporting the first cell/field and RNG difference. Missing, malformed or unsupported represented state is rejected.

`run-pass` records source/tool hashes and the executed DLL closure, checks them before/after, and retains build/capture logs and rejected evidence. `compare-pass` only compares the supplied observations and records the hashes of the consumed files; it does not rebuild or recheck their correspondence to current source. The fixture exercises wall staining and deliberately excludes dirt-pocket/`SpreadGrass` branches. It does not represent a naturally generated prefix, raw `Tile` wall-frame metadata, the `WorldGenerator.RunPass` manifest-tail RNG draw, Native execution or whole-world parity.

The actual 2026-10-28 qualification used fresh `run-pass` executions for seeds 1458, 42 and 8675309: all 17,920 output cells and RNG states matched, with 1,832 source/tool inputs and six DLLs unchanged in each run. Two actual runtime controls confirmed detection: `--control omit-pass` produced 83 differing cells and an RNG difference at seed 1458; `--control extra-rng` preserved cells but changed RNG. Both controls correctly return a rejected comparison and nonzero CLI exit. Their evidence is retained under `.cache/worldgen-run-pass-root-01`, `-42`, `-8675309`, `-omit` and `-extra`; these local captures are not portable golden files.

## Reset replay and the next differential step

Replay invokes exactly `VanillaReferenceDifferentialCheckpoint1458Tests.Official_reference_seed_8675309_keeps_pinned_reset_dungeon_side_and_anchor` through the supplied xUnit DLL. It saves stdout/stderr, audits actual XML for exactly that one passing test with no failures/errors/skips, hashes the complete discovered DLL closure before/after and retains rejected evidence. Timeout is $60\,\mathrm{s}$. It refuses existing output directories. A successful replay proves the supplied compiled artifact still passes the existing official Reset checkpoint; it does not prove that the artifact matches current source, execute the original game again, compare every world cell or run all generation passes.

The official `WorldGenerator.Controller` already offers pause-after-pass and `WorldGenSnapshot` create/restore. That is a useful foundation for a future real-prefix, individual-pass oracle. A snapshot contains manifest/GenVars and canonical tile/chest/tile-entity data; restore deactivates NPCs and does not reproduce arbitrary raw in-memory state. Intermediate manifest hashes can be absent, and their comparison tolerates absence, so a future oracle must require explicit supported before/after state, its own canonical cell hashes and observed RNG endpoints. `Main.rand` is recreated from the generator seed before each pass; this must not be confused with continuity of other RNG owners.

`TerraRuntime.WorldCompare` checks structural budgets of final `.wld` files. It is useful for silhouettes/histograms and related checks, but it is not an exact first-cell/RNG differential. No general exact-world oracle or automatic C# algorithm translation is introduced here.
