#!/usr/bin/env python3
import argparse
import re
from pathlib import Path


def compact(text: str) -> str:
    return re.sub(r"\s+", " ", text).strip()


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Verify Terraria 1.4.5.8 packet-17 tile-manipulation wire contract."
    )
    parser.add_argument("--net-message", required=True)
    args = parser.parse_args()

    source = compact(Path(args.net_message).read_text(encoding="utf-8"))
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

    print("tile_manipulation_message_id=17")
    print("tile_manipulation_payload_bytes=8")
    print("tile_manipulation_wire=byte,int16,int16,int16,byte")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
