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

The default fixture covers one actual AI3 lane. It does not cover AI7, emitted projectiles, collision/gravity, buffs, death, spawning/despawning, networking or a natural world lifecycle. Calling the original Linux-distributed assembly on a Windows CoreCLR host does not establish Linux OS or NativeAOT compatibility. These tools do not promote NPC compatibility classifications or replace existing independent fixtures and gameplay acceptance gates.

## Dryad AI7 continuation

```sh
python tools/vanilla.py npc run --assembly /path/to/TerrariaServer.exe --output-dir .cache/npc-tools/dryad --seed 1458 --profile ai7-dryad
python tools/vanilla.py npc compare --official .cache/npc-tools/dryad/official.json --runtime .cache/npc-tools/dryad/runtime.json --profile ai7-dryad --report .cache/npc-tools/dryad-comparison.json
```

`ai007-dryad-stardew-v1` supplies the state of an already active Dryad Stardew animation. It executes all 480 animation ticks and sixteen subsequent idle ticks through original `NPC.UpdateNPC`, including `AI_007_TownEntities` and `FindFrame`, versus the current `ServerRuntimeState.Tick` town phase. Both start from the same complete named input projection and physical RNG on one dry stone floor, with no connected players or world clock. The runtime's surface is explicitly attached rather than inferred. Original dedicated-server buffers and disconnected client records permit real packet serialization without a listener or external network connection.

The separate strict profile records motion and old motion, AI/local AI, life/defense/counters, home state, frame index, binary64 frame counter and the full physical RNG at every one of the 496 frames. It observes actual runtime packet `91` emote values and original `EmoteBubble` records, including event timing. Frames cannot be reordered or omitted, and reads are bounded to $8\,\mathrm{MiB}$. Omission and extra-RNG controls invoke real changed executions. This profile does not normalize any state or apply the AI3 `JustHit` carrier.

The first fresh pair exposed a genuine missing implementation: the original advanced the animation and emitted emotes at zero-based frames `59` and `149`; the old runtime refused state `24` and froze all 496 frames. The new normal regression also failed on freshly built old production at the first position comparison. This led to the bounded [Dryad continuation](npc-ai3-ai7-runtime-1458.md#dryad-stardew-animation-continuation) in the existing town owner. Initiating the animation, general AI7, state `14` magic combat, wet residents, natural worlds and multiplayer protocol parity remain outside this fixture.

The fixed backend prototype matches every represented state/RNG frame and both emote events at seed `1458`. Actual omission and extra-RNG runs fail at frame `0` on motion and RNG respectively. The unchanged new regression and complete locomotion/conversation neighbors pass `74/74`, retaining the non-Dryad state `24` negative cases. The old/fixed test DLLs differ, so the causal regression claim identifies the unchanged test source rather than an identical test binary. Raw evidence remains under `.cache/npc-town-applied-old` and `.cache/npc-town-applied-fixed`; final combined source-guarded qualification and production gates are recorded separately.
