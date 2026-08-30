# Vanilla NPC and AI parity roadmap

This roadmap tracks exhaustive TerrariaServer 1.4.5.8 NPC behavior separately from the completed D4 decomposition architecture. A checked decomposition box proves ownership and extension boundaries; it never means that all `NPCID.Count = 697` content identities, negative net variants, AI branches or side effects are implemented.

The executable truth is `VanillaNpcAiCoverageCatalog`. `FullVanillaAiParity` remains false for every currently admitted NPC.

## Current authoritative slices

| NPC | definition | targeting/state | physics | special slice | full AI parity |
|---|---:|---:|---:|---|---:|
| Blue Slime | yes | partial | partial | slime engagement/jump cadence | no |
| Demon Eye | yes | partial | partial | flying-eye collision response | no |
| Zombie | yes | partial | partial | profiled AI_003 traversal/check-active/event door pressure | no |
| Eye of Cthulhu | yes | partial | partial | phases/dashes and Servant spawn intents | no |
| Servant of Cthulhu | yes | partial | partial | source-backed flyer pursuit | no |
| Skeleton | yes | partial | partial | profiled AI_003 `1.5f` traversal/check-active/event door pressure | no |
| King Slime | yes | partial | partial | teleport environment and minion intents | no |
| 23 additional hostile AI_001 types | yes | partial | partial | typed timer bonus/jump-window profiles | no |
| 12 additional hostile AI_002 types | yes | partial | partial | typed normal/special/enraged steering profiles | no |
| 17 additional hostile AI_005 types | yes | partial | partial | typed pursuit/bounce/water profiles | no |
| 47 AI_006 worm identities | yes | partial | partial | typed family roles, chain spawn and motion | no |

The admitted AI_001 set now covers Mother, Lava, Dungeon, Corrupt, Illuminant, Toxic Sludge, Ice,
Crimslime, both biome Spiked Slimes, Umbrella, Rainbow, masked/ribbon variants, Spiked, Sand,
Queen Slime blue/pink minions, Golden and Shimmer Slime. Negative net identities `-1..-10` have
source-backed type, scale and combat defaults; packet projection and spawn materialization resolve
the signed identity instead of silently using the positive type defaults.

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
- [x] carry persisted Blood Moon state into the authoritative NPC world-motion stage;
- [x] source-shaped target-local Graveyard scan (`170x125`, threshold `28`, Sunflower compensation) and one-in-sixty fighter door-pressure roll;
- [x] accumulate Blood Moon/Graveyard door and tall-gate pressure to the vanilla threshold and emit typed opening intents through an explicit world-mutation sink boundary;
- [x] import TerrariaServer 1.4.5.8 `WorldGen.OpenDoor` object mutation: locked-Dungeon-door rejection, `1x3 -> 2x3` frame/style transform, row block paint/coating transfer and source `tileCut`/stalactite/drip clearance rules;
- [x] import packet-19 door/tall-gate wire contract and server-authored playing-peer replication boundary;
- [x] import the `ShiftTallGate` `388 -> 389` object mutation behind an explicit `Collision.EmptyTile(ignoreTiles:true)` actor-occupancy boundary, failing closed when that boundary is unavailable;
- [x] import the pinned AI_003 door-pressure policy: exact restricted-reset type set, `Main.getGoodWorld` Blood Moon suppression, `insideUnbreakableWalls +6`, type `27 +1`, types `31/294/295/296 +6`, type `460` force-open and type `26` destroy-door disposition;
- [x] carry persisted `GetGoodWorld` into the admitted Zombie/Skeleton event projection so Blood Moon no longer incorrectly grants accumulation there;
- [ ] wire live player/NPC occupancy into the tall-gate boundary and enable the authoritative production opening sink in default server composition;
- [ ] project live `insideUnbreakableWalls` target state, route concrete future AI_003 types into the pressure policy and implement type `26` authoritative door destruction before admitting those special branches;
- [ ] partition and import remaining AI_003 movement parameter families;
- [ ] type-specific attacks, transformations, projectiles and spawn effects;
- [ ] differential scenarios for each admitted AI_003 subtype.

The current door layer is no longer guessing frame geometry. Normal-door mutation reproduces the pinned 1.4.5.8 `OpenDoor` transform, including locked-door rejection and the source clearance set; successful authoritative mutations can be represented exactly as packet 19. The exact AI_003 pressure/reset table is also executable, and the currently admitted restricted Zombie/Skeleton slice now receives the persisted `GetGoodWorld` suppression. Tall-gate type shifting is implemented too, but vanilla checks every gate cell against live player/NPC rectangles before opening. TerraRuntime keeps that actor query as an explicit boundary and fails closed without it. Special future fighter types remain fail-closed at production composition until concrete type routing, live inside-wall target state and the type-26 destroy side effect are authoritative.

## N2 — Common ordinary families

- [x] typed definitions and movement profiles for the hostile AI_001 catalog;
- [x] source-backed slime net variants `-1..-10` with effective spawn/packet defaults;
- [ ] remaining AI_001 projectile, item-containment, split, transform, seed and visual branches;
- [x] typed definitions, steering profiles and wet behavior for all hostile AI_002 identities;
- [x] source-backed AI_002 net variants `-38..-43` with effective spawn/packet defaults;
- [ ] AI_002 daylight despawn, Pigron line-of-sight phasing state and cosmetic branches;
- [x] typed definitions and classic pursuit/bounce/water profiles for hostile AI_005 identities;
- [x] admitted AI_005 size net variants for Eaters, Crimeras and Hornet families;
- [ ] AI_005 jitter, close homing, daylight flight, surface hugging and stinger projectiles;
- [x] typed AI_006 head/body/tail family relationships for Devourer, Giant Worm, Eater of Worlds and Bone Serpent;
- [x] source-backed worm head burrow/air steering and exact segment-gap follow primitives;
- [x] frozen-prepass runtime leader lookup and authoritative body/tail follow for admitted ordinary worm segments;
- [x] live solid/actuated/frame-important/deep-liquid world query wired into admitted worm-head steering;
- [x] incremental Devourer/Giant Worm/Bone Serpent chain spawning with post-allocation `ai[0]` linkage;
- [x] Digger, Seeker and Leech definitions, motion profiles and linked chain spawning;
- [x] Dune Splicer, Tomb Crawler and Blood Eel definitions, gaps, always-dig policy and linked chains;
- [x] Wyvern, Crawltipede and Cultist Dragon patterned chains plus Truffle/Stardust singleton worm profiles;
- [ ] Eater of Worlds chain length, split/death repair and complete synchronized lifecycle;
- [x] remaining AI_006 worm family definitions and profiles;
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
