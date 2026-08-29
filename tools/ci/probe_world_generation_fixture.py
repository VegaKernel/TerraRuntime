#!/usr/bin/env python3
import argparse
import hashlib
import json
import struct
from pathlib import Path

EXPECTED_VERSION = 326
EXPECTED_MAGIC = b"relogic"
EXPECTED_FILE_TYPE = 2
EXPECTED_SECTION_COUNT = 11
EXPECTED_FRAME_IMPORTANCE_COUNT = 754
EXPECTED_FRAME_IMPORTANCE_BYTES = (EXPECTED_FRAME_IMPORTANCE_COUNT + 7) // 8
EXPECTED_ENVELOPE_LENGTH = (
    4 + len(EXPECTED_MAGIC) + 1 + 4 + 8 + 2 + EXPECTED_SECTION_COUNT * 4 + 2 + EXPECTED_FRAME_IMPORTANCE_BYTES
)


def read_exact(data: bytes, offset: int, length: int) -> tuple[bytes, int]:
    end = offset + length
    if end > len(data):
        raise SystemExit(f"World file truncated at offset {offset}: need {length} bytes, have {len(data) - offset}.")
    return data[offset:end], end


def unpack(fmt: str, data: bytes, offset: int) -> tuple[object, int]:
    size = struct.calcsize(fmt)
    raw, offset = read_exact(data, offset, size)
    return struct.unpack(fmt, raw)[0], offset


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Extract the canonical Terraria 1.4.5.8 .wld envelope from an official generated fixture."
    )
    parser.add_argument("--world", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()

    world_path = Path(args.world)
    data = world_path.read_bytes()
    offset = 0

    version, offset = unpack("<i", data, offset)
    magic, offset = read_exact(data, offset, len(EXPECTED_MAGIC))
    file_type, offset = unpack("<B", data, offset)
    revision, offset = unpack("<I", data, offset)
    favorite_flags, offset = unpack("<Q", data, offset)
    section_count, offset = unpack("<h", data, offset)

    if version != EXPECTED_VERSION:
        raise SystemExit(f"Expected world version {EXPECTED_VERSION}, got {version}.")
    if magic != EXPECTED_MAGIC:
        raise SystemExit(f"Expected magic {EXPECTED_MAGIC!r}, got {magic!r}.")
    if file_type != EXPECTED_FILE_TYPE:
        raise SystemExit(f"Expected file type {EXPECTED_FILE_TYPE}, got {file_type}.")
    if section_count != EXPECTED_SECTION_COUNT:
        raise SystemExit(f"Expected {EXPECTED_SECTION_COUNT} sections, got {section_count}.")

    section_offsets: list[int] = []
    for _ in range(section_count):
        value, offset = unpack("<i", data, offset)
        section_offsets.append(int(value))

    frame_importance_count, offset = unpack("<h", data, offset)
    if frame_importance_count != EXPECTED_FRAME_IMPORTANCE_COUNT:
        raise SystemExit(
            f"Expected {EXPECTED_FRAME_IMPORTANCE_COUNT} frame-importance bits, got {frame_importance_count}."
        )

    frame_importance, offset = read_exact(data, offset, EXPECTED_FRAME_IMPORTANCE_BYTES)
    if offset != EXPECTED_ENVELOPE_LENGTH:
        raise SystemExit(f"Envelope length mismatch: expected {EXPECTED_ENVELOPE_LENGTH}, got {offset}.")
    if section_offsets[0] != EXPECTED_ENVELOPE_LENGTH:
        raise SystemExit(
            f"First section pointer must equal envelope length {EXPECTED_ENVELOPE_LENGTH}, got {section_offsets[0]}."
        )
    if any(left >= right for left, right in zip(section_offsets, section_offsets[1:])):
        raise SystemExit(f"Section pointers are not strictly increasing: {section_offsets}")
    if section_offsets[-1] > len(data):
        raise SystemExit(
            f"Last section pointer {section_offsets[-1]} exceeds world length {len(data)}."
        )

    set_tile_ids = [
        tile_id
        for tile_id in range(frame_importance_count)
        if frame_importance[tile_id >> 3] & (1 << (tile_id & 7))
    ]
    fixture = {
        "terraria_version": "1.4.5.8",
        "world_file_version": version,
        "file_type": file_type,
        "revision": revision,
        "favorite_flags": favorite_flags,
        "section_count": section_count,
        "section_offsets": section_offsets,
        "frame_importance_count": frame_importance_count,
        "frame_importance_hex": frame_importance.hex(),
        "frame_importance_sha256": hashlib.sha256(frame_importance).hexdigest(),
        "frame_important_tile_ids": set_tile_ids,
        "world_sha256": hashlib.sha256(data).hexdigest(),
        "world_length": len(data),
    }

    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(fixture, indent=2, sort_keys=True) + "\n", encoding="utf-8")

    print(f"world_version={version}")
    print(f"world_length={len(data)}")
    print(f"frame_importance_count={frame_importance_count}")
    print(f"frame_importance_sha256={fixture['frame_importance_sha256']}")
    print(f"frame_importance_hex={fixture['frame_importance_hex']}")
    print(f"frame_important_tile_count={len(set_tile_ids)}")
    print("frame_important_tile_ids=" + ",".join(str(value) for value in set_tile_ids))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
