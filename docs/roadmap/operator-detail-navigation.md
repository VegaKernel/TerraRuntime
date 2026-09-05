# Operator detail navigation and filtering

This slice removes the fixed viewport-sized row cap from the built-in Terminal.Gui operations workspace.

## Goals

- render complete **bounded operations snapshots** in a scrollable/selectable detail surface;
- keep runtime capture bounded and detached from authoritative mutable state;
- add UI-local filtering without introducing new runtime queries or mutable-state shortcuts;
- preserve text selection while background snapshot refreshes continue;
- keep the existing Dashboard/Details/Actions/external-dashboard ownership model intact;
- exercise the behavior in unit tests and the existing ANSI/NativeAOT TUI smoke gates.

## Implemented behavior

Players, NPCs, projectile groups, item groups, Network, World and Logs now populate one read-only `TextView` rather than a fixed array of 18 labels. The viewport may be narrow or short without changing how many bounded rows are available to the operator; Terminal.Gui scrolling owns viewport navigation.

`Ctrl+F` focuses the current detail filter. Pressing Enter applies a case-insensitive local substring filter to the already captured bounded snapshot. `Ctrl+L` clears it. The header keeps the runtime-owned screen summary visible while the footer reports filtered/total row counts and shortcuts.

Filters are remembered per built-in detail screen during the current UI session. They are presentation state only and never alter `IPlayerOperations`, `INpcOperations`, `IProjectileOperations`, `IWorldItemOperations`, `INetworkOperations`, `IWorldOperations` or `ILogOperations` capture semantics.

Logs request up to 256 recent entries from the already bounded log operations surface, so filtering is useful without making the UI an unbounded log consumer.

## Responsiveness contract

The TUI thread still consumes detached snapshots from `OperationsCache`. Filtering and string formatting run only on those bounded snapshots. Detail text replacement is deferred while the operator has an active selection, preserving copy/select behavior across periodic refreshes.

No authoritative gameplay state is mutated by filtering, scrolling, selection or navigation.
