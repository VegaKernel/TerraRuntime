# Local weapon and tool continuity 1.4.5.8

[Русский](../ru/local-weapon-tool-continuity-1458.md) · [Owned item use](player-item-use-1458.md) · [Tool authority](tool-use-authority-1458.md) · [Placement continuity](local-placement-tool-continuity-1458.md)

## Supported clocks and existing authority

The connected component preserves local item clocks when a represented melee, ranged or channelled magic use is followed by a tool. Local clocks and dedicated-remote presentation clocks remain separate. Both reuse the common source clock kernel and the same already-consumed critical-chance sample; packet arrival does not add a simulation step.

The clock catalog contains exactly 41 retained definitions:

| Family | Count | Item IDs |
|---|---:|---|
| Melee | 21 | `4, 24, 482, 483, 484, 653, 656, 659, 921, 1185, 1192, 1199, 2517, 2745, 3484, 3490, 3496, 3502, 3508, 3514, 3520` |
| Bows | 9 | `39, 99, 3480, 3486, 3492, 3498, 3504, 3510, 3516` |
| Guns | 8 | `98, 219, 533, 534, 679, 964, 1929, 4703` |
| Channelled magic | 3 | `113, 218, 495` |

These definitions describe source clocks and explicit prefix speed resolution. They do not create new launch, melee-hit, ammunition, inventory or world-edit permissions. Projectile births still pass through the existing strict source producer and owned ammo/mana payment. Tool actions still require their complete selected-item, local-stage, reach, building, hit-cache and world explanation. The runtime matrix exercises both packet-first and phase-first continuity for all 41 definitions, with additional boundary cases; it does not cover every prefix, equipment or combat combination.

An existing source-qualified ranged birth can be accepted after control release without an owned local start. In that partial role, local clock knowledge remains explicitly unknown. Remote presentation continues, but the birth cannot manufacture a timer or permission for a later tool action. Transfer retires source-Shoot receipts while preserving genuinely owned remaining clocks and clearing world-bound hit credit.

## Ordered magic input and payment

A bounded receipt holds early packet `27` intent until its source phase. It does not spend mana, refill a potion, advance RNG or adopt a projectile merely because a report arrived. The real late sequence `27 → 13 → 42` can join that receipt: normalized `13` supplies the exact adjacent held-control transition, and `42` is an observation of the expected paid result. Neither report resets a clock or supplies a balance.

The existing player phase owns regeneration, feedback, the common sample and the source Shoot tail. Earlier player slots consume their common samples before a later local player's full-mana feedback. Duplicate input cannot replay these draws. The receipt retains the original launch pose while full player, input revision, inventory, private resource, equipment and phase currentness guard adoption. A stale private mutation, replacement generation or unrelated input cannot refresh the old explanation. Birth callbacks run after accepted phase adoption; consume-first draining preserves the first observer exception and does not replay another player's source payment.

Constructor-known clocks alone do not establish a local mana-feedback role. The existing tool shadow retains that qualification after a represented local mana use, including a subsequent tool or passive selection. Dedicated-only remote phases keep their original RNG behavior. Never-bound component callers retain their existing strict CheckMana-only boundary; a bound phase that loses source ownership does not fall back to it. Two additional socket scenarios follow a paid cast through a tool switch and natural recovery to full mana, checking feedback and the next tool action without resetting mana.

Mana regeneration and AutoQuickMana use the effective owned maximum, capped at `400`, including admitted equipment bonuses. The source mana-regeneration delay `4` is already present at the paid birth boundary. AutoQuickMana uses the existing ordered funded-potion selection: an unfunded first physical potion cannot be skipped to fund a later one, and acknowledgements cannot pay twice.

Fresh starts use the shared NoItems/crowd-control facts. NoItems acquired after an already owned source frame does not retroactively revoke that paid frame's permitted tail. This ordering does not admit fresh uses while blocked or claim complete crowd-control physiology.

## Existing Gem source cohorts

The seven retained staffs `739..744` and `3377` keep their existing payment, feature and volley owners. One source-Shoot receipt can retain up to two synchronous distinct reports; a twin is not overwritten by a single pending-report slot. Midpoint/repeat eligibility remains with the existing paid source epoch.

The independent Gem traces and composed runtime input have a specific distinction. For six deterministic staffs, the input preserves the original literal fields. For stochastic staff `743`, only the center offset is composed with the independently source-differential Game volley planner on the actual detached gameplay-RNG preimage. Original velocity bits, damage, knockback and AI fields remain asserted. This is a source-backed composition, not a claim that the six literal traces and the stochastic center use the same runtime checkpoint.

## Evidence and limits

The frozen private component passes 58 normal-discovery methods across 14 classes. Its two new grouped Facts include independent source checks and 201 static runtime cases using actual decoded/owned ingress. Three essential source omissions cause assertion failures on the same TestDLL: omitted feedback, omitted pre-`42` interception and omitted full capture currentness. Exact source/output restoration then passes all 58 methods. The frozen manifest is `ee18875ad0d9d5c0854eca5a7f9b3c833adf72146847e0ef126e031ec1ea5403`; these component results precede final combined shipping gates.

The retained-role and leaf-boundary successor passes 45 unchanged/new focused tests and expands the grouped runtime proof to 203 cases. Reverting only to a selected-family feedback gate fails the new cast-to-tool sibling on the same test assembly. The original dedicated-remote and strict leaf assertions and numeric fixtures remain unchanged. Final integration results are recorded separately.

Official Linux and Windows 1.4.5.8 binaries were independently repeated. The weapon wrapper records 258 profiles of 80 phases; separate mana profiles qualify regeneration/feedback ordering. Selection-struct writeback and copied hit-cache snapshots are checked. Windows arithmetic remains explicit. These bounded calls do not establish whole `Main`, complete `Player.Update`, arbitrary gear, or inter-stream Shoot scheduling. Canonical UpdatePlayers RNG and gameplay/projectile RNG retain their existing ownership.

The separate 6,196-item useStyle table is descriptive `SetDefaults` metadata, not weapon authority. Of 34 noncanonical requested IDs, 28 become Air and six alias another item. The canonical metadata accessor resolves those aliases; strict weapon admission remains unchanged. New component full-suite, actual TCP and Native acceptance are recorded by the final integration gate, not inferred from this managed component proof. Linux Native remains a separate platform qualification.
