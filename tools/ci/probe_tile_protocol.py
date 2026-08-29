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


def find_action_semantics(source: str) -> re.Match[str]:
    pattern = re.compile(
        r"if \((?P<action>[A-Za-z_]\w*) == 0\) \{ WorldGen\.KillTile\(.*?\} "
        r"if \((?P=action) == 1\) \{.*?WorldGen\.PlaceTile\(.*?\} "
        r"if \((?P=action) == 2\) \{ WorldGen\.KillWall\(.*?\} "
        r"if \((?P=action) == 3\) \{ WorldGen\.PlaceWall\(.*?\} "
        r"if \((?P=action) == 4\) \{ WorldGen\.KillTile\(.*?noItem: true\); \}",
        re.DOTALL,
    )
    match = pattern.search(source)
    if match is None:
        raise SystemExit(
            "Terraria 1.4.5.8 packet 17 action baseline changed: expected actions 0..4 to map to "
            "KillTile, PlaceTile, KillWall, PlaceWall and KillTile(noItem:true)."
        )
    return match


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
    action_match = find_action_semantics(message_buffer)

    context_start = max(0, action_match.start() - 2400)
    context_end = min(len(message_buffer), action_match.end() + 3600)

    print("tile_manipulation_message_id=17")
    print("tile_manipulation_payload_bytes=8")
    print("tile_manipulation_wire=byte,int16,int16,int16,byte")
    print("tile_manipulation_action_0=KillTile")
    print("tile_manipulation_action_1=PlaceTile")
    print("tile_manipulation_action_2=KillWall")
    print("tile_manipulation_action_3=PlaceWall")
    print("tile_manipulation_action_4=KillTileNoItem")
    print(f"tile_manipulation_receive_context={message_buffer[context_start:context_end]}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
