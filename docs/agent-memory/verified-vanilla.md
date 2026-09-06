# Verified vanilla facts

Last evidence refresh: 2026-09-06.

This file stores concise facts already checked against the locally decompiled official TerrariaServer **1.4.5.8**. It prevents repeated source archaeology, but it does not authorize guessing adjacent behavior. Unknown cases remain fail-closed until separately verified.

## Liquids

Primary evidence: `Terraria.Liquid.Update`, `Terraria.Liquid.LiquidCheck`, and the relevant liquid helper/check paths in TerrariaServer 1.4.5.8.

Verified runtime facts currently implemented/tested:

- Ordinary same-kind settling is gravity-first, then horizontal leveling when liquid remains in the source during the same update.
- Horizontal leveling uses the source-backed 2/3/4/5/7-cell averaging shape.
- The full-source downward-fill edge preserves the vanilla `255 -> 254` source case when the lower cell receives the final unit.
- 5/7-cell averaging preserves the fed source column in the verified branch where neighbours already equal the rounded level.
- Lava ordinary flow delay is 5 liquid updates.
- Honey ordinary flow delay is 10 liquid updates.
- Water does not own the material merge itself; it wakes adjacent lava/honey/shimmer cells and the foreign-liquid update owns `LiquidCheck` merge placement.
- Water + lava creates Obsidian tile `56`.
- Water + honey creates Honey Block tile `229`.
- Lava + honey creates Crispy Honey Block tile `230`.
- Shimmer contact wins the verified source-order selection and creates Shimmer Block tile `659`.
- The verified ordinary open-cell merge threshold is 24 liquid units.
- Left/right/up foreign-liquid handling and the lower-cell sub-24 source clear follow `LiquidCheck` ordering.
- Material mutations and the verified lower-cell sub-24 foreign-liquid clear use packet `20` tile-square replication, not packet `48` alone.
- In dedicated-server `Liquid.UpdateLiquid`, stable active entries retire at `10 + activePlayersInSlots0To14 / 3`; Terraria 1.4.5.8 counts only player slots `0..14` for this liquid-cycle threshold.
- A changed liquid amount resets `kill` to zero and schedules the cell above; unchanged entries increment `kill`.
- A stable `254` liquid amount is normalized to `255` when the active entry retires.
- Water with `y > Main.UnderworldLayer` loses two liquid units per `Liquid.Update`; `Main.UnderworldLayer == Main.maxTilesY - 200`.

Known liquid gaps that must not be guessed:

- active `tileObsidianKill` replacement/destruction paths;
- `tileCut` side effects;
- container-specific lower-cell handling;
- quick-settle/panic/forced-settle lifecycle branches beyond the ordinary dedicated-server active-entry retirement path;
- `quickFall` / `quickSettle` generation modes;
- complete post-load liquid initialization behavior.

## Working rule for new vanilla facts

When adding a fact:

1. name the official type/method or table used as evidence;
2. state only the behavior needed by TerraRuntime;
3. put literal IDs/constants here only after they are source-pinned and represented by typed production constants where appropriate;
4. add a regression test that fails under the previous/broken behavior;
5. update/remove the corresponding "known gap" entry.
