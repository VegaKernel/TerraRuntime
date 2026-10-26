# World progression and event state

[Русский](../ru/world-progression-events.md) · [Gameplay decomposition roadmap](../roadmap/gameplay-decomposition-and-catalogs.md)

TerraRuntime projects validated `.wld` runtime metadata into gameplay-owned progression and event views. Persistence field order and raw invasion values stay at the world-file boundary.

## Permanent progression

`VanillaWorldProgressionId` names 36 permanent TerrariaServer 1.4.5.8 milestones: bosses, invasions already defeated, Hardmode, celestial pillars, Old One's Army tiers and source-backed unlock events such as a smashed Shadow Orb. `VanillaWorldProgressionState.IsComplete` queries an immutable runtime snapshot without exposing packed persistence bits.

`WorldFileRuntimeMetadata.Progression` performs the explicit field-to-milestone projection. Event activity, weather and temporary holidays are not mixed into this state.

## Runtime mutation ownership

Each live `WorldRuntime` owns one `RuntimeWorldProgressionMutations` journal for progression produced after load. The same journal is passed explicitly to NPC/town gameplay and to persistence snapshot capture, so two runtimes cannot accidentally converge on mutable progression through a process-static lookup. Baseline facts loaded from `.wld` remain distinguishable from newly produced mutations, allowing save patching to persist only state that this runtime actually changed.

Live packet `7` overlays all 36 permanent milestones from that journal on the immutable loaded baseline, using the original bit positions. The existing blue/yellow town-slime and Truffle unlock mutations are projected the same way. This makes already accepted boss, Hardmode, invasion and pillar victories visible to existing clients and late join before a save; it adds no missing victory producer. Independent official `NetMessage.SendData(7)` one-bit captures cover every milestone and the three unlocks.

Temporary Moon victory seasons use separate nullable live/save overrides. A known `false` clears a stale saved Today flag; an omitted override or scalar host clock without all four seasonal baseline flags preserves the original header value. The save patcher locates the two booleans through the complete validated header parser after its variable TreeTops fields and changes only explicitly supplied flags. Existing prepared-cache season values already cover these fields; parser cursors are recomputed from the raw header for every patch. See [Moon scores and temporary seasons](moon-season-progression-1458.md).

## Active events and invasions

The existing `ShadowOrbSmashed` journal producer is now saveable: the header patcher owns its verified byte immediately after `downedPirates`. Detached snapshots, cold load and prepared cache preserve the accepted flag; packet `7` already projects the same journal bit. This closes the existing Skyblock Eater progression handoff without adding a tile-destruction producer or claiming natural invasion scheduling.

`VanillaWorldInvasionId` pins the official five-value invasion range: none, Goblin Army, Snow Legion, Pirate Invasion and Martian Madness. Unknown persisted values project to `Unknown` and fail closed instead of becoming a valid gameplay event.

`VanillaWorldEventState` separately exposes Blood Moon, Eclipse, Slime Rain, Party, Lantern Night, Sandstorm, Halloween and Christmas activity. Manual/genuine and today/forever persistence variants are normalized into one semantic active state.

## World time identity

`VanillaMoonPhase` names the exact eight-value Terraria 1.4.5.8 moon cycle. `VanillaMoonPhases` validates persisted primitives and owns wraparound, so the authoritative runtime clock does not compare or reset unexplained raw phase numbers. Persistence converts the typed value back to a byte only at the world-file patch boundary.

## Capability boundary

The ordinary invasion runtime owns Goblin, Frost Legion, Pirate and Martian counters, movement, warning cadence and completion milestones. Client requests `-1`, `-2` and `-3` require a selected server-funded item debit while idle. Client `-7` is refused: a Martian Probe needs its own authoritative producer. Start sizing uses active physical slots with source base maximum health at least 200, including dead census participants. Unknown health, unsupported metadata or stale owners refuse atomically. Debit, `UnifiedRandom` and event state adopt before observers. Initial replication remains `7` then the source `78` indicator, distinct from ordinary progress synchronization. See [paid invocation scope](paid-event-invocation-1458.md).

Socket bootstrap reads one immutable owner-published snapshot. `packet 6` receives live `7` then optional invasion `78`; an inactive ordinary invasion emits no progress frame. The subsequent `packet 8` response starts with live `7` and section status, preserving the source distinction between the two requests. A runtime transfer captures a fresh destination snapshot on its owner thread and sends `7` then optional `78` before destination sections and `49`. Frame encoding uses the detached scalar snapshot without reading mutable owners on the socket thread. Loaded zero-start progress stays signed: join synchronization uses maximum 1, while death credit preserves the source maximum 0.

Canonical admitted Goblins receive the invasion day-despawn context and source selection; unsupported Summoner selection is refused without substitution. Pirate selection retains ordinary choices and Captain/ship caps; the linked Captain/Ghost owner is active, while ship/cannons remain unadmitted. Frost/Martian natural selection and complete encounters remain open; Martian381 is definition-only. Existing ordered Goblin/Pirate loot is retained. Ten verified ordinary Goblin/Pirate families now adopt exact terminal removal, credit, loot and RNG before observers, then drain output in source order: loot, `78`, inactive `23`. Nested deaths, callback exceptions and replacements cannot replay accepted rewards or rewind newer event state. Possible or unknown post-Plantera Dungeon Spirit production refuses before HP until a retained producer exists; an earlier source-false predicate permits the ordinary path.

Captain death adopts its five invasion credits with drops, Ghost birth and parent retirement before observers; Ghost fade death contributes one credit through the same linked transaction. The producer also works outside an active invasion, including an absent optional invasion owner, without inventing credit. Size `5 → 0` completes on the next invasion lifecycle step while Ghost is still alive; size `6 → 1` requires the later Ghost death credit to reach zero. Raw counter/currentness and nested publication are independently guarded. The configured linked corpus verifies lifecycle scalar adoption and literal `98`, not a new whole `WorldRuntime` `98 → 82 → 7` replay. This retained death path is included in the accepted event-authority checkpoint: focused and full suites plus the Windows NativeAOT smoke paths pass; Linux Native remains unverified.

Completion adopts the counter state and permanent milestone before publication: progression `98`, announcement `82`, world `7`, then remaining movement warnings. The first-clear Lantern Night request is retained; its nightly consumer remains open. Detached checkpoints preserve the five invasion fields, four completion milestones and pending Lantern flag. Frost Legion is live-owned; unknown invasion identities preserve raw metadata and remain fenced. [Event clocks](event-clock-authority-1458.md) and [Moon death authority](moon-event-death-authority-1458.md) describe live saved flags, transient waves and their exact admitted scope. Full `Main.Update` scheduling and complete encounter parity remain open.

These types complete identity/state decomposition, not full event simulation. Starting/stopping conditions, waves, spawn pools, rewards, world transitions, announcements and replication remain separate source-backed implementations. Reading a completed milestone never by itself grants those gameplay consequences.
