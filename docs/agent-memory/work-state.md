# Work state

Updated: 2026-09-06.

This is the resume point for the next agent/session. It intentionally contains the current clean checkpoint plus the next verified boundary, so the repository does not need to be re-analysed from zero.

## Last clean checkpoint

- Checkpoint: `/TZ/TerraRuntime-main-TZ-25.zip`.
- Base: TZ-24 liquid material reactions and packet-20 material replication.
- TZ-25 adds the repository-local agent memory under `docs/agent-memory/` and closes the ordinary dedicated-server liquid lifecycle slice described below.

## Liquid state at TZ-25

Source-backed against TerrariaServer 1.4.5.8 `Liquid.Update`, `Liquid.UpdateLiquid`, `Liquid.LiquidCheck` and `Main.UnderworldLayer`:

- ordinary same-kind gravity-first settling and 2/3/4/5/7-cell horizontal leveling;
- partial downward-fill continuation;
- vanilla `255 -> 254` one-unit preservation case and 5/7 fed-source-column exception;
- lava/honey flow delays of 5/10 liquid updates;
- ordinary open/inactive-cell water/lava/honey/shimmer material reactions with packet-20 tile-square replication;
- bounded active-entry processing where work re-enqueued during a TerraRuntime tick cannot consume another logical liquid update in that same tick;
- ordinary dedicated-server `kill` lifecycle: amount changes reset `kill` and wake the cell above, stable entries advance `kill`, stable `254` normalizes to `255` on retirement;
- dedicated-server retirement threshold `10 + activePlayersInSlots0To14 / 3`;
- Underworld water evaporation of two units per liquid update for `y > maxTilesY - 200`.

Still open/fail-closed:

- active `tileObsidianKill` replacement/destruction paths;
- `tileCut` side effects;
- container-specific lower-cell merge handling;
- `quickFall` / `quickSettle` generation/settle modes;
- panic/forced-settle behavior;
- complete post-load liquid initialization parity.

## Validation recorded for TZ-25

- `TerraRuntime.World` build: 0 warnings, 0 errors.
- `TerraRuntime.Application` build: 0 warnings, 0 errors.
- test assembly build: 0 warnings, 0 errors.
- `VanillaWorldLiquidSimulator1458Tests`: 24/24 green.
- wider affected liquid/snapshot/replication/mutation/runtime integration set: 63/63 green.
- the runtime integration set includes three spawned players and proves the dedicated-server retirement threshold is extended from 10 to 11 logical updates.
- `python3 tools/ci/check_documentation.py`: green, 92 mirrored RU/EN pages and 223 Markdown files checked.

## Next recommended pass

Stay on the liquid vertical unless a higher-priority bug is reported. The next source-backed boundary should be the active-cell merge side effects (`tileObsidianKill`, `tileCut`, container handling), because they sit directly adjacent to the now-implemented `LiquidCheck` material path. Do not start `quickFall`/`quickSettle` by copying worldgen behavior wholesale; first map the tables and destruction/container ownership needed by active merge cells.

Before changing that path, read `verified-vanilla.md`, inspect the exact 1.4.5.8 tables/call sites, and update this file when the pass completes or is interrupted.
