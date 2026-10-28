# NPC behavior source tools

`tools/vanilla.py npc` builds an offline adapter and executes the pinned Terraria 1.4.5.8 AI body and the current runtime behavior on the same explicit fixture. It compares consecutive frames rather than constructor metadata or a previously compiled test. The adapter references production projects; no shipping project loads Terraria or depends on the probe.

```sh
python tools/vanilla.py npc run --assembly /path/to/TerrariaServer.exe --output-dir .cache/npc-tools/ice-golem --seed 1458
python tools/vanilla.py npc compare --official .cache/npc-tools/ice-golem/official.json --runtime .cache/npc-tools/ice-golem/runtime.json --report .cache/npc-tools/comparison.json
python tools/vanilla.py npc run --assembly /path/to/TerrariaServer.exe --output-dir .cache/npc-tools/omitted --seed 1458 --control omit-step
python tools/vanilla.py npc run --assembly /path/to/TerrariaServer.exe --output-dir .cache/npc-tools/extra-rng --seed 1458 --control extra-rng
```

Use `py -3` on Windows if needed. Output directories and report files must be new. A normal match returns zero; a behavior difference or invalid input returns nonzero. The mutation controls intentionally produce rejected comparisons. `compare` validates and hashes the supplied observations; it does not rebuild or prove they correspond to current source. `run` additionally records source/tool pins, build/capture logs, the complete executed DLL closure and official assembly/dependency pins, checking them before and after execution.

## Represented scenario

`ai003-ice-golem-airborne-v1` executes sixteen consecutive type `243` Ice Golem frames. The official entry is `NPC.AI_003_Fighters`; the runtime entry is `RuntimeNpcAiStateExecutor.Tick` with `VanillaNpcTargetingAiStepper`, including the committed projectile-attack timer tail. There is one active player, an empty tile grid, a Classic dedicated-server context at night and an explicit missing-health NPC state. Positive vertical velocity prevents shots and ground-contact branches while horizontal acceleration, targeting, attack timers and hit-dependent random draws remain active.

Positions and old positions remain fixed in this AI fixture; velocity, AI and local AI carry into the next call. Before frames `2`, `5`, `9` and `13`, the carrier sets `justHit`. Every observation contains the full named NPC projection, all four AI/local-AI slots, and the complete physical `Main.rand` state: all 56 seed-array slots and its cursor. Floats use canonical eight-digit hexadecimal binary32 bits and compare exactly. Seed equality alone never qualifies two inputs as comparable.

The direct AI methods have a caller-boundary distinction: the runtime proposal retires `JustHit`, while official `AI_003_Fighters` leaves it for the later `NPC.Update` tail. The first direct-call comparison exposed this difference. The named carrier therefore applies only that source-backed pulse retirement after the official AI call. Provenance preserves all sixteen actual `rawAfterAiJustHit` flags and names the operation in `carrierRetirement`; the validator requires the official pulse flags and runtime cleared flags. No velocity, timer or random state is normalized. This is AI plus the explicit pulse retirement, not execution of the full `NPC.Update` method.

The schema refuses unknown/missing fields, incomplete RNG arrays, nonfinite or noncanonical float bits, reordered/missing frames, unsafe DLL identities, and a frame preimage that does not continue the preceding state. Different valid initial state or context returns `incomparable-input`, separately from an NPC or RNG difference.

The initial backend qualification on 2026-10-28 built with zero warnings/errors and matched all sixteen frames at seed `1458`, including the physical RNG endpoint at cursor `21`. Actual omitted-step and extra-RNG executions were rejected at frame `0` for velocity and RNG respectively. The earlier direct-AI pulse mismatch is retained separately rather than overwritten. Prototype captures and consumed hashes are under `.cache/npc-oracle-dev/carrier-comparisons.json`; these qualify the adapter and its controls, not the common runner's later across-build source guard.

Final common-runner qualification rebuilds the adapter with the unknown-field guards and matches all sixteen frames for seeds `1458`, `42` and `8675309`. Both real controls reject, with no execution/provenance errors. Source/tool and DLL guards stay exact; see the [three-domain verification record](../verification/three-domain-oracles-20261028.json).

## Limits

This fixture covers one actual AI3 lane. It does not cover AI7, emitted projectiles, collision/gravity, buffs, death, spawning/despawning, networking or a natural world lifecycle. Calling the original Linux-distributed assembly on a Windows CoreCLR host does not establish Linux OS or NativeAOT compatibility. It does not promote NPC compatibility classifications or replace existing independent fixtures and gameplay acceptance gates.
