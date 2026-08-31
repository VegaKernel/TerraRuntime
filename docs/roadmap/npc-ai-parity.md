# Vanilla NPC and AI parity roadmap

This roadmap tracks exhaustive TerrariaServer 1.4.5.8 NPC behavior separately from the completed D4 decomposition architecture. A checked decomposition box proves ownership and extension boundaries; it never means that all `NPCID.Count = 697` content identities, negative net variants, AI branches or side effects are implemented.

The executable truth is `VanillaNpcAiCoverageCatalog`. `FullVanillaAiParity` remains false for every currently admitted NPC.

## Current authoritative slices

| NPC | definition | targeting/state | physics | special slice | full AI parity |
|---|---:|---:|---:|---|---:|
| Blue Slime | yes | partial | partial | slime engagement/jump cadence | no |
| Demon Eye | yes | partial | partial | flying-eye collision response | no |
| Zombie | yes | partial | partial | profiled AI_003 traversal/check-active/event door pressure | no |
| Eye of Cthulhu | yes | partial | partial | classic phases, Expert phase one/transformation, Servant RNG and source-ordered rapid-dash states | no |
| Servant of Cthulhu | yes | partial | partial | source-backed flyer pursuit | no |
| Skeleton | yes | partial | partial | profiled AI_003 `1.5f` traversal/check-active/event door pressure | no |
| King Slime | yes | partial | partial | teleport/despawn, Good World scaling, minions, death/progression and source-ordered loot semantics | no |
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
- [x] wire live player/NPC occupancy into the tall-gate boundary and enable the authoritative production opening sink in default `ServerRuntimeState` composition;
- [x] project live `insideUnbreakableWalls` target state via `VanillaWorldUnbreakableWallScan` (8×250 ray scan for wall 350, color ≥16);
- [ ] route concrete future AI_003 types into the pressure policy and implement type `26` authoritative door destruction before admitting those special branches;
- [ ] partition and import remaining AI_003 movement parameter families;
- [ ] type-specific attacks, transformations, projectiles and spawn effects;
- [ ] differential scenarios for each admitted AI_003 subtype.

The current door layer is no longer guessing frame geometry. Normal-door mutation reproduces the pinned 1.4.5.8 `OpenDoor` transform, including locked-door rejection and the source clearance set; successful authoritative mutations can be represented exactly as packet 19. The exact AI_003 pressure/reset table is also executable, and the currently admitted restricted Zombie/Skeleton slice now receives the persisted `GetGoodWorld` suppression and live `insideUnbreakableWalls` projection via `VanillaWorldUnbreakableWallScan` (8×250 ray scan for wall 350, color ≥16). Tall-gate type shifting is now fully wired through the production `RuntimeTallGateOccupancyProbe` (live `Collision.EmptyTile(ignoreTiles:true)` actor-rectangle checks) and the authoritative `RuntimeGroundFighterDoorOpeningSink` in default `ServerRuntimeState` composition. Special future fighter types remain fail-closed at production composition until concrete type routing and the type-26 destroy side effect are authoritative.

## N2 — Common ordinary families

- [x] typed definitions and movement profiles for the hostile AI_001 catalog;
- [x] source-backed slime net variants `-1..-10` with effective spawn/packet defaults;
- [ ] remaining AI_001 projectile, item-containment, split, transform, seed and visual branches;
- [x] typed definitions, steering profiles and wet behavior for all hostile AI_002 identities;
- [x] source-backed AI_002 net variants `-38..-43` with effective spawn/packet defaults;
- [x] AI_002 source-backed daylight discouragement/despawn and Pigron 300-tick line-of-sight phasing/re-entry state with live world collision/Graveyard queries;
- [ ] AI_002 presentation-only alpha/rotation/dust/sound branches and remaining type-specific transforms/effects;
- [x] typed definitions and classic pursuit/bounce/water profiles for hostile AI_005 identities;
- [x] admitted AI_005 size net variants for Eaters, Crimeras and Hornet families;
- [x] AI_005 source-ordered jitter, close homing, Bee/SmallBee acceleration ramp, daylight flight/despawn, surface Hornet damping, bounce minima and wet-rise movement;
- [x] ordinary AI_005 Probe laser and Blood Squid blood-shot/recoil side effects through generation-safe post-commit projectile intents;
- [ ] remaining AI_005 side effects: Hornet/Moss Hornet stingers require authoritative player stealth/item-animation state; Good World Eater spawn requires admitted NPC 666 defaults/lifecycle;
- [x] typed AI_006 head/body/tail family relationships for Devourer, Giant Worm, Eater of Worlds and Bone Serpent;
- [x] source-backed worm head burrow/air steering and exact segment-gap follow primitives;
- [x] frozen-prepass runtime leader lookup and authoritative body/tail follow for admitted ordinary worm segments;
- [x] live solid/actuated/frame-important/deep-liquid world query wired into admitted worm-head steering;
- [x] incremental Devourer/Giant Worm/Bone Serpent chain spawning with post-allocation `ai[0]` linkage;
- [x] Digger, Seeker and Leech definitions, motion profiles and linked chain spawning;
- [x] Dune Splicer, Tomb Crawler and Blood Eel definitions, gaps, always-dig policy and linked chains;
- [x] Wyvern, Crawltipede and Cultist Dragon patterned chains plus Truffle/Stardust singleton worm profiles;
- [x] Eater of Worlds classic/expert chain length and missing-link head/tail split repair;
- [x] pin AI_006 link lifecycle to official TerrariaServer 1.4.5.8 evidence: active-only Eater structural death gates, `aiStyle`-sensitive body split gates, source ordering and `ai[3]` root propagation;
- [ ] Eater of Worlds death/loot/progression and complete synchronized lifecycle;
- [x] remaining AI_006 worm family definitions and profiles;
- [ ] bats, fish, casters, mimics, critters and event enemy families;
- [ ] spawn pool, biome, time, weather and progression eligibility.

## N3 — Bosses

- [x] import the source-backed Eye of Cthulhu Expert phase-one parameters and synchronized Servant intent cadence;
- [x] import Expert transformation timing and the source-ordered random Servant spawn every 20 ticks, including the tick-100 stage transition spawn;
- [x] import deterministic Expert phase-two hover/dash behavior: long-range `400/600/800` acceleration bands, `1.15/1.30` later-dash speed multipliers, `50/90` slowdown/duration and low-life state `5` movement up to its RNG transition;
- [x] finish Eye of Cthulhu Expert RNG-shaped rapid-dash states `ai[1]=3/4`: live player-velocity prediction, `Next(1,4)`/`Next(-3,1)` state seeding, direction/velocity perturbation, critical-life renormalization and source `20/10 + 13` cadence;
- [ ] finish Eye of Cthulhu Good World reflection/re-entry, damage/defense difficulty projection and remaining irreversible/cosmetic effects;
- [x] finish King Slime `AI_015` difficulty/seed branches and despawn: Good World scale/air-speed behavior plus source-ordered Expert `1/4` Spiked Slime minion selection are authoritative; the pinned method has no separate Master AI branch;
- [x] finish King Slime authoritative death lifecycle and `downedSlimeKing` progression persistence;
- [x] import King Slime normal-mode NPC-specific loot plus Expert/Master source-ordered gameplay rule semantics, packet-28-timed `playerInteraction` accounting, active-recipient filtering, Master relic delivery and per-player Master pet placement;
- [ ] wire the concrete packet-90 instanced Boss Bag encoder/`54000`-tick slot lease and remaining King Slime death-time world effects (Slime Rain termination and first-kill Nerdy Slime unlock/spawn);
- [ ] add remaining pre-Hardmode bosses with complete child/projectile ownership;
- [ ] add Hardmode, event and endgame bosses;
- [ ] boss bars, announcements, progression transitions and multiplayer targeting parity.

Eye of Cthulhu still intentionally reports `FullVanillaAiParity = false`. Expert rapid dashes now consume the source RNG sequence through the injected authoritative NPC random stream and read live target velocity through the player-slot snapshot boundary. Good World reflection/re-entry and combat-stat difficulty projection remain separate open work, so the coverage catalog advertises `BossExpertRapidDashSlice` rather than full parity.

King Slime still intentionally reports `FullVanillaAiParity = false`. Normal-mode loot owns its ordered world-item transaction. Expert/Master gameplay now preserves the pinned raw-RNG order for Boss Bag, Master relic and the per-active-interacting-player pet rule, including inline `Item.NewItem`-equivalent delivery points. Boss Bag transport remains explicitly incomplete until packet 90 and its `54000`-tick server-side slot-reuse lease are represented; Slime Rain termination and the first-kill Nerdy Slime world side effect also remain open.

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
