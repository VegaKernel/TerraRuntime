# Dashboard streamline 2026-09-01

The built-in Terminal.Gui overview uses one primary operator feed and a compact right-side telemetry/roster column.

- `Server` is no longer a dashboard tile. The active Terraria port is shown in the dashboard window title.
- `Console` owns 64% of the tiled width.
- `TPS` and `Network` remain side-by-side in the top of the right column.
- `Worlds / Players` fills the remaining right column and remains the future UI target for authoritative cross-runtime player transfer.
- Log visibility and threshold are one control: `Logs OFF`, `DEBUG+`, `INFO+`, `WARN+`, `ERROR+`.
- Chat visibility remains independent through `Chat ON/OFF`.
- Graph X scaling permits sub-unit data-per-cell values, so a bounded 60-sample history expands across a maximized viewport instead of occupying only its original tile-width worth of columns.
