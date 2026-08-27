# TerraRuntime agent rules

These instructions apply to every coding agent working in this repository.

Before non-trivial work, read the relevant parts of `docs/roadmap.md` and `docs/native-aot-baseline.md`. Follow the roadmap unless the requested change requires correcting it first.

## 1. Source-of-truth hierarchy

When sources disagree, use this order:

1. Locally decompiled official `TerrariaServer.exe` **1.4.5.8** for vanilla behavior, `.wld` layout, gameplay ordering, runtime constants and packet-handling semantics.
2. `VegaKernel/Multiplicity` **2.7.x** for protocol **326 / Terraria 1.4.5.8** typed packet implementation.
3. `bybrooklyn/terrustia` as an independent cross-check and source of testing/performance ideas.
4. TShock/OTAPI only for historical behavior, compatibility knowledge and exploit lessons.

Never infer a 1.4.5.8 detail from an older reference when the 1.4.5.8 decompile can answer it. Never let a secondary implementation override verified current vanilla behavior.

Decompiled game source is local reference material only. Do not commit it, game assets, game text, or copied method bodies. Reimplement behavior cleanly.

## 2. Understand first, then minimize

Trace the real code path end to end before changing it. After understanding it, prefer the first option that is sufficient:

- no change if the feature is unnecessary;
- reuse an existing helper/pattern;
- use the BCL or platform feature;
- use an already-admitted dependency;
- only then add the smallest new implementation that solves the real problem.

Prefer boring code, fewer moving parts and fewer dependencies. Do not add abstractions for hypothetical future needs.

For bugs, fix the root cause at the shared boundary rather than patching one observed caller. Inspect sibling callers and adjacent state transitions before declaring the bug fixed.

## 3. Evidence beats green lights

A green test suite is necessary, not sufficient.

Protocol/gameplay tests that use the same encoder/decoder on both sides can agree on the same mistake. For protocol, world-file and gameplay work, obtain independent evidence whenever practical:

- official 1.4.5.8 decompiled source;
- real official client/server traffic;
- real vanilla `.wld` files;
- golden bytes captured independently;
- differential behavior against the official dedicated server.

A regression test must be capable of detecting the regression. For a non-trivial bug fix, verify that the new test fails when the fix is removed or the old behavior is restored. A test that also passes on the broken implementation is not evidence.

Do not mark a roadmap item complete merely because code compiles or round-trips through our own implementation.

## 4. NativeAOT is a production constraint

TerraRuntime is NativeAOT-first. Every shipping project under `src/` is part of that contract.

Do not introduce runtime code generation, arbitrary managed DLL loading, reflection-driven assembly scanning or serializers without an explicit trimming/AOT contract. Prefer static registration, source generation and typed codecs.

Every new or upgraded production NuGet dependency must pass the dependency admission gate in `docs/native-aot-baseline.md`, including exercised Linux and Windows NativeAOT smoke paths. Successful restore or linking alone is insufficient.

## 5. Runtime ownership invariants

The authoritative game-loop thread owns mutable simulation state.

- Socket callbacks, timers, UI/control code and background workers do not mutate world/player/NPC/projectile/item state directly.
- Network input becomes owned typed commands before transient receive buffers are released.
- Background work consumes immutable snapshots or isolated buffers and returns explicit completion data through the authoritative command boundary.
- Preserve per-connection ordering where protocol semantics require it.
- Keep client-controlled work bounded. No client-controlled queue, allocation, decompression, hashing operation or declared length may grow without a hard ceiling.
- Do not put blocking disk/network work on the game thread.

When a deliberate simplification has a known scalability/correctness ceiling, document the ceiling and the intended upgrade trigger in a nearby comment.

## 6. World-file and persistence safety

Silent world corruption or data loss is worse than refusing to load/save.

- Treat unknown/newer `.wld` layouts conservatively. Structural readability does not imply save compatibility.
- Never write fields at assumed offsets unless the exact 1.4.5.8 layout/version rule has been verified.
- Preserve state deliberately. When adding persistent world state, make the save/load decision explicit rather than relying on default omission.
- Save through bounded snapshot handoff, background serialization/write and atomic replacement.
- A partial/truncated section must not silently delete unrelated valid state.
- Real-world round-trip tests are required as persistence support grows.

## 7. Security and trust boundaries

Treat every byte from a client as hostile input.

Validation, bounds checks, rate/accounting limits and failure isolation at trust boundaries are not optional cleanup work. Malformed input must produce bounded failure, not crash the server, skip shutdown saving or allocate attacker-chosen amounts of memory/CPU.

Keep protocol decode errors, rate limits, invalid connection state and gameplay rejection distinguishable in code and telemetry.

## 8. Performance work requires measurement

Do not merge an optimization because it "should be faster".

For meaningful performance changes, record before/after measurements on the same workload and world. Measure the thing being optimized, including allocation/GC effects where relevant.

Keep CPU time and wall time distinct. Do not compare phase data gathered from incompatible clocks. Prefer production-like stress and real process/socket tests when in-process benchmarks hide scheduler, I/O or backpressure behavior.

Do not use `unsafe`, pooling, custom allocators, parallel gameplay passes or exotic layouts without measured benefit and focused correctness tests.

## 9. Comments and constants

Comments explain reasoning that code cannot recover on its own: why a constant exists, which vanilla rule it represents, why an alternative was rejected, or what failure mode a guard prevents.

When a behavior/layout is derived from the official decompile, reference the relevant vanilla type/method (and version) in the comment or supporting test where useful. Do not paste decompiled implementation text.

Do not invent protocol IDs, file-format gates or gameplay constants. Verify them against the source hierarchy.

## 10. Definition of done

A non-trivial change is not done until the relevant checks are green:

- build with warnings as errors;
- focused unit/integration regression test;
- existing test suite;
- Linux NativeAOT publish + exercised smoke path;
- Windows NativeAOT publish + exercised smoke path;
- independent verification when the change is protocol/gameplay/world-format shaped;
- before/after measurement when the claim is performance-related.

If CI or a smoke path is red, fix it before stacking unrelated roadmap work on top.

When implementation changes what is actually supported, update the roadmap/status documentation in the same work so documentation does not describe a different server than the code.
