#!/usr/bin/env python3
import argparse
import re
from pathlib import Path


def compact(text: str) -> str:
    return re.sub(r"\s+", " ", text).strip()


def verify_wire_contract(source: str) -> None:
    expected = compact(
        """
        case 17:
            writer.Write((byte)number);
            writer.Write((short)number2);
            writer.Write((short)number3);
            writer.Write((short)number4);
            writer.Write((byte)number5);
            break;
        """
    )
    if expected not in source:
        raise SystemExit(
            "Terraria 1.4.5.8 packet 17 serialization contract changed: expected "
            "byte action + int16 x + int16 y + int16 data + byte style."
        )


def verify_action_semantics(source: str) -> None:
    kill_positions = [match.start() for match in re.finditer(r"WorldGen\.KillTile\(", source)]
    for kill_position in kill_positions:
        start = max(0, kill_position - 8000)
        end = min(len(source), kill_position + 16000)
        region = source[start:end]
        kill = re.search(r"case 0:.*?WorldGen\.KillTile\(", region, re.DOTALL)
        place = re.search(r"case 1:.*?WorldGen\.PlaceTile\(", region, re.DOTALL)
        if kill is None or place is None or kill.start() > place.start():
            continue

        prefix = region[: kill.start()]
        if prefix.count("ReadInt16()") < 3 or prefix.count("ReadByte()") < 2:
            continue
        return

    # Keep diagnostics intentionally bounded. This is only source-contract evidence, not a vendored source copy.
    print(f"diagnostic_killtile_occurrences={len(kill_positions)}")
    for index, position in enumerate(kill_positions[:4]):
        context = source[max(0, position - 500) : min(len(source), position + 1000)]
        print(f"diagnostic_killtile_context_{index}={context}")

    raise SystemExit(
        "Terraria 1.4.5.8 packet 17 receive semantics were not recognized: expected action 0/1 "
        "tile mutation dispatch near the packet payload reader."
    )


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Verify Terraria 1.4.5.8 packet-17 tile-manipulation source contracts."
    )
    parser.add_argument("--net-message", required=True)
    parser.add_argument("--message-buffer", required=True)
    args = parser.parse_args()

    net_message = compact(Path(args.net_message).read_text(encoding="utf-8"))
    message_buffer = compact(Path(args.message_buffer).read_text(encoding="utf-8"))
    verify_wire_contract(net_message)
    verify_action_semantics(message_buffer)

    print("tile_manipulation_message_id=17")
    print("tile_manipulation_payload_bytes=8")
    print("tile_manipulation_wire=byte,int16,int16,int16,byte")
    print("tile_manipulation_action_0=KillTile")
    print("tile_manipulation_action_1=PlaceTile")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
