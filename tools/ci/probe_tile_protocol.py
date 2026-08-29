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
    candidates = [match.start() for match in re.finditer(r"case 17:", source)]
    for start in candidates:
        # MessageBuffer contains several switches. The packet-17 receive branch is the candidate that
        # reads the fixed packet payload and then dispatches action 0/1 to WorldGen tile mutations.
        region = source[start : start + 24000]
        if region.count("ReadInt16()") < 3 or region.count("ReadByte()") < 2:
            continue

        kill = re.search(r"case 0:.*?WorldGen\.KillTile\(", region, re.DOTALL)
        place = re.search(r"case 1:.*?WorldGen\.PlaceTile\(", region, re.DOTALL)
        if kill is None or place is None:
            continue

        if kill.start() > place.start():
            continue

        # Keep the baseline intentionally narrow. More action IDs must be added only when their exact
        # server behavior is needed by TerraRuntime gameplay.
        return

    raise SystemExit(
        "Terraria 1.4.5.8 packet 17 receive semantics changed: expected action 0 -> "
        "WorldGen.KillTile and action 1 -> WorldGen.PlaceTile after the fixed payload read."
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
