# Vanilla 1.4.5.8 oceans and beaches

Canonical ordinary worlds now build both ocean bodies in the pinned `BeachesAndOceanCleanup` pass position. The
previous `Final Cleanup` `AlignOcean` repair has been removed: it used a synthetic smoothstep floor after palms,
coral and other ocean decoration had already run, so it could overwrite valid late-pass output.

The clean-room `Beaches` port now preserves the 1.4.5.8 behavior routes that define ocean geometry:

- Reset-owned `LeftBeachEnd`, `RightBeachStart`, dungeon side and the 50-tile boundary padding;
- random water starts in `[220, 260)`, including the forced 275-tile jungle-side ocean;
- a single shared-RNG choice that assigns the optional Florida depth profile to at most one coast;
- the first solid tile at the inland anchor, followed by the source `[1, 5)` vertical offset;
- the complete standard and Florida `TuneOceanDepth` breakpoint tables;
- the `depth * 0.75 - 3` water/floor split, full water rows, the 127-liquid shoreline row, sand floor and cleared walls;
- the original left-to-edge and right-to-edge column order and shared RNG consumption.

Raw tile identity and numeric profile facts are owned by `OceanGenerationCatalog1458`, not scattered through
the writer. The source-contract probe independently compares the routes and both 16-band depth tables with the pinned
TerrariaServer 1.4.5.8 decompile.

Canonical finalization also validates basin geometry. Each coast must have an edge-connected water body, high wet
coverage, a sand floor under the upper ocean body, bounded adjacent floor changes, and a measurable rise from the map
edge toward the beach. The check tolerates bounded gaps made by ocean caves and decoration. The existing canonical-size
workflow runs this finalizer for Small, Medium and Large worlds and sweeps seeds `1`, `42` and `8675309` for every size.

This closes the ocean visual-audit item. It is a scoped claim about the ordinary 1.4.5.8 `Beaches` behavior and
structural output, not a claim that the entire world generator is byte-for-byte identical to Terraria.
