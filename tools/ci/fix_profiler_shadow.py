from pathlib import Path

path = Path("src/TerraRuntime.World/RuntimeWorldSnapshotProfiler.cs")
text = path.read_text()
replacements = {
    "out TileShardDescriptor decoded))": "out TileShardDescriptor decodedShard))",
    "shards[shardIndex] = decoded;": "shards[shardIndex] = decodedShard;",
}
for old, new in replacements.items():
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"expected exactly one {old!r}, found {count}")
    text = text.replace(old, new, 1)
path.write_text(text)
