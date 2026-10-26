# Paid wiring installation in Terraria 1.4.5.8

The existing player item-use owner admits ordinary red, blue, green and yellow wrenches (`509`, `850`, `851`, `3612`) and selected actuators (`849`). A packet `17` operation report and its exact packet `5` resource tail complete one bounded observation cohort. The authoritative player phase must independently establish the selected item, source clocks, current connection generation, reach and owned inventory before any world change occurs. Reports received before that phase wait for it; they grant no use time or resource credit.

| Selected item | Packet `17` action | Paid resource | Use time |
|---|---|---|---|
| Red wrench `509` | `5` | Wire `530` | `5` ticks |
| Blue wrench `850` | `10` | Wire `530` | `5` ticks |
| Green wrench `851` | `12` | Wire `530` | `5` ticks |
| Yellow wrench `3612` | `16` | Wire `530` | `5` ticks |
| Actuator `849` | `8` | Selected actuator | `10` ticks |

Consuming the last selected actuator replaces only `itemTime` with the remaining animation (`14` in the captured first-use scene); `itemTimeMax` and `toolTime` remain `10`.

Wrenches consume exactly one unit from the first positive wire stack in inventory slots `0..57`, even when a later stack has more private funding. An unfunded first stack cannot be skipped. Actuators consume the selected stack. Installation in air is valid. Existing bits, wrong colors, missing payment, stale terrain, stale owners and repeats during the source cooldown do not authorize another change. The inventory journal retains its existing quantity and custody rules; an arbitrary client inventory report cannot mint payment. A late exact resource echo acknowledges the completed receipt without consuming again.

All five items use animation `15`. Wrenches have tile boost `20`; actuators have boost `0`. Simple interaction range uses the existing player-body bounds and default range `5/3`, with the source cap applied before the item boost. This admission covers prefix `0`, ordinary non-Journey context and represented range equipment. Cursor slot `58`, buffered selection ambiguity, Artisan Loaf and unrepresented range modifiers remain outside this subset. Wiring does not inherit mining's `NoBuilding` refusal: the source wiring method does not check it.

The world owner enforces `Player.CanDoWireStuffHere`: wall `350` always refuses; wall `87` refuses before Golem; below the surface in a dual-dungeon world, Temple walls `87/92/42/24` also refuse before Golem. This installation rule differs from the Plantera restriction on actuating underground Lihzahrd brick.

The player clock, one funded resource debit and the existing tile overlay adopt together before observers run. Wiring installation does not wake the liquid queue or invoke tile framing. It dirties the same section and persistence owners used by circuits and ordinary placement. Publication uses the existing packet `17` relay and canonical inventory update; subsequent pulses, section snapshots and save/load see the installed bits in the same `WorldTileStore`.

## Actuator framing and removal semantics

Circuit actuation now prepares the inactive bit and the source nine-cell `SquareTileFrame(resetFrame:false)` pass through the existing world transaction. Recursive supported removals, item allocation, RNG and final source single-cell packet `20` retain their order. A switch `136` without another support can fall off an actuated block and return its real item `538`. A supported torch remains attached because `CheckTorch` tests `active()`, unlike the switch's `nactive()` support test. The transaction preserves torch on/off state while updating its facing frame. Unsupported adjacent framing, including unrepresented torch destruction, rejects before adoption.

The admitted actuation frame pass is dry. A visited cell with liquid rejects the whole transaction because source `Liquid.AddWater` can itself trigger tile destruction and publication. Read-only wet cells outside that pass are preserved without a spurious liquid wakeup. This restriction concerns actuation framing; installing a wire or actuator bit does not frame cells and can preserve existing liquid metadata. The existing source pass does not reproduce cosmetic merge-atlas frames of ordinary stone; independent comparisons retain those original observations while comparing the supported type/flag representation and exact frame-important attachment frames.

The low-level wire/actuator mutation owner also follows source installation/removal semantics: placing an actuator does not require an active tile, and removing it clears only the actuator bit, preserving the inactive bit and unrelated metadata. This correction does not itself authorize a client removal request.

Drop comparisons distinguish the packet's reservation offer from the original server's field. Ordinary framing `FindOwner` advertises `15`, and the primitive packet `17` local-player reservation advertises `100`; the server field normally stays `0`. The shared owner now keeps the raw server field in snapshots and carries the offer only in its publication journal. The unchanged original captures independently pin both roles. See [server reservation clocks](world-item-reservation-1458.md) for direct/deferred publication, release and join behavior.

## Explicit remaining scope

Client wire cutters remain closed. Independent full `Player.ItemCheck` captures show that five visible dust allocations consume thirty extra RNG draws, while the same action with a full dust pool consumes no extra dust draws. The common `ItemCheck` draw remains in both cases: RNG cursors are `31` and `1`. The world, clock and packet result are identical, but the server has no owned client dust-pool/viewport state from which to establish that random lineage. Suppressed dedicated-server dust is not evidence of the visible client's RNG state. The retained original captures document this dependency rather than silently choosing one branch.

The new corpus contains `44` profiles: `34` real `ItemCheck` calls (`26` installation profiles and `8` conditional cutter profiles), `6` primitive cycles and `4` framing scenes. Both independent captures are byte-identical; the preceding `110`-scene corpus remains unchanged.

Mass wiring `109`, Grand Design, Multicolor Wrench, Actuation Rod, automatic builder placement, generic attachment destruction and complete wiring parity remain open. See [wiring mechanisms](wiring-mechanisms.md) for the preceding circuit scope and [the source probe](../../tools/reference/wiring1458/README.md) for reproducible original-version evidence.

The [verification record](../verification/wiring-installation-1458.json) retains the final Release, focused, fast/full, causal-control and Windows NativeAOT results, source fingerprints, comparison boundaries and shared files for integration. The final full run executes `1,566,780` cases and all `5,279` discovered methods with no failures, errors or skips; the mandatory fast run passes `25,367/25,367`.
