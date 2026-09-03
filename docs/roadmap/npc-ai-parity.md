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
| Brain of Cthulhu | yes | partial | partial | 20/40 Creeper spawn, invulnerability gate, phase teleports/pursuit, death/progression and difficulty loot | no |
| Brain Creeper | yes | partial | partial | Brain-relative orbit/charge, Expert/Good World pursuit and difficulty material loot | no |
| Skeletron | yes | partial | partial | head/hand ownership, Expert skull homing/lifetime, death/progression and source-ordered loot | no |
| Queen Bee | yes | partial | partial | AI_043 attack cycle, Jungle/surface/Good World enrage, Bee/SmallBee ownership, stinger 719, death/progression and source-ordered loot | no |
| Eater of Worlds | yes | partial | partial | chain/split lifecycle, shared interaction, last-segment loot/progression, Skyblock shadow-orb state, meteor scheduling and missing-health Heart branch | no |
| Deerclops | yes | partial | partial | AI_123 attack/return/teleport states, Expert passive shadow hands, projectile ownership, death/progression and difficulty loot | no |
| Wall of Flesh / Eye / Hungry | yes | partial | partial | linked-child bootstrap, shared root life, eye laser, side spawns, loot/recovery, brick-box mutation and Hardmode transition | no |
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

## Server-authoritative parity scope

This roadmap tracks **dedicated-server gameplay parity**, not literal execution parity with every line present in Terraria's shared client/server source. A behavior belongs in TerraRuntime only when the official dedicated-server path owns, mutates, validates, persists, or replicates authoritative game state.

Do **not** import presentation-only branches merely because they appear in the decompiled server assembly. In particular, the following are outside the NPC/AI completion gates unless independent evidence shows an authoritative network/gameplay side effect:

- `Dust.*` and other particle-only effects;
- direct/local `SoundEngine.PlaySound(...)` calls that become no-ops on `Main.dedServ` and are not paired with an explicit network sound event;
- `Gore.*`, `CombatText.*`, `Lighting.*`, camera/screen-shake, alpha/rotation changes used only for rendering, and other client presentation state;
- cosmetic RNG that exists only to shape those presentation effects.

Conversely, packet emission, NPC/projectile/item spawn, player/NPC damage or buffs that execute on the dedicated server, collision/world mutation, loot, progression, persistence, authoritative RNG ordering, targeting and replicated state **do** count toward parity. When a shared method mixes both categories, reimplement only the server-authoritative branch and document the omitted presentation branch as intentionally out of scope rather than as unfinished gameplay work.

`FullVanillaAiParity` therefore means full parity for the claimed **server-authoritative** profile. It does not require headless emulation of client-only visuals or audio.

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
- [ ] type-specific authoritative attacks, transformations, projectiles and spawn side effects; presentation-only spawn effects are intentionally out of scope;
- [ ] differential scenarios for each admitted AI_003 subtype.

The current door layer is no longer guessing frame geometry. Normal-door mutation reproduces the pinned 1.4.5.8 `OpenDoor` transform, including locked-door rejection and the source clearance set; successful authoritative mutations can be represented exactly as packet 19. The exact AI_003 pressure/reset table is also executable, and the currently admitted restricted Zombie/Skeleton slice now receives the persisted `GetGoodWorld` suppression and live `insideUnbreakableWalls` projection via `VanillaWorldUnbreakableWallScan` (8×250 ray scan for wall 350, color ≥16). Tall-gate type shifting is now fully wired through the production `RuntimeTallGateOccupancyProbe` (live `Collision.EmptyTile(ignoreTiles:true)` actor-rectangle checks) and the authoritative `RuntimeGroundFighterDoorOpeningSink` in default `ServerRuntimeState` composition. Special future fighter types remain fail-closed at production composition until concrete type routing and the type-26 destroy side effect are authoritative.

## N2 — Common ordinary families

- [x] typed definitions and movement profiles for the hostile AI_001 catalog;
- [x] source-backed slime net variants `-1..-10` with effective spawn/packet defaults;
- [ ] remaining AI_001 authoritative projectile, item-containment, split, transform and seed branches; presentation-only visual branches are intentionally out of scope;
- [x] typed definitions, steering profiles and wet behavior for all hostile AI_002 identities;
- [x] source-backed AI_002 net variants `-38..-43` with effective spawn/packet defaults;
- [x] AI_002 source-backed daylight discouragement/despawn and Pigron 300-tick line-of-sight phasing/re-entry state with live world collision/Graveyard queries;
- [ ] remaining AI_002 authoritative type-specific transforms/effects; presentation-only alpha/rotation/dust/sound branches are intentionally out of scope;
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
- [x] Eater of Worlds packet-28 shared playerInteraction, per-segment material loot, last-segment boss promotion, Expert/Master/normal boss loot and persistent `downedBoss2` progression;
- [x] finish Eater of Worlds authoritative death-event parity for the admitted server slice: source-order last-segment boss promotion, `realLife = -1` semantics represented by per-segment ownership plus shared interaction credit, Skyblock low-tile `shadowOrbSmashed`, first/1-in-2 repeat meteor scheduling and the Eater-specific missing-health `1/4` Heart branch; presentation-only effects remain out of scope;
- [x] remaining AI_006 worm family definitions and profiles;
- [x] source-backed AI_017 Vulture/Raven activation, collision rebound, steering and wet escape;
- [x] source-backed AI_020 Spike Ball and AI_021 Blazing Wheel authoritative motion state machines;
- [ ] bats, fish, casters, mimics, critters and event enemy families;
- [ ] spawn pool, biome, time, weather and progression eligibility.

## N3 — Bosses

- [x] import the source-backed Eye of Cthulhu Expert phase-one parameters and synchronized Servant intent cadence;
- [x] import Expert transformation timing and the source-ordered random Servant spawn every 20 ticks, including the tick-100 stage transition spawn;
- [x] import deterministic Expert phase-two hover/dash behavior: long-range `400/600/800` acceleration bands, `1.15/1.30` later-dash speed multipliers, `50/90` slowdown/duration and low-life state `5` movement up to its RNG transition;
- [x] finish Eye of Cthulhu Expert RNG-shaped rapid-dash states `ai[1]=3/4`: live player-velocity prediction, `Next(1,4)`/`Next(-3,1)` state seeding, direction/velocity perturbation, critical-life renormalization and source `20/10 + 13` cadence;
- [x] finish Eye of Cthulhu Good World gameplay reflection/re-entry and Classic/Expert/Master phase-two damage/defense projection;
- [x] finish Eye of Cthulhu remaining authoritative Good World star-shot reflection identities: source-special projectiles `728` and `955` now use their pinned defaults and the exact `NPCID.Sets.ReflectStarShotsInForTheWorthy` NPC set; presentation-only sound/dust/gore effects remain out of scope;
- [x] finish King Slime `AI_015` difficulty/seed branches and despawn: Good World scale/air-speed behavior plus source-ordered Expert `1/4` Spiked Slime minion selection are authoritative; the pinned method has no separate Master AI branch;
- [x] finish King Slime authoritative death lifecycle and `downedSlimeKing` progression persistence;
- [x] import King Slime normal-mode NPC-specific loot plus Expert/Master source-ordered gameplay rule semantics, packet-28-timed `playerInteraction` accounting, active-recipient filtering, Master relic delivery and per-player Master pet placement;
- [x] implement the concrete packet-90 instanced Boss Bag frame, packet-151 slot-release frame and `54000`-tick unpublished slot lease, plus source-ordered Slime Rain termination and first-kill Nerdy Slime unlock/spawn with `.wld` persistence;
- [x] connect the Expert/Master difficulty-loot path to live packet-28/playerInteraction combat ingress and advance leased slots from the authoritative item-update phase;
- [x] add Brain of Cthulhu/Creeper gameplay vertical: source defaults, 20/40 child spawn, invulnerability gate, both teleport/pursuit phases, Creeper charge/pursuit, packet-28 loot and `downedBoss2`;
- [x] finish Brain of Cthulhu authoritative player `ZoneCrimson` escape gate from live SceneMetrics-projected biome facts; presentation-only sound/dust/gore and client alpha rendering remain intentionally out of scope;
- [x] add Skeletron gameplay vertical: source-backed head/hand ownership, Expert skull cadence/homing/lifetime, shared head/hand interaction credit, Classic/Expert/Master loot, isolated RedHat-condition evaluator coverage and persisted `downedBoss3` progression;
- [x] add Queen Bee gameplay vertical: AI_043 attack cycle, source-shaped Jungle/surface/Good World enrage, Bee/SmallBee spawn ownership with localAI seed, stinger 719 lifetime, Classic/Expert/Master loot and persisted `downedQueenBee`;
- [x] add Deerclops gameplay vertical: AI_123 chase/attack/return/teleport/despawn states, distance shield, source-backed 961/962/965 projectile ownership, Classic/Expert/Master loot and persisted `downedDeerclops`;
- [x] finish Deerclops remaining dedicated-server gameplay parity: Expert passive shadow hands use source `localAI[2]` cadence, three-way player-slot rotation, 1200-pixel range and generation-safe per-NPC interaction credit; scream `Slow` remains deliberately excluded because the pinned `Main.netMode == 2` branch does not apply it on the dedicated server;
- [x] add the remaining pre-Hardmode boss vertical, Wall of Flesh: root/eye/Hungry ownership, initial 13-child bootstrap, leech/Fire Imp/Expert Hungry side effects, eye laser `83`, shared root life, difficulty loot, recovery drops, brick-box death mutation and persisted Hardmode transition;
- [ ] add Hardmode, event and endgame bosses;
  - Hardmode/endgame boss bring-up now has explicit authoritative state families for Queen Slime, the mechanical bosses, Plantera, Golem, Duke Fishron, Lunatic Cultist, Empress of Light and Moon Lord. Linked Plantera/Golem/Moon Lord parts, Duke Detonating Bubble plus AI 71 Sharkron/Sharkron2, Cultist clones/Ancient Vision/Ancient Light/Ancient Doom, ritual Dragon/Ancient Vision spawning and the implemented source-owned boss projectile intents are dispatched through the runtime rather than metadata-only fallback.
  - The late-boss projectile slice now includes Duke Sharknado `385`; Cultist ice/lightning/fire/ritual `464/465/467/468/490` plus Ancient Doom `593`; Empress lasting-rainbow/rainbow-streak/lance/sun-dance families `872/873/919/923`; and Moon Lord Phantasmal eye/sphere/deathray/leech/bolt `452/454/455/456/462`. Moon Lord hand/head/True Eye attack clocks follow the pinned source sequences, Cultist ritual hits transition through the authoritative punishment/abort states, and daytime Empress rage drives the source `9999` projectile damage and Expert-like cadence.
  - Moon Lord part-death is now server-owned at the shared damage boundary: the first lethal hand/head hit executes the pinned `checkDead` survival transition, restores life, enters invulnerable `ai[0] = -2`, and creates exactly one True Eye using the source 1200-tick loop with offset `588 + 400 * activeTrueEyes`. The core vulnerability gate requires both owned hands plus the owned head to remain present in retired `-2` state, and the retired head enters `-3` when core `ai[0] = 2` death drama begins. The old synthetic three-eye spawn on core opening is removed.
  - This combined checkbox remains open for event-boss coverage and the final parity gates: exact projectile-style internals that live outside NPC authority, source-order differential fixtures, remaining seed-specific Empress boundaries, the Moon Lord core's complete 600-tick terminal death sequence, and exact child-slot-loss self-termination still need dedicated verification before `FullVanillaAiParity` can be claimed.
- [ ] boss bars, announcements, progression transitions and multiplayer targeting parity.

Eye of Cthulhu still intentionally reports `FullVanillaAiParity = false`. Expert rapid dashes consume the source RNG sequence through the injected authoritative NPC random stream and read live target velocity through the player-slot snapshot boundary. Good World re-entry, transformation projectile reflection and phase-two Classic/Expert/Master damage/defense projection are now authoritative gameplay state. Reflection covers the admitted aiStyle 1/2 player projectile identities plus the source-special Good World star shots `728` and `955`, using the pinned `ReflectStarShotsInForTheWorthy` NPC identity set. Presentation-only sound/dust/gore are intentionally outside the server-authoritative parity claim and must not keep the slice open by themselves, so the coverage catalog advertises bounded boss slices only for real authoritative gaps.

The pre-Hardmode boss roster is now closed for the server-authoritative gameplay scope tracked above: King Slime, Eye of Cthulhu, Eater of Worlds, Brain of Cthulhu, Queen Bee, Skeletron, Deerclops and Wall of Flesh all have explicit authoritative behavior/death ownership rather than generic fallback. This does not set `FullVanillaAiParity = true`: shared/global work such as boss bars, announcements, broad multiplayer-targeting parity and presentation-only branches remains tracked separately.

King Slime still intentionally reports `FullVanillaAiParity = false`. Normal-mode loot owns its ordered world-item transaction. Expert/Master rule evaluation preserves the pinned raw-RNG order for Boss Bag, Master relic and per-active-interacting-player pet drops. Packet 90 now reuses the exact packet-21 payload, packet 151 releases an expired instanced slot, and the server-side lease store keeps that unpublished slot unavailable for `54000` ticks. The committed death slice also follows source order for `StopSlimeRain`, the first-kill blue town-slime unlock/Nerdy spawn and `downedSlimeKing`, with both persistent flags patched back into the canonical `.wld`. Live packet-28 combat/death ingress now records source-ordered player interaction, executes the implemented King Slime difficulty loot before death effects, and advances instanced-item leases from the authoritative item phase; packet 151 is emitted when an exact lease expires.

## N4 — Town, friendly and special NPCs

- [ ] town AI, housing and schedules;
  - source-backed AI_007 shelter/home/chair scheduling, shimmer state 25, projectile combat for Merchant/Nurse/Arms Dealer/Guide, and melee state 15 for Dye Trader/Tax Collector/Stylist are authoritative; social/emote and remaining special town branches remain open;
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
