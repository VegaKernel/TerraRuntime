# World progression and event state

[Русский](../ru/world-progression-events.md) · [Gameplay decomposition roadmap](../roadmap/gameplay-decomposition-and-catalogs.md)

TerraRuntime projects validated `.wld` runtime metadata into gameplay-owned progression and event views. Persistence field order and raw invasion values stay at the world-file boundary.

## Permanent progression

`VanillaWorldProgressionId` names 36 permanent TerrariaServer 1.4.5.8 milestones: bosses, invasions already defeated, Hardmode, celestial pillars, Old One's Army tiers and source-backed unlock events such as a smashed Shadow Orb. `VanillaWorldProgressionState.IsComplete` queries an immutable runtime snapshot without exposing packed persistence bits.

`WorldFileRuntimeMetadata.Progression` performs the explicit field-to-milestone projection. Event activity, weather and temporary holidays are not mixed into this state.

## Active events and invasions

`VanillaWorldInvasionId` pins the official five-value invasion range: none, Goblin Army, Snow Legion, Pirate Invasion and Martian Madness. Unknown persisted values project to `Unknown` and fail closed instead of becoming a valid gameplay event.

`VanillaWorldEventState` separately exposes Blood Moon, Eclipse, Slime Rain, Party, Lantern Night, Sandstorm, Halloween and Christmas activity. Manual/genuine and today/forever persistence variants are normalized into one semantic active state.

## World time identity

`VanillaMoonPhase` names the exact eight-value Terraria 1.4.5.8 moon cycle. `VanillaMoonPhases` validates persisted primitives and owns wraparound, so the authoritative runtime clock does not compare or reset unexplained raw phase numbers. Persistence converts the typed value back to a byte only at the world-file patch boundary.

## Capability boundary

These types complete identity/state decomposition, not full event simulation. Starting/stopping conditions, waves, spawn pools, rewards, world transitions, announcements and replication remain separate source-backed implementations. Reading a completed milestone never by itself grants those gameplay consequences.
