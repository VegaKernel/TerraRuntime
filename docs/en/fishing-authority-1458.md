# Fishing authority, Terraria 1.4.5.8

Fishing joins the existing player item phase, inventory funding, projectile store, world tiles, NPC spawning and reward publication. A packet `27` supplies a bounded cast direction and wire identity. It cannot supply a catch, pay bait, move a trusted bobber or grant inventory. The local source clocks must first reach their actual Shoot boundary; packet `41` cannot initialize those clocks. Each accepted bobber retains its exact projectile generation and connection occupation.

The current admission covers the eleven base poles with a neutral prefix and an ordinary upright, unmounted player. Casting without bait is allowed, but cannot produce a normal bite. Source bait selection searches slots `54..57`, then `0..49`. A bite retains the bait type; pulling searches those slots again for that type. Only real funded stock can pay the action, including a conserved-bait result. The existing inventory journal adopts a spent unit and its ceiling before publication, including canonical empty state after the last unit. Transfer carries those same quantities and ceilings; a later client report cannot recreate them.

```mermaid
sequenceDiagram
    participant Input as Controls and cast intent
    participant Player as Existing item phase
    participant Bobber as Existing projectile owner
    participant World as World and NPC owners
    participant Stock as Existing inventory and world items
    Input->>Player: Request use
    Player->>Bobber: Adopt qualified source birth
    Bobber->>World: Query liquid, pond, scene, time and weather
    Bobber->>Bobber: Advance wait and bite RNG
    Input->>Player: Request pull
    Player->>Stock: Adopt source bait decision and payment
    Player->>Bobber: Adopt one returning catch
    Bobber->>Stock: Give catch at source Kill boundary
    Stock-->>Input: Publish accepted inventory or overflow
```

AI `61` motion uses the existing terrain/liquid collision owner. Pond width and vertical columns read actual tiles, excluding source solid half bricks and slopes. The vertical census refuses after `262144` cell reads, in addition to the world-width-bounded horizontal scan; this is a runtime work bound, not a vanilla pond constant. Larger pathological ponds require a separately budgeted query before admission can expand. Fishing conditions use the existing scene scanner, loaded surface/rock layer and seed flags, live world clock/weather and loaded or newly completed Hardmode/Skeletron progression. The rules include water, honey, lava, pond penalties, depth, time, moon phase and weather. Gummy Worm's existing permanent booster contributes through the player phase.

The three fishing potions use the existing paid selected-consumable path and indexed local buff clock: Fishing `2354/121`, Sonar `2355/122`, Crate `2356/123`. Their source durations and last-positive effects are retained, including the update where a timer changes from one to zero. A raw buff snapshot cannot grant these effects or refresh a paid duration. Consumed ladybug bait uses the existing player luck state and transient world weather owner; the source penalty becomes effective after the already completed UpdateLuck stage. Its owned timer survives transfer and cannot be erased by a client luck report.

Current source `FishingCheck` uses `FishDropsDB`; the old `FishingCheck_RollItemDropOld` is not the active selector. The clean implementation preserves the ordered factual rules, probability-before-rarity draw, display-preview draws and separate shared drop-context RNG. Ordinary AI/bite draws use the existing world Main stream; pull/payment draws use the existing UpdatePlayers stream. Neither stream is replaced by client random values. Special NPC results enter the existing NPC allocator and progression owner; Duke Fishron uses the source bobber-center spawn branch. NPC creation support is distinct from complete AI parity.

An accepted pull retires its source receipt before observers. Repeated controls, forged catch `ai`, stale packet `27/29`, replacement generations and later re-entry cannot issue a second catch. Selection changes, crowd control, death, disconnect and transfer retire the corresponding actor lifetime. A paid positive catch follows the source returning/Kill reward path; an unpaid bite carries no reward entitlement. Rewards use the existing inventory/world-item transaction and its source overflow path. See [Angler quests](angler-quest-authority-1458.md) and [fishing crates](fishing-crates-authority-1458.md) for the connected downstream operations.

The source and runtime tests cover scoped behavior; they do not establish full vanilla compatibility. Decorative bobber override accessories, unrepresented fishing equipment/loadout effects, chum projectile lifecycle, mounted/inverted bodies and Shimmer fishing effects remain outside current admission. Imported unknown local clocks do not become known from a client report. Player inventory and lifetime quest count have no server `.plr` disk owner in this repository: existing transfer preservation and `.wld` daily quest persistence must not be described as durable character saving. Official graphical-client acceptance, genuine Linux NativeAOT execution and complete special-NPC scheduling/RNG parity require separate evidence. Current checks and exact remaining work are recorded in agent memory.

The cloud bonus reads source `Main.cloudBGAlpha`, whose writer is graphical `DrawSurfaceBG`. The dedicated no-draw path leaves it zero. The runtime therefore uses live rain separately and does not substitute rain-driven `Main.cloudAlpha`; rendered-client cloud history remains outside the authoritative capture.

Special catches reuse the existing NPC spawn preview and physical allocator. Dedicated packet `130` births truncate bobber position, add Nautilus's `64` offset, then divide/multiply by `16`; direct/SP ItemCheck keeps pixel coordinates. Fishron `370` instead uses the owned bobber center plus `100`, ordinary NewNPC slot/defaults and target `255`. Red Slime `682` adopts its existing town-resident identity, two English name/profile draws and progression flag before world `7`, birth retention and public `23`; the same resident/name and unlock enter checkpoint owners. An already unlocked Red result refuses another birth even when the bite predates another player's unlock. Independent evidence comprises 42 actual official calls: seven types, two caller roles and three seeds, with 21 dedicated runtime comparisons of position/wet, defaults, target, AI, lifetime `937`, names and next RNG. This is birth/publication evidence; full NPC AI, arbitrary external WetCollision callback ordering and complete client/local RNG scheduling are not claimed.
