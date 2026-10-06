# World progression and event state

[Русский](../ru/world-progression-events.md) · [Gameplay decomposition roadmap](../roadmap/gameplay-decomposition-and-catalogs.md)

TerraRuntime projects validated `.wld` runtime metadata into gameplay-owned progression and event views. Persistence field order and raw invasion values stay at the world-file boundary.

## Permanent progression

`VanillaWorldProgressionId` names 36 permanent TerrariaServer 1.4.5.8 milestones: bosses, invasions already defeated, Hardmode, celestial pillars, Old One's Army tiers and source-backed unlock events such as a smashed Shadow Orb. `VanillaWorldProgressionState.IsComplete` queries an immutable runtime snapshot without exposing packed persistence bits.

`WorldFileRuntimeMetadata.Progression` performs the explicit field-to-milestone projection. Event activity, weather and temporary holidays are not mixed into this state.

## Runtime mutation ownership

Each live `WorldRuntime` owns one `RuntimeWorldProgressionMutations` journal for progression produced after load. The same journal is passed explicitly to NPC/town gameplay and to persistence snapshot capture, so two runtimes cannot accidentally converge on mutable progression through a process-static lookup. Baseline facts loaded from `.wld` remain distinguishable from newly produced mutations, allowing save patching to persist only state that this runtime actually changed.

## Active events and invasions

`VanillaWorldInvasionId` pins the official five-value invasion range: none, Goblin Army, Snow Legion, Pirate Invasion and Martian Madness. Unknown persisted values project to `Unknown` and fail closed instead of becoming a valid gameplay event.

`VanillaWorldEventState` separately exposes Blood Moon, Eclipse, Slime Rain, Party, Lantern Night, Sandstorm, Halloween and Christmas activity. Manual/genuine and today/forever persistence variants are normalized into one semantic active state.

## World time identity

`VanillaMoonPhase` names the exact eight-value Terraria 1.4.5.8 moon cycle. `VanillaMoonPhases` validates persisted primitives and owns wraparound, so the authoritative runtime clock does not compare or reset unexplained raw phase numbers. Persistence converts the typed value back to a byte only at the world-file patch boundary.

## Capability boundary

The ordinary invasion runtime owns Goblin, Pirate and Martian counters, movement, warning cadence and completion milestones. Trusted negative `packet 61` requests `-1`, `-3` and `-7` enter the authoritative command phase. Start sizing uses active physical player slots with source base maximum health at least 200, including dead players. Source `UnifiedRandom` and all selected owners are validated before adoption; unknown imported health, unsupported loaded invasion state or a stale owner refuses the operation atomically. Generic requests start only while idle; `-7` always clears the delay and calls the Martian start branch. Initial replication is `packet 7` followed by the source `packet 78` indicator, distinct from ordinary progress synchronization.

Socket bootstrap reads one immutable owner-published snapshot. `packet 6` receives live `7` then optional invasion `78`; an inactive ordinary invasion emits no progress frame. The subsequent `packet 8` response starts with live `7` and section status, preserving the source distinction between the two requests. A runtime transfer captures a fresh destination snapshot on its owner thread and sends `7` then optional `78` before destination sections and `49`. Frame encoding uses the detached scalar snapshot without reading mutable owners on the socket thread. Loaded zero-start progress stays signed: join synchronization uses maximum 1, while death credit preserves the source maximum 0.

Canonical admitted Goblin actors receive the invasion day-despawn context and source-shaped spawn selection. NPC identity selection remains separate from actor admission: a selected unsupported Summoner is refused without substitution. Pirate and Martian natural spawn selectors and their complete encounters remain open; Martian381 remains definition-only. Seven ordered loot tables are independently verified, without granting those actors AI or item-use support. Supported Goblin/Pirate terminal deaths adopt their counter credit after loot and before `78` then inactive `23`; replaced or revived generations cannot receive a stale credit.

Completion adopts the counter state and permanent milestone before publication. The source order is progression `98`, announcement `82`, world `7`, then any remaining source movement warnings. The first-clear Lantern Night request is retained as a scalar flag; the later nightly RNG phase is not claimed. Checkpoints retain the five original invasion fields, the three completion milestones and the owned pending Lantern Night flag through the detached save image. Snow Legion and unknown loaded states retain their raw persistence representation while live simulation is fenced. These boundaries do not establish full `Main.Update` scheduling or complete invasion parity.

These types complete identity/state decomposition, not full event simulation. Starting/stopping conditions, waves, spawn pools, rewards, world transitions, announcements and replication remain separate source-backed implementations. Reading a completed milestone never by itself grants those gameplay consequences.
