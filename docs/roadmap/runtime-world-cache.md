# Runtime world cache

TerraRuntime keeps Terraria `.wld` files as the canonical persistence and recovery format. The adjacent `.runtime-world` file is a disposable startup cache and may always be deleted.

## Schema v1

The first schema caches the normalized runtime tile array so startup can skip the variable-length `.wld` tile RLE/flag decoder. Non-tile sections are still decoded and validated from the canonical `.wld`, including runtime metadata, chests, signs, NPC persistence, tile entities, pressure plates, town rooms, bestiary, creative powers and the footer.

File name:

```text
world.wld
world.runtime-world
```

Layout:

```text
96-byte fixed header
N * 16-byte normalized WorldTile records
32-byte SHA-256 of the tile payload
```

The fixed header contains:

- magic `TRWCACHE`;
- runtime cache schema version;
- header size;
- canonical `.wld` byte length;
- SHA-256 of the complete canonical `.wld`;
- Terraria world format version;
- world width and height in tiles;
- tile-record layout size;
- tile count;
- payload byte length;
- reserved compatibility field.

Each v1 tile record explicitly stores the normalized fields instead of dumping the CLR struct layout: tile type, wall type, frame coordinates, flags, liquid amount, tile/wall paint, shape and liquid kind. The final byte is reserved and must be zero. This keeps the cache independent from CLR padding and NativeAOT layout details.

## Validation and fallback

A cache hit requires all structural fields, the canonical source fingerprint and the tile payload hash to validate. Unsupported schema, stale source hash, truncation, invalid tile data or any other cache failure is a cache miss, never a world-load success.

On a cache miss the server loads the canonical `.wld` normally. Only after that full load succeeds is a replacement cache written. Cache writes use `world.runtime-world.tmp`, flush the complete file, and rename it over the old cache. Failure to create or replace the cache does not affect the canonical `.wld` or prevent the server from starting.

## Current scope and next measurements

Schema v1 deliberately targets the expensive tile reconstruction path first. It is not yet the final Phase 5 runtime image. Before caching more state, startup telemetry should separate `.wld` file read, source hashing, tile reconstruction, non-tile section decode, initial section/bootstrap preparation, total `WorldReady`, allocations and cache I/O. The next schema should only add post-load state that measurements show to be worth persisting.
