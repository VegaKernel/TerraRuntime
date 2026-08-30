# Vanilla NPC and AI parity roadmap

This roadmap tracks exhaustive TerrariaServer 1.4.5.8 NPC behavior separately from the completed D4 decomposition architecture. A checked decomposition box proves ownership and extension boundaries; it never means that all `NPCID.Count = 697` content identities, negative net variants, AI branches or side effects are implemented.

The executable truth is `VanillaNpcAiCoverageCatalog`. `FullVanillaAiParity` remains false for every currently admitted NPC.

## Current authoritative slices

| NPC | definition | targeting/state | physics | special slice | full AI parity |
|---|---:|---:|---:|---|---:|
| Blue Slime | yes | partial | partial | slime engagement/jump cadence | no |
| Demon Eye | yes | partial | partial | flying-eye collision response | no |
| Zombie | yes | partial | partial | profiled AI_003 traversal/check-active/door contact | no |
| Eye of Cthulhu | yes | partial | partial | phases/dashes and Servant spawn intents | no |
| Servant of Cthulhu | yes | partial | partial | source-backed flyer pursuit | no |
| Skeleton | yes | partial | partial | profiled AI_003 `1.5f` speed band/traversal/check-active | no |
| King Slime | yes | partial | partial | teleport environment and minion intents | no |

## N0 — Evidence and fail-closed admission

- [x] separate decomposition completion from exhaustive parity;
- [x] machine-readable per-NPC capability claims with no full-parity claims;
- [x] packet-23 sync for every admitted definition, including live hitbox sync anchors;
- [x] unknown definitions fail closed instead of inheriting an `aiStyle` implementation;
- [x] full local test suite green after reconciling King Slime and live-scale regressions.

## N1 — AI_003 fighter family

- [x] Zombie ordinary fighter slice;
- [x] Skeleton definition, distinct speed band, world physics, check-active and packet sync;
- [x] route admitted fighter speed, acceleration, stuck/despawn windows and terrain-jump values through explicit version-pinned profiles consumed by AI and world traversal;
- [ ] partition and import remaining AI_003 movement parameter families;
- [ ] source-backed door/tall-gate mutation conditions for Blood Moon, Graveyard and special targets;
- [ ] type-specific attacks, transformations, projectiles and spawn effects;
- [ ] differential scenarios for each admitted AI_003 subtype.

The profiled traversal slice deliberately preserves the previously verified ordinary Zombie/Skeleton values. The change removes generic constants from the behavior/world boundary; it does not claim additional subtype parity or Blood Moon/Graveyard door mutation behavior.

## N2 — Common ordinary families

- [ ] slime/net-variant catalog and type-specific AI_001 branches;
- [ ] flying-eye variants and AI_002 type branches;
- [ ] AI_005 flyers beyond Servant of Cthulhu;
- [ ] worm/segment ownership and synchronized parent-child lifecycle;
- [ ] bats, fish, casters, mimics, critters and event enemy families;
- [ ] spawn pool, biome, time, weather and progression eligibility.

## N3 — Bosses

- [ ] finish Eye of Cthulhu expert/master/seed branches and all irreversible effects;
- [ ] finish King Slime difficulty/seed branches, despawn, progression and loot integration;
- [ ] add remaining pre-Hardmode bosses with complete child/projectile ownership;
- [ ] add Hardmode, event and endgame bosses;
- [ ] boss bars, announcements, progression transitions and multiplayer targeting parity.

## N4 — Town, friendly and special NPCs

- [ ] town AI, housing and schedules;
- [ ] shops, happiness and progression-dependent inventory;
- [ ] rescue/transform states, pets, critters and catchability;
- [ ] shimmer, statue and special-seed behavior;
- [ ] persistence and reconnect state for every persistent NPC family.

## N5 — Completion gates

- [ ] version-pinned definition coverage for every live positive NPC identity and supported negative net variant;
- [ ] every admitted NPC has explicit targeting, AI, physics, networking, lifecycle, combat and death/loot disposition;
- [ ] official-server differential fixtures cover state/RNG ordering and irreversible effects;
- [ ] unsupported fallback count is zero for the claimed vanilla server profile;
- [ ] only then may `FullVanillaAiParity` become true.
