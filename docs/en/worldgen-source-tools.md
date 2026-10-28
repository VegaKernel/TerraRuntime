# World generation source tools

`tools/vanilla.py worldgen` adds two separate fast checks: a source registration catalog and a bounded replay of one compiled Reset checkpoint. Neither generates a world or proves complete pass parity.

```sh
python tools/vanilla.py worldgen capture --source-root /path/to/decompiled/1458 --output .cache/worldgen-tools/catalog.json
python tools/vanilla.py worldgen check --reference .cache/worldgen-tools/catalog.json --source-root /path/to/decompiled/1458 --report .cache/worldgen-tools/check.json
python tools/vanilla.py worldgen mutate --reference .cache/worldgen-tools/catalog.json --report .cache/worldgen-tools/mutations.json
python tools/vanilla.py worldgen replay-reset --tests-dll tests/TerraRuntime.Tests/bin/Release/net11.0/TerraRuntime.Tests.dll --output-dir .cache/worldgen-tools/reset-replay
```

Use `py -3` instead of `python` on Windows if needed. Capture/output/report paths must be new. `--repo` on catalog check/mutate selects another checkout. The replay requires an existing compiled test artifact; it does not build one.

## Registration catalog

Capture reads the official 1.4.5.8 source, checks release/version, records raw source and tool hashes and extracts all 109 lexical registrations before special-seed filtering. It resolves pass names, order, outer registration conditions, legacy/typed weights and the dual-dungeon disabled-name list. Comments and literals cannot create fake declarations; unknown outer syntax or conditional compilation is refused instead of guessed. Delegate implementations are identified by hashes, not copied into the capture.

Check compares the runtime `PassCatalog1458` name sequence, registration/method fingerprints and dual-dungeon filter. The runtime catalog does not store individual weights or the full registration predicates, so those are explicitly **unrepresented** or **recorded but not executed**. Generated cells and RNG are **not executed** by this check. Without `--source-root`, current source freshness is explicitly not rechecked; with it, changed source inputs reject the reference. The capture is a provenance record, not an attestation of behavior.

Mutation checks require a green baseline and then corrupt five named metadata cases in memory: first-pair order, final name, body fingerprint, filter entry and the pass-array declaration. A red baseline never becomes a green mutation result. Production catalog files are never modified.

The initial real-source audit on 2026-10-28 intentionally returns a diagnostic failure: all 109 names/order, registration-expression/name hashes and dual-dungeon filtering agree, but the current source `AddPasses` method hashes to `1e073711995e1eb38578d7d23e991cf5005fefaf573188ebbdcec261f945347f`, while the production catalog records `72e757af1fb0a7b565d397bbb0f9fd1d32a2960838ba636c593e122645ab9672`. The unchanged earlier source reader independently reproduces the new hash. A fresh CI-style decompile with the historical pinned Windows assembly/ILSpy version also reproduces it. Simple newline/whitespace transformations did not explain the old hash. Its origin remains unresolved: no runtime constant was changed and agreement of names is not treated as body equivalence.

## Reset replay and the next differential step

Replay invokes exactly `VanillaReferenceDifferentialCheckpoint1458Tests.Official_reference_seed_8675309_keeps_pinned_reset_dungeon_side_and_anchor` through the supplied xUnit DLL. It saves stdout/stderr, audits actual XML for exactly that one passing test with no failures/errors/skips, hashes the complete discovered DLL closure before/after and retains rejected evidence. Timeout is $60\,\mathrm{s}$. It refuses existing output directories. A successful replay proves the supplied compiled artifact still passes the existing official Reset checkpoint; it does not prove that the artifact matches current source, execute the original game again, compare every world cell or run all generation passes.

The official `WorldGenerator.Controller` already offers pause-after-pass and `WorldGenSnapshot` create/restore. That is a useful foundation for a future real-prefix, individual-pass oracle. A snapshot contains manifest/GenVars and canonical tile/chest/tile-entity data; restore deactivates NPCs and does not reproduce arbitrary raw in-memory state. Intermediate manifest hashes can be absent, and their comparison tolerates absence, so a future oracle must require explicit supported before/after state, its own canonical cell hashes and observed RNG endpoints. `Main.rand` is recreated from the generator seed before each pass; this must not be confused with continuity of other RNG owners.

`TerraRuntime.WorldCompare` checks structural budgets of final `.wld` files. It is useful for silhouettes/histograms and related checks, but it is not an exact first-cell/RNG differential. No general exact-world oracle or automatic C# algorithm translation is introduced here.
