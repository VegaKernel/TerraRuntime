# Moon Lord death lifecycle

[Русский](../ru/moon-lord-death-sequence.md) · [NPC families](npc-behavior-families.md) · [NPC parity roadmap](../roadmap/npc-ai-parity.md)

## Implemented boundary

The first lethal core hit enters `ai[0] = 2`, restores life and makes the core invulnerable at the shared damage boundary. The authoritative AI then advances the death clock even when every player has disconnected or died. Death motion uses the pinned interpolation toward velocity $(0, -0.5)\,\mathrm{px/tick}$ with factor `0.98`.

`NpcAuthority` supplies the combat pipeline as the NPC executor's post-commit sink. Effects run only after the exact NPC generation and revision have committed:

- At $60\,\mathrm{ticks}$, remove active projectiles `456/462/455/452/454` through the projectile despawn/replication boundary and deactivate every True Eye (`NPC 400`). This is deliberately a global type scan, as in the official source. Other projectiles and ordinary NPCs remain active.
- At $600\,\mathrm{ticks}$, set core life to zero and invoke the existing imported-loot/progression boundary. Record the Moon Lord milestone in `RuntimeWorldProgressionMutations`, despawn the core and publish its terminal NPC state. No synthetic `packet 28` strike is emitted for timer expiry.
- Head, hands and True Eyes whose `ai[3]` slot no longer contains a core expire through the ordinary NPC lifecycle. They do not adopt another active core. Terminal parts cannot plan new projectiles.

```mermaid
sequenceDiagram
    participant Damage as Damage executor
    participant AI as NPC AI
    participant Store as NPC store
    participant Death as Combat pipeline
    participant Save as Progression journal
    Damage->>Store: First lethal hit enters state 2
    AI->>Store: Commit death tick 60
    AI-->>Death: Committed snapshot
    Death->>Death: Remove attacks and True Eyes
    AI->>Store: Commit tick 600 with life zero
    AI-->>Death: Committed snapshot
    Death->>Save: Mark MoonLord milestone
    Death->>Store: Despawn exact core generation
```

The journal uses the existing canonical `.wld` save pipeline; the change adds no new file layout or omitted runtime-only progression field. Entity scans use buffers bounded by the configured NPC/projectile tables. Stale generation or revision callbacks cannot alter replacement entities or progression.

## Distance teleport

After pursuit, a living-target core in state `0/1` teleports only when its distance from the target center is strictly greater than $2400\,\mathrm{px}$. It enters state `-2` without resetting the timer and shifts to $150\,\mathrm{px}$ above the player. The same offset moves each active retained shell slot, in order, followed by every active True Eye globally. Repeated slot references retain repeated translations. Intro can allocate the shell and teleport it in the same step; the offset excludes subsequent core world movement.

Committed effects have an optional after-spawn phase, guarded by the exact core generation/revision after allocations and slot links. The world-motion wrapper forwards that phase. Authoritative translations emit `ForcedUpdate`, which the existing replication registry encodes as `packet 23` without motion-cadence suppression; baseline state and telemetry use the committed position. No network callback mutates NPC state.

Evidence includes 48 unmodified original server AI calls across the exact threshold, four directions and protected/exposed/intro completion states. Tests compare core and peer positions, AI and next RNG, plus emitted packet coordinates while the ordinary motion cadence is active. Two cases add the production world-motion wrapper; one replaces the core during commit. Disabling teleport or forced synchronization fails 26 tests each; removing the post-allocation generation guard fails the replacement test. This does not establish complete hand/head/True Eye attack AI or whole-encounter parity.

## Evidence and limits

Initialization sets `localAI[3]` and enters intro while preserving the incoming timer, even when the incoming state was death or departure. Intro and teleport return keep velocity unchanged and complete only when the incremented timer equals `60`. On completion, pursuit runs in the same tick. Intro creates the three parts even if target loss then enters departure; allocation retains the exact slots. Spawn coordinates use the core's center before world movement, truncated before the offsets, and new parts retain vanilla's unassigned target `255`.

The shared NPC AI stream now consumes the original core sound decision before initialization, including its conditional second draw, and the teleport-return draw whose result vanilla immediately overwrites. The retained fixture covers 384 direct original server AI calls with actual `NewNPC`, two RNG seeds (including the sound branch), fractional positions, initialized/uninitialized cores and absent/present players. State, velocity, slots, child positions/AI/target and next RNG result match through the executor; a separate world-motion test protects spawn ordering. Restoring the previous implementation fails all 385 tests. Death-presentation RNG consumption and complete cross-system random-stream parity remain open.

Mounted players remain in the authoritative NPC target candidates, including server-owned players. Vanilla `NPC.TargetClosest` filters activity, death and ghost state, not mount type. Mounting therefore cannot make a living player disappear from the core's departure check or from ordinary NPC targeting. Mount-specific player hitbox geometry remains a separate parity concern.

The departure path (`ai[0] = 3`) advances without living targets. On loss of the last player, the core performs its final pursuit step before entering departure with timer zero. Departure interpolates toward `(direction, -0.5)` with factor `0.98`. At $40\,\mathrm{ticks}$ it removes the five attack types and all True Eyes globally; projectile removal repeats `packet 27` and clears the server baseline without emitting `packet 29`. At $60\,\mathrm{ticks}$ it removes all hands, heads and True Eyes, then the departing core, clears `LunarApocalypseIsUp`, and requests the existing world-info broadcast. It awards no victory or loot. The journal retains the loaded event baseline and captures explicit boolean changes for the existing save-header patcher.

Departure evidence includes 120 direct original AI calls for velocity/timer/entity/event state, twelve target-loss pursuit cases, an executor-driven departure, stale-generation protection, packet27 repetition and join-baseline removal, and 32 original header combinations toggled in both directions. Header comparisons align only the original save timestamps in the expected fixture and require exactly one changed source byte. Full event/announcement behavior, remaining shared RNG consumption still require further verification; these checks do not establish full AI parity.

`LateHardmodeBossParityTests` covers the death motion, terminal clock boundary and absent-player case; these regressions fail on the previous implementation. `MoonLordDeathSequenceTests` drives the real executor and post-commit pipeline through the entire clock, verifies cleanup, progression, orphan expiry and slot reuse. Existing world-progression tests cover the persisted milestone layout.

`tools/ci/check_moon_lord_death_source.py` independently checks `NPC.AI_077_MoonLordCore`, `AI_078_MoonLordHands`, `AI_079_MoonLordHead` and `AI_081_TrueEyeOfCthulhu` from TerrariaServer `1.4.5.8`, with the executable SHA-256 pinned. The dedicated source-contract workflow repeats the check with ILSpy `11.0.0.9375`; game source stays outside version control.

This closes bounded lifecycle and death-loot gaps. Complete global event/announcement behavior, owner generation tracking across core slot reuse, and broader official-server differential scenarios remain open. Presentation-only death effects, including projectile `622`, are outside the server-authoritative claim. `FullVanillaAiParity` remains false.

## Core pursuit

Protected and exposed pursuit now refresh `TargetClosest(false)` every AI tick. Outside the $20\,\mathrm{px}$ dead zone, steering uses displacement minus current velocity, vanilla reversal acceleration and a half blend with the previous velocity. Inside the dead zone it preserves velocity. `MoonLordCoreMotionTests` retains independently captured hashes of 2,000 calls to the unmodified official `AI_077_MoonLordCore` in exposed state, covering exact velocity bits, target and AI state with one or two active players. Removing movement correction fails all fifteen focused tests; removing target refresh fails all ten differential cases. Degenerate zero steering preserves finite runtime velocity; the remaining full-core state/RNG contract still requires separate parity work.

## Exact shell slots

Successful shell allocations retain the two hand slots and head slot in the core's `localAI[0..2]`, through the existing generation-checked post-commit spawn boundary. Missing allocations retain a negative sentinel. While the core is protected (`ai[0] = 0`), a missing slot, inactive part or wrong NPC type removes the core directly, even without a live player. This does not run boss loot or record a defeat. A matching owned part in another slot cannot replace the original part. The three retained parts must all enter state `-2` to open the core; missing-shell removal does not apply to an already exposed or dying core. The source checker and full executor tests cover these boundaries, including partial allocation under a full NPC table.

## Death loot and participation

The committed terminal tick now executes the Moon Lord table. Classic drops Portal Gun, 70–90 Luminite and two distinct weapons from the ten-option `1.4.5.8` pool, including Moon Lord Whip. Mask and Meowmere Minecart retain their separate chance rolls. Expert/Master bags use addressed `packet 90` delivery to active participants; Master adds the relic and per-participant pet rolls. The trophy roll precedes the boss table in every mode. Common-item luck, stack rolls, weapon selection and intervening item materialization preserve source ordering.

Accepted hits on the head or hands credit the active core in the exact `ai[3]` slot, through both inbound `packet 28` and server-owned damage. Invalid or non-core owner slots do not credit an unrelated core. No loot is emitted on the initial lethal strike or before tick 600; stale terminal callbacks cannot duplicate delivery. Public items use the existing `packet 21` path and instanced bags retain the existing bounded slot lease.

The eighteen drop definitions and natural prefixes were checked against the unmodified official Linux dedicated-server assembly loaded by a local .NET probe on Windows. The retained tests pin the prefix and next RNG value for 100 seeds per item (1,800 comparisons); this is assembly differential evidence, not a Linux NativeAOT execution claim. `VanillaMoonLordLootTests` covers all 90 ordered distinct weapon choices, delivery/RNG interleaving and difficulty tables. `MoonLordLootPipelineTests` exercises real death ticks, both damage paths, participant routing and stale callbacks. Removing the loot dispatch makes the new pipeline regressions fail.

These are drop definitions only: opening bags, using or placing the new items, global coin/heart rules and full weapon gameplay remain separate work.
