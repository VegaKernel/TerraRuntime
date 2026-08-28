# Gameplay decomposition and typed vanilla catalogs roadmap

This document defines the mandatory decomposition of TerraRuntime gameplay code as vanilla parity grows. The goal is not merely to replace raw literals with prettier constant names. The goal is to stop protocol numbers, Terraria content IDs, gameplay rules, mutable runtime state and subsystem orchestration from collapsing into the same code paths.

TShock/Terraria server ecosystems are useful as a naming and historical reference because mature code commonly uses semantic identifiers such as `ItemID`, `ProjectileID`, `TileID`, `NPCID`, `BuffID` and related catalogs instead of scattering raw content IDs. TerraRuntime must take that lesson without inheriting TShock's hook/global-state architecture or depending on Terraria assemblies.

The source-of-truth hierarchy remains unchanged: TerrariaServer 1.4.5.8 decompile first, Multiplicity for protocol 326 wire models, terrustia as an independent implementation cross-check, and TShock/OTAPI as behavioral/history references only.

The governing rule is:

> **A numeric representation may cross a protocol/file boundary, but gameplay code should operate on named, validated domain concepts wherever the number has stable semantic meaning.**

---

## 1. What counts as a magic number

Not every numeric literal is a problem.

A number is a magic number when its correctness depends on Terraria/version/domain knowledge that is not apparent from the local operation.

Examples that must become named/versioned concepts:

- NPC, projectile, item, tile, wall, buff and prefix type IDs;
- maximum vanilla content counts/ranges;
- inventory slot ranges and special slot groups;
- protocol-visible enum values and flag bits;
- AI style IDs when they are used as semantic categories;
- tile frame/layout dimensions derived from a known vanilla object rule;
- world format gates/version constants;
- fixed gameplay timers/radii/speeds/damage multipliers verified from vanilla behavior;
- special-case IDs such as "if projectile type == 12" where `12` identifies a named vanilla entity;
- special negative/legacy IDs and canonicalization tables;
- sentinel values whose meaning is domain-specific.

Numbers that may remain local when clear:

- loop increments;
- array index arithmetic with obvious local meaning;
- mathematical constants and simple factors whose meaning is evident;
- `0`/`1` boolean/counter comparisons where introducing a named constant reduces clarity;
- short-lived benchmark/test values that are not pretending to encode vanilla truth.

Do not create a giant `Constants.cs` graveyard. Every named constant/catalog belongs to the subsystem that owns the invariant.

---

## 2. Separate raw representation from domain identity

TerraRuntime should distinguish at least three layers:

```text
wire/file primitive
    int / short / byte
        |
        v
validated vanilla domain ID
    ItemTypeId / NpcTypeId / ProjectileTypeId / ...
        |
        v
runtime entity identity
    ItemHandle / NpcHandle / ProjectileHandle / generation/revision
```

These are different concepts.

Examples:

- `NpcTypeId(1)` means the vanilla NPC content type, not NPC slot 1.
- `NpcHandle(slot=1, generation=7)` identifies one live NPC instance, not NPC type 1.
- `ProjectileTypeId` is not the same thing as projectile identity/UUID/slot.
- `ItemTypeId` is not an inventory slot.
- `TileTypeId` is not a tile coordinate.

Code review and type signatures should make those confusions difficult.

---

## 3. Version-pinned ID value types

Introduce small allocation-free value types where they materially prevent category mistakes.

Candidate types:

```text
ItemTypeId
NpcTypeId
ProjectileTypeId
TileTypeId
WallTypeId
BuffTypeId
PrefixId
TileEntityTypeId
LiquidKind
NpcAiStyleId
ProjectileAiStyleId
```

Use `readonly record struct`/equivalent compact value types where appropriate.

Requirements:

- explicit construction/validation at trust boundaries;
- cheap equality/hash/formatting;
- no reflection;
- no allocation on common gameplay paths;
- underlying primitive remains accessible to codecs/persistence through an explicit conversion;
- invalid/unrepresentable IDs cannot silently enter authoritative state;
- version-specific valid ranges are centralized rather than copied into packet handlers and gameplay systems.

Do not wrap every local integer in a new type. Add value types where domain categories are commonly confused or where validation/versioning matters.

---

## 4. Vanilla content catalogs

TerraRuntime needs its own version-pinned catalogs and must not reference `Terraria.ID.*` assemblies at runtime.

Conceptual catalogs:

```text
VanillaItemIds
VanillaNpcIds
VanillaProjectileIds
VanillaTileIds
VanillaWallIds
VanillaBuffIds
VanillaPrefixIds
VanillaTileEntityIds
VanillaLiquidKinds
```

Naming can change, but behavior should not depend on unexplained numeric literals spread through gameplay code.

Examples:

```text
VanillaNpcIds.BlueSlime
VanillaProjectileIds.WoodenArrowFriendly
VanillaTileIds.Stone
VanillaItemIds.CopperShortsword
VanillaBuffIds.Poisoned
```

Requirements:

- catalogs are pinned to Terraria 1.4.5.8/protocol 326 where applicable;
- names are stable inside TerraRuntime even if the underlying raw value changes in a future supported Terraria version;
- raw values are verified against the official reference hierarchy;
- TShock/Terraria `*ID` naming may be used to cross-check human-readable names, not as runtime dependency or final source of truth;
- no reflection-based lookup tables;
- avoid shipping copyrighted game text/descriptions/assets merely to make a catalog convenient.

---

## 5. Metadata tables, not giant switch forests

An ID catalog answers "what is this type?". Gameplay also needs verified metadata answering "what properties does this type have?".

Candidate immutable definitions:

```text
ItemDefinition
NpcDefinition
ProjectileDefinition
TileDefinition
WallDefinition
BuffDefinition
PrefixDefinition
TileObjectDefinition
```

Metadata should include only facts that are useful to runtime behavior and independently verified.

Examples:

### Item metadata

- max stack;
- width/height where gameplay relevant;
- damage/knockback/use timing defaults;
- use style;
- ammo/use-ammo relation;
- consumable/tool/weapon/placeable flags;
- tile/wall placement type;
- accessory/equipment categories;
- rarity/value only where server behavior needs them.

### NPC metadata

- width/height;
- life/damage/defense defaults;
- gravity/collision flags;
- AI style/family metadata;
- boss/town/friendly flags;
- knockback resistance;
- networking/runtime flags required for correct lifecycle.

### Projectile metadata

- width/height;
- friendly/hostile defaults;
- penetration;
- time-left/lifetime defaults;
- tile collision;
- owner semantics;
- AI style/family;
- damage class/behavior metadata where the server needs it.

### Tile/wall metadata

- solid/platform/solid-top;
- frame-important/multi-tile status;
- light/block-light properties where gameplay needs them;
- actuated/wiring interactions;
- break/place constraints;
- object dimensions/origin/anchor rules;
- liquid interaction traits;
- growth/spread/framing families.

Metadata tables should prefer immutable arrays/indexed tables for dense vanilla ID spaces and measured lookup paths over dictionaries on hot paths.

---

## 6. Data generation and maintenance

Hand-writing thousands of definitions is error-prone, but runtime reflection/extraction is forbidden.

Preferred approach:

```text
local verified reference inputs/tools
        |
        v
curated/versioned machine-readable manifest
        |
        v
build/source generator
        |
        v
static AOT-safe C# tables
```

Rules:

- decompiled official source remains local and is never committed;
- do not copy decompiled method bodies or copyrighted game text/assets into generated output;
- generated data contains only reviewed facts/constants/flags required to reproduce behavior;
- generated output is deterministic;
- source generator/build tooling is version-pinned;
- generated catalog count/range checks are tested;
- selected values are independently cross-checked against real protocol/world/client behavior where relevant;
- a generated table is not trusted merely because extraction succeeded.

If a generated manifest becomes large, keep a documented provenance/version header and a reviewable diff format.

---

## 7. Gameplay package decomposition

The desired direction is subsystem ownership rather than a monolithic `Gameplay` namespace full of helpers.

Conceptual target:

```text
Core/Gameplay
    Combat/
    Buffs/
    Spawning/
    Loot/
    Progression/
    Events/

Core/Items
    Definitions/
    Inventory/
    Use/
    Equipment/
    Placement/

Core/Npcs
    Definitions/
    Lifecycle/
    Spawning/
    Behavior/
    Physics/
    Combat/
    Town/
    Bosses/

Core/Projectiles
    Definitions/
    Lifecycle/
    Behavior/
    Physics/
    Collision/
    Combat/

Core/Worlds
    Tiles/
    Walls/
    Objects/
    Chests/
    Signs/
    TileEntities/
    Wiring/
    Liquids/
    Growth/
    Biomes/
    Generation/
```

Exact folders/classes can remain smaller until justified. The point is ownership and dependency direction, not bureaucracy made of directories.

Do not split a 30-line cohesive implementation into eight interfaces just to satisfy a diagram. Decompose when behavior/data/lifecycle have genuinely different ownership or testing needs.

---

## 8. Item subsystem decomposition

Items should not be represented only as packet fields or a loose tuple of `netId/stack/prefix`.

Separate concepts:

### Definition/defaults

- `ItemTypeId` and immutable `ItemDefinition`;
- canonical air/empty item state;
- verified legacy negative ID normalization;
- default stats and legal prefix/stack ranges.

### Runtime item stack

- type;
- stack;
- prefix;
- relevant flags/state;
- normalization invariants.

### Inventory layout

Replace raw slot ranges such as `0..98` and `700..989` with named layout semantics already verified from vanilla packet behavior.

Conceptual categories:

```text
MainInventory
Armor
Dyes
MiscEquipment
MiscDyes
Banks
Trash
Loadouts
ProtocolRelaySlots
PrivateSlots
```

The numeric layout stays centralized and version-pinned.

### Item use

Decompose:

- use timing/animation;
- weapon/tool semantics;
- ammo consumption;
- consumables;
- placement;
- healing/mana;
- equipment/accessory application;
- use-triggered projectile/NPC/world requests.

Networking converts client requests into semantic item-use commands. It must not own gameplay implementation.

---

## 9. Projectile subsystem decomposition

Projectile support should be divided into:

- type definitions/defaults;
- lifecycle/slot/generation;
- spawn provenance/ownership;
- behavior/AI dispatch;
- movement/physics;
- tile collision;
- NPC/player collision;
- combat/damage/knockback;
- penetration/local/static immunity where vanilla requires it;
- child projectile spawning;
- kill/despawn effects;
- dirty state and replication.

Replace conditions such as:

```text
if (type == 14 || type == 20 || type == 36)
```

with one of:

- named ID checks when the set is genuinely tiny and behavior-specific;
- a verified metadata trait/family;
- a behavior strategy registered for a projectile family.

Do not create vague tags merely to hide numbers. A trait must correspond to a real gameplay rule.

This decomposition is also the prerequisite for the custom projectile behavior contracts in `gameplay-worldgen-extensibility.md`.

---

## 10. NPC subsystem decomposition

NPC support should be divided into:

- definitions/defaults;
- lifecycle and generation/revision;
- spawn eligibility/rates/pools;
- targeting;
- behavior/AI;
- movement physics;
- collision and world queries;
- combat/hit/death;
- buffs/debuffs;
- loot;
- town NPC/housing behavior;
- boss-specific orchestration;
- dirty tracking/replication.

NPC AI code should not contain unrelated packet encoding, global player-array scans, persistence writes or plugin dispatch internals.

Shared behavior families are encouraged when verified. For example, slime-like movement can have reusable movement/state primitives while each NPC definition selects parameters or specialized behavior. Do not force unlike NPCs into one giant `switch (type)` merely because vanilla historically did so.

This decomposition is the prerequisite for the NPC decorator/replacement extension pipeline in `gameplay-worldgen-extensibility.md`.

---

## 11. Tile, wall and world-object decomposition

Tiles are especially dangerous because raw type/frame/coordinate values easily become one giant mutation function.

Separate at least:

### Tile identity and state

- `TileTypeId`;
- wall type;
- frame X/Y;
- slope/half-block;
- wires/actuator;
- liquid amount/kind;
- paint/coating and other persisted flags where supported.

### Static tile metadata

- solidity/platform behavior;
- frame-important status;
- object family/dimensions;
- break/place rules;
- liquid/growth/wiring traits.

### Mutation services

Semantic operations should exist for concepts such as:

```text
PlaceTile
KillTile
PlaceWall
KillWall
SlopeTile
PlaceObject
Actuate
WirePulse
SetLiquid
Grow/Spread
```

The authoritative world system validates and commits these operations, updates revisions/dirty sections and schedules replication.

### Multi-tile objects

Introduce an explicit `TileObjectDefinition`/equivalent for:

- dimensions;
- origin;
- anchors/support rules;
- style/frame mapping;
- associated tile entity where relevant.

Do not scatter frame-width arithmetic and magic frame offsets across chest/sign/furniture handlers.

TShock/OTAPI may help identify historical edge cases, but exact 1.4.5.8 framing/placement rules come from the official reference.

---

## 12. Buffs, prefixes and combat semantics

### Buffs/debuffs

Use `BuffTypeId` plus verified metadata for:

- debuff/persistent flags;
- PvP/NPC applicability;
- stacking/replacement/timer rules;
- server-authoritative effects where required.

Do not store behavior as one enormous `if (buffId == ...)` chain if effects naturally belong to separate systems.

### Prefixes

Use `PrefixId` and central validation/default rules rather than accepting arbitrary byte values throughout item code.

### Combat

Create explicit semantic structures for:

- damage source/provenance;
- target;
- base/final damage;
- defense/armor interaction;
- knockback;
- crit;
- immunity/cooldown state;
- death reason/result;
- PvP/environment/NPC/projectile source categories.

Avoid APIs where a caller passes six unnamed integers/booleans whose meaning is reconstructed from call order.

---

## 13. Loot, recipes and spawn rules

Large rule tables should be represented as data/rules rather than repeated hardcoded branches where practical.

### Loot

Target concepts:

```text
LootTable
LootRule
Condition
Chance
StackRange
ItemTypeId
```

Preserve vanilla RNG ordering and conditional semantics. A declarative rule representation is useful only if it can express verified behavior without silently changing RNG order.

### Recipes

If/when server-side recipe validation becomes authoritative, separate recipe definitions, ingredients, crafting-station requirements and progression conditions from inventory packet handling.

### Spawning

NPC spawn logic should separate:

- environmental query;
- eligible spawn pool;
- weights/chances;
- population caps;
- progression/event modifiers;
- selected NPC definition;
- final spawn request.

Do not let every NPC AI class reinvent spawn eligibility.

---

## 14. World progression, events and biome rules

Introduce named state rather than anonymous world flags and numeric event codes.

Subsystems should own:

- boss/progression milestones;
- hardmode/world transformations;
- invasions/events;
- moon/weather/time-driven state;
- biome/zone classification;
- town/housing progression interactions.

Use typed enums/bitsets/value objects where they clarify stable semantics.

Do not create a single universal `WorldFlags` enum containing unrelated persistence, progression and networking bits. Keep persistence representation separate from gameplay state.

---

## 15. Flags and bitfields

Raw protocol/file bit masks belong at codecs/boundaries.

Gameplay should normally see named semantics.

Examples:

```text
PlayerControlFlags
TileWireFlags
NpcStateFlags
ProjectileStateFlags
ItemFlags
```

Rules:

- use `[Flags]` enums or compact value types only when bit composition is genuinely part of the domain model;
- codecs convert wire bits into named fields/flags;
- unknown/reserved bits are handled explicitly according to protocol/file compatibility requirements;
- gameplay does not repeatedly write expressions such as `(flags & 0x20) != 0` when `0x20` has a stable documented meaning.

Multiplicity remains the wire packet model authority. Do not duplicate packet bit layout catalogs in gameplay merely to rename them.

---

## 16. Timers, distances, speeds and tuning constants

Vanilla AI/gameplay contains many numbers that are not content IDs but still encode observable behavior.

Group them with the behavior that owns them:

```text
BlueSlimeBehaviorParameters
NpcPhysicsConstants
ProjectileCollisionConstants
PlayerRespawnRules
SpawnRateRules
```

Requirements:

- comments/tests identify the vanilla rule/source when non-obvious;
- unit names are explicit where confusion is possible (`Ticks`, `Pixels`, `Tiles`, `Seconds`);
- prefer named small parameter records for families of AI behavior;
- no global grab-bag `GameplayConstants` file;
- do not make every vanilla constant configurable merely because it is named.

A named constant is not automatically a public configuration option. Vanilla parity defaults stay fixed unless an extension/policy surface deliberately permits modification.

---

## 17. Units and coordinate types

A major source of subtle magic-number bugs is mixing pixels, tiles, sections and ticks.

Where it improves safety without bloating hot paths, use named helper/value types or explicit API names for:

- tile coordinates;
- world pixel positions;
- section coordinates;
- tile rectangles versus pixel rectangles;
- tick durations versus wall-clock durations.

At minimum, method/parameter names must state units when the primitive type cannot.

Do not silently multiply/divide by `16`, section width/height or tick rate throughout gameplay. Centralize conversions in world/protocol math helpers.

---

## 18. Protocol and persistence boundaries

Numeric values are unavoidable on the wire and in `.wld`, but they should be localized.

Target boundary:

```text
Multiplicity packet / .wld primitive
        |
        v
validate raw ranges + normalize legacy/sentinel representation
        |
        v
typed domain command/state
        |
        v
authoritative gameplay
```

On output:

```text
typed authoritative state
        |
        v
protocol/persistence mapper
        |
        v
validated raw representation
```

Never let a packet DTO become the authoritative gameplay entity merely because its fields are convenient.

---

## 19. Custom content compatibility

The typed vanilla catalogs must coexist cleanly with the custom archetype system.

Keep these identities separate:

```text
NpcTypeId                         = vanilla client-visible NPC type
CustomNpcArchetypeId              = namespaced server-defined archetype
ProjectileTypeId                  = vanilla client-visible projectile type
CustomProjectileArchetypeId       = namespaced server-defined archetype
```

A custom archetype may select a vanilla presentation type, but its namespaced identity must never be smuggled into a vanilla protocol type field.

The same rule applies if custom items/tiles are added later: server-defined behavior/policy identity is not automatically a new official-client content ID.

---

## 20. TShock as reference, not dependency

TShock is useful for:

- established human-readable `ItemID`/`ProjectileID`/`TileID`/`NPCID` naming;
- discovering categories of validation that mature servers had to care about;
- finding historic exploit/edge-case classes;
- cross-checking which raw numbers deserve semantic names.

TShock is not authoritative for:

- Terraria 1.4.5.8 exact behavior when it differs from current vanilla;
- TerraRuntime subsystem ownership;
- global-state architecture;
- hook/event architecture;
- packet codec ownership;
- threading model.

Never add a TShock dependency to obtain its ID constants. Recreate verified version-pinned facts inside TerraRuntime's own catalogs.

---

## 21. Preventing magic numbers from returning

Documentation alone will lose this fight eventually because humans are extremely capable of reintroducing `if (type == 636)` at 02:00.

Introduce enforceable rules incrementally.

### Architecture/code-review rule

Outside catalog/codec/persistence/test-fixture code:

- no new raw vanilla content type IDs;
- no new raw protocol message IDs;
- no duplicated content-count/range constants;
- no raw inventory slot-range arithmetic;
- no undocumented protocol/file flag masks.

### Automated audit

Start simple:

- repository script/test scans known gameplay directories for suspicious raw ID comparisons and documented forbidden patterns;
- maintain a very small allowlist for legitimate literals;
- fail CI on newly introduced prohibited patterns where signal quality is high.

If false positives become painful, replace the textual audit with a small Roslyn analyzer that understands contexts/types.

Do not begin with a heroic custom analyzer before the typed APIs exist. First make the correct code easy to write, then enforce it.

---

## 22. Testing strategy

### Catalog tests

- known selected IDs match independently verified 1.4.5.8 values;
- declared count/range boundaries match reference values;
- every dense metadata table has expected length and valid index behavior;
- no duplicate names/IDs where uniqueness is required;
- source generation is deterministic.

### Boundary tests

- invalid raw IDs are rejected/normalized exactly at ingress;
- domain IDs serialize back to correct wire/file representation;
- legacy/sentinel values have explicit regressions;
- unsupported future IDs fail safely rather than indexing tables out of range.

### Gameplay tests

For each subsystem refactor, prove behavior before and after remains identical through focused differential/golden tests.

Refactoring a magic number is not done merely because the renamed constant has the same numeric value. Tests should cover the semantic branch it controls.

### Architecture tests

- gameplay projects do not reference Terraria assemblies;
- domain code does not reference concrete Multiplicity packet models except through the protocol boundary where deliberately allowed;
- core gameplay does not reference Vega/TShock;
- catalog/generated-data layer remains AOT-safe.

---

## 23. Migration order

### D0 - Numeric/domain audit

Produce an inventory of:

- raw content IDs;
- hardcoded counts/ranges;
- raw bit masks;
- special slot ranges;
- hardcoded timers/distances/speeds;
- giant type/AI switches;
- duplicate constants across projects.

Classify each as:

```text
wire/file representation
content identity
runtime identity
behavior tuning
local arithmetic
```

Only then decide the right replacement.

### D1 - ID types and catalog foundation

- add typed vanilla IDs;
- add version-pinned catalogs;
- central range validation;
- deterministic generated-data path if needed;
- selected independent verification tests.

### D2 - Items and inventory

- item definitions/defaults;
- typed inventory layout;
- prefix/stack normalization;
- item-use semantic boundary;
- remove raw item/slot IDs from gameplay paths.

### D3 - Projectiles

- projectile definitions;
- typed lifecycle/provenance;
- behavior/physics/collision/combat decomposition;
- remove raw projectile IDs and AI-style numbers from gameplay paths;
- align with custom projectile extension pipeline.

### D4 - NPCs

- NPC definitions;
- AI family/behavior decomposition;
- spawn/physics/combat/loot separation;
- boss/town behavior boundaries;
- remove raw NPC IDs/AI-style numbers;
- align with custom NPC extension pipeline.

### D5 - Tiles, walls and objects

- tile/wall definitions;
- named tile state flags;
- multi-tile object definitions;
- placement/break/framing operations;
- wiring/liquids/growth decomposition;
- remove raw tile/wall IDs and frame constants from unrelated handlers.

### D6 - Buffs, combat, loot and progression

- buff/prefix catalogs;
- damage-source model;
- loot rules;
- event/progression IDs/state;
- biome/zone semantics;
- remove remaining cross-subsystem magic values.

### D7 - Enforcement

- CI audit for prohibited raw IDs/masks;
- architecture tests;
- optional Roslyn analyzer if textual enforcement is insufficient;
- document any intentional remaining raw values and their ownership.

---

## 24. Definition of done

This roadmap slice is complete when:

- gameplay code no longer relies on unexplained raw vanilla content IDs;
- items, NPCs, projectiles, tiles, walls, buffs and prefixes have explicit version-pinned identity/catalog boundaries where required;
- raw protocol/file primitives are converted at boundaries rather than propagated as authoritative domain state;
- inventory slot families and other special ranges are named and centralized;
- entity type identity is impossible to casually confuse with entity slot/generation identity;
- projectile/NPC behavior is decomposed enough to support the extension contracts without packet hacks;
- tile/world mutation rules are decomposed from packet handling and replication;
- behavior constants live with the behavior that owns them rather than a global constants dump;
- TShock/Terraria assemblies are not runtime dependencies;
- new raw IDs/masks are prevented by CI/code-review rules;
- focused differential tests demonstrate that decomposition preserves verified vanilla behavior;
- all generated/catalog code remains clean under Linux and Windows NativeAOT publication.
