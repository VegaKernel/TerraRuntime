# Runtime world cache

TerraRuntime keeps Terraria `.wld` files as the canonical persistence and recovery format. The adjacent `.runtime-world` file is a disposable startup cache and may always be deleted.

## Schema v2

Schema v2 caches the normalized runtime tile array so startup can skip the variable-length `.wld` tile RLE/flag decoder, but stores the tile payload as independently verified shards so SSD/NVMe reads and tile reconstruction can run concurrently.

Non-tile sections are still decoded and validated from the canonical `.wld`, including runtime metadata, chests, signs, NPC persistence, tile entities, pressure plates, town rooms, bestiary, creative powers and the footer. Future cache sections can use the same independent-section model once startup profiling proves they are worth persisting.

File name:

```text
world.wld
world.runtime-world
```

Layout:

```text
96-byte fixed header
shard 0 normalized WorldTile records
shard 1 normalized WorldTile records
...
shard N normalized WorldTile records
N * 48-byte shard integrity entries
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
- total tile payload byte length;
- shard count and shard-entry size.

Each tile record is 16 bytes and explicitly stores normalized fields instead of dumping the CLR struct layout: tile type, wall type, frame coordinates, flags, liquid amount, tile/wall paint, shape and liquid kind. Liquid state is currently part of the tile record, matching the in-memory `WorldTile` model.

Each shard targets 16 MiB of encoded tile payload. The trailing shard table records the tile start index, tile count and SHA-256 for every shard. Cache loading uses positional `RandomAccess` reads with bounded parallelism. The default is at most four concurrent shard reads, which is conservative for SATA SSD while still exposing useful queue depth on NVMe. The read option can later be raised for measured NVMe workloads without changing the file schema.

## Validation and fallback

A cache hit requires all structural fields, the canonical source fingerprint, the exact deterministic shard layout and every shard hash to validate. Unsupported schema, stale source hash, truncation, invalid tile data or any shard failure is a cache miss, never a world-load success. Shard failures report the shard index through the diagnostic detail code.

On a cache miss the server loads the canonical `.wld` normally. Only after that full load succeeds is a replacement cache written. Cache writes remain sequential and use `world.runtime-world.tmp`, flush the complete file, and rename it over the old cache. Failure to create or replace the cache does not affect the canonical `.wld` or prevent the server from starting.

## Startup profiling

The server emits one machine-readable `startup_profile` line containing at least:

- `.wld` file read time;
- cache load time;
- canonical loader total time;
- envelope/header parse time;
- tile storage allocation time;
- tile decode time;
- non-tile section time;
- cache rebuild/write time;
- join-bootstrap preparation time;
- `WorldReady` and `NetworkReady` wall time;
- process allocation delta during startup.

These measurements decide what schema v3 should persist. Likely candidates are independently loadable metadata, chests/signs/tile entities and prebuilt section/bootstrap data. The loader should parallelize only independent immutable sections and publish the completed world at one validation/commit point.
