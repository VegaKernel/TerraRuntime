# Offline gameplay behavior oracle

`tools/vanilla.py gameplay run` executes the pinned original Terraria `1.4.5.8` method and the current compiled runtime helpers in separate SDK-only processes. `gameplay compare` validates and compares their observations. Reflection and official assembly loading stay in `GameplayProbe`; shipping projects do not reference it.

The first supported scenario, `pick-ammo-random-conservation-v1`, executes twenty-four original `Player.PickAmmo` calls with Wooden Bow `39`. The explicit inventory contains Wooden, Flaming and Unholy arrows in ordinary, coin and ammo slots; the coin-slot occurrence participates twice in vanilla's default selection order. Random cycling and preprojected Magic Quiver, Ammo Box, Ammo Reservation Potion and `ammoCost75` exercise selector entropy and every applicable conservation offer, including offers after an earlier successful conservation.

The runtime side calls existing `VanillaAmmoCycling1458.TrySelect`, `TryAdvanceSelectedAmmo`, `VanillaProjectileWeaponCombatCatalog.PrepareAmmoConservation` and `VanillaOrdinaryBowLaunch1458.TryResolve`. Its detached caller applies the returned conservation decision to its fixture stock, including clearing the last consumed item. This stock projection does not exercise authoritative inventory admission or debit journaling. The effects are explicit inputs, not evidence that `UpdateEquips` or buff updates ran.

Each capture contains all 58 ordinary inventory slots (`type`, `stack`, `ammoFamily`, `consumable`), cycling offset/cooldown, selected physical slot, consumed-ammo identifier, conservation decision and launch projectile/damage/speed/knockback. Floating results use exact IEEE 754 binary32 signed bit patterns. Every preimage and resulting state includes `Main.rand`'s physical cursor and all 56 state slots. Per-frame preimages must continue the previous resulting state. The unused mouse slot is checked separately on the original side.

The comparator requires identical seed, context and complete input before comparing all frame fields. Different preimages produce `incomparable-input`; differing observed execution produces `behavior-mismatch`. Missing fields, unknown profiles, duplicate JSON keys, partial inventories/RNG, discontinuous frames and invalid DLL identities fail validation. Input files have a $2\,\mathrm{MiB}$ hard limit. Provenance binds the original executable/dependency hashes, actual entry, probe DLL and runtime DLL closure. The common runner additionally guards current source inputs and compiled artifacts before and after execution.

```powershell
py -3 tools/vanilla.py gameplay run --assembly /path/to/TerrariaServer.exe --output-dir .cache/gameplay-tools/ammo --seed 1458
py -3 tools/vanilla.py gameplay compare --official .cache/gameplay-tools/ammo/official.json --runtime .cache/gameplay-tools/ammo/runtime.json --report .cache/gameplay-tools/comparison.json
py -3 tools/vanilla.py gameplay run --assembly /path/to/TerrariaServer.exe --output-dir .cache/gameplay-tools/omitted --seed 1458 --control omit-step
py -3 -m unittest discover -s tools/ci -p test_vanilla_gameplay_trace.py
```

Use new cache output paths for fresh captures. The backend accepts `normal`, `omit-step` and `extra-rng`; mutation modes are runtime-only. `omit-step` skips the selector/conservation/debit transition at step seven while retaining a read-only launch query; `extra-rng` draws once after that step. Both are negative controls, never accepted parity evidence.

Final common-runner qualification matches all twenty-four frames for seeds `1458`, `42` and `8675309`; both real controls reject with no execution/provenance errors and source/tool/DLL guards unchanged. Seed `1458` selects slots `1`, `50`, `54`, `55`, performs eight debits and clears two depleted stacks. Exact records are in the [three-domain verification report](../verification/three-domain-oracles-20261028.json).

This profile qualifies an isolated `PickAmmo` boundary on synthetic projected effects. It does not run `ItemCheck`, actual equipment/buff updates, authoritative player admission, packet `27`, projectile publication, a live client or NativeAOT gameplay. A match establishes only the complete stated projection and profile; new weapons, effects or call boundaries need their own original execution and controls.
