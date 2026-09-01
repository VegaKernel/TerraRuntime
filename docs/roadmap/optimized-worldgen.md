# Optimized world-generation roadmap

This roadmap owns the delivery of the built-in `terraruntime:optimized` profile.

The profile is intentionally independent from source-exact `terraruntime:vanilla` generation. The release criterion is
not seed-identical output. The release criterion is a deterministic, official-client-compatible, visually coherent and
**playable** Terraria world whose required geography, structures and progression resources are guaranteed to fit.

> Checkbox policy: `[x]` means implementation plus executable evidence exists on `main`. Presence-only placeholders do
> not count as completed gameplay.

## O0 - Spatial planning and generator identity

- [x] register `terraruntime:optimized` as a separate built-in profile without replacing `terraruntime:vanilla`;
- [x] keep built-in implementations separated under `TerraRuntime.World/Generation/{Flat,Optimized,Skyblock,Vanilla}`;
- [x] use a deterministic pass graph with isolated per-pass RNG;
- [x] allocate major structure regions before terrain mutation;
- [x] reject layouts where mandatory reserved structures overlap or escape map bounds;
- [x] keep a protected central spawn envelope;
- [x] split optimized generation into base, playability, landmark and final progression-validation providers instead of growing one monolith;
- [ ] add versioned generator-layout metadata so future algorithm revisions can intentionally reproduce old worlds.

## O1 - Organic base world

- [x] coherent multi-octave terrain heightfield;
- [x] softened/flattened spawn transition without a hard rectangular platform;
- [x] bounded left/right oceans with continuous solid basin floors and beach transitions;
- [x] forest baseline;
- [x] snow biome;
- [x] desert biome;
- [x] jungle biome;
- [x] corruption/crimson biome selected from `WorldGenerationOptions`;
- [x] underground mushroom region;
- [x] underworld band;
- [x] deterministic variable-radius cave walkers protected from mandatory structure reservations;
- [x] large caverns, vertical shafts, underground lakes and a connected cave-room layer;
- [x] domain-warped snow/desert/jungle/world-evil boundary tongues over natural terrain only;
- [ ] add measured terrain-quality fixtures for Small/Medium/Large world silhouettes.

## O2 - Guaranteed structures and map elements

Every mandatory role must have a reserved region or an explicit count/range budget before generation starts.

- [x] dungeon reservation and first traversable shaft/room geometry;
- [x] create an explicit readable surface opening for the optimized dungeon;
- [x] multiple bounded floating islands;
- [x] jungle hive with Honey;
- [x] Jungle Temple shell/interior plus Lihzahrd Altar;
- [x] Aether pocket plus Shimmer;
- [x] world-evil Demon Altar;
- [x] Underworld Hellforge;
- [x] richer dungeon graph with branches, rooms, spikes/traps, locked chests and biome-safe placement;
- [x] Floating Island houses with persistent custom sky caches;
- [x] replace custom sky caches with source-backed vanilla Skyware loot roles;
- [x] Floating Lakes as a distinct island variant;
- [x] solid-mass pyramids with deterministic count budgets, carved surface openings/shafts/chambers and persistent caches;
- [x] Living Wood trees with hollow trunks, roots, underground rooms and persistent caches;
- [x] bounded Underworld houses plus platform-bridge variation;
- [ ] extend Underworld settlements with source-backed furniture/loot/resource families;
- [ ] multiple hives on larger worlds with valid Queen Bee progression space;
- [x] granite, marble and spider/cobweb micro-biomes;
- [ ] add glowing-mushroom and additional representative micro-biomes;
- [x] current optional landmarks have explicit world-size/density budgets rather than unbounded random placement.

## O3 - Progression resources and loot

Presence of terrain alone is not considered playable progression.

- [x] Copper/Iron/Silver/Gold-tier ore placement;
- [x] Hellstone placement;
- [x] Water, Lava, Honey and Shimmer availability;
- [x] starting Guide persistence;
- [x] Life Crystal distribution with a fail-closed minimum count scaled by world area;
- [x] separate surface/underground/cavern persistent chest budgets;
- [x] persistent landmark caches for sky houses, pyramids, Living Trees and Underworld houses;
- [x] biome chest/loot families needed for ordinary pre-hardmode exploration;
- [x] dungeon locked chest/key progression;
- [x] source-backed 2x2 Shadow Orb / Crimson Heart progression anchors with world-size budgets and correct Crimson +36 frame style;
- [x] Hive Larva worldgen anchors plus a persistent jungle progression cache with source-backed Jungle Spores/Stingers/Vines; authoritative Queen Bee activation remains gameplay-owned;
- [x] reachable Hellforge route with an explicit dry Obsidian/exposed-Hellstone resource pocket and final topology targets;
- [ ] hardmode-ready world anchors required by later progression mutation logic.

Optimized exploration loot now uses pinned TerrariaServer `1.4.5.8` primary families for Skyware, ordinary surface and
underground caches plus dedicated Snow/Ice, Jungle, Underground Desert and both-ocean roles. Placement and deterministic
scheduling remain TerraRuntime-owned, so this closes source-backed progression roles without claiming seed-identical
vanilla chest tables. Pyramid, Living Tree and Underworld landmark caches remain separate custom roles.

## O4 - Organic presentation

The optimized profile may use any deterministic mathematics that produces better worlds while preserving bounded work
and validation guarantees.

Candidate techniques include value/simplex-style noise implemented in-repo, fractal octave combinations, domain
warping, signed-distance masks, splines, cellular fields and correlated random walks. No technique is adopted merely
because it is fashionable; output quality and cost must be measured.

- [x] multi-scale value-noise surface;
- [x] correlated variable-radius caves;
- [x] SDF/ellipse-like perturbed floating islands;
- [x] noise-warped large cavern rooms connected to the smaller random-walk cave texture;
- [x] domain-warped biome boundaries over natural material families;
- [x] visibly distinct sky-house/Floating-Lake island roles;
- [x] large surface landmarks (pyramids and Living Trees);
- [x] deep-world landmarks (Underworld settlements and micro-biomes);
- [x] slope-aware one-tile natural surface transitions with persisted top slopes/half-blocks;
- [x] deterministic ordinary forest/jungle/snow trees plus surface undergrowth and sunflower patches, with explicit density budgets and frame-important-object avoidance;
- [x] ordinary optimized trees publish persistent vanilla-format foliage anchors and executable crown-count regressions;
- [x] share bounded surface probing, clearance, solid-fill and ocean-column integrity primitives across optimized generation layers;
- [ ] deterministic screenshot/map fixtures for visual regression review;
- [ ] generation-time and allocation budgets on canonical world sizes.

## O5 - Fail-closed playability validation

A generated candidate is rejected before commit if a mandatory element is absent.

- [x] validate dungeon region and dungeon material;
- [x] validate Jungle Temple;
- [x] validate hive;
- [x] validate Aether/Shimmer;
- [x] validate snow/desert/jungle/world-evil biome material;
- [x] validate both oceans for water coverage plus sampled continuous solid basin floors;
- [x] validate floating-island mass;
- [x] validate Demon Altar, Hellforge and Hellstone;
- [x] validate spawn safety with a bounded dry walkable starter area;
- [x] validate landmark budgets, landmark material minima, persistent landmark caches and the readable dungeon opening;
- [x] validate persistent chest and Life Crystal budgets;
- [x] validate an excavation-aware path/reachability graph from spawn to surface biomes and major structure entrances;
- [x] validate area-scaled minimum Copper/Iron/Silver/Gold/Hellstone quantities instead of presence only;
- [x] validate connected dungeon/temple/hive interior components and explicit Temple/dungeon access openings;
- [x] validate final post-landmark structure footprints, material minima and complete 3x2 progression objects;
- [ ] run generated `.wld` through pinned TerrariaServer `1.4.5.8` acceptance and an official-client join smoke.

## O6 - Production gate

`terraruntime:optimized` becomes the recommended new-world profile only after all of the following are true:

- [ ] a normal character can progress from spawn through pre-hardmode without importing a second world;
- [ ] required biome, dungeon, hive, temple, floating-island, Aether and Underworld roles are guaranteed for every
      supported world size;
- [ ] progression-critical resources and loot have minimum-count gates;
- [ ] generated worlds pass TerraRuntime structural validation and pinned official-server acceptance;
- [x] deterministic replay is covered for fixed seeds, including generated chest side-table content;
- [ ] generation time and peak memory are measured and bounded;
- [ ] visual-regression review shows organic terrain, caves and structure placement rather than obvious rectangular
      generation artifacts.

`terraruntime:vanilla` continues independently toward source/reference parity. Its unfinished byte-identical worldgen
parity does not block the optimized profile, and optimized visual/algorithmic changes do not weaken vanilla evidence.