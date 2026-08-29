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


def verify_kill_fail_semantics(source: str, action_match: re.Match[str]) -> None:
    action_block = action_match.group(0)
    kill = re.search(
        r"WorldGen\.KillTile\((?P<x>[A-Za-z_]\w*), (?P<y>[A-Za-z_]\w*), (?P<fail>[A-Za-z_]\w*)\);",
        action_block,
    )
    if kill is None:
        raise SystemExit("Could not isolate packet 17 action-0 KillTile fail argument.")

    fail = re.escape(kill.group("fail"))
    context = source[max(0, action_match.start() - 1800):action_match.start()]
    assignment = re.search(
        rf"bool {fail} = (?P<data>[A-Za-z_]\w*) == 1;",
        context,
    )
    if assignment is None:
        raise SystemExit(
            "Terraria 1.4.5.8 packet 17 action-0 fail flag is no longer derived as data == 1."
        )


def verify_server_relay(source: str, action_match: re.Match[str]) -> None:
    action_name = action_match.group("action")
    action = re.escape(action_name)
    context = source[action_match.start():min(len(source), action_match.end() + 6500)]
    relay = re.search(
        rf"NetMessage\.TrySendData\(17, -1, whoAmI, null, {action}, [^;]+\);",
        context,
    )
    if relay is None:
        raise SystemExit(
            "Terraria 1.4.5.8 packet 17 server relay no longer uses ignoreClient=whoAmI."
        )

    relay_tail = context[max(0, relay.start() - 650):relay.end()]
    failed_hit_relay_guard = re.compile(
        rf"if \(Main\.netMode == 2\) \{{ "
        rf"if \([A-Za-z_]\w*\) \{{ NetMessage\.SendTileSquare\([^;]+\); \}} "
        rf"else if \(\({action} != 1 && {action} != 21\) \|\| "
        rf"!TileID\.Sets\.Falling\[[^\]]+\] \|\| Main\.tile\[[^\]]+\]\.active\(\)\) \{{ "
        rf"NetMessage\.TrySendData\(17, -1, whoAmI, null, {action}, [^;]+\); \}} \}}"
    )
    if failed_hit_relay_guard.search(relay_tail) is None:
        raise SystemExit(
            "Terraria 1.4.5.8 packet 17 relay tail changed: action 0 is no longer proven to relay "
            "independently of the data==1 fail flag."
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
    action_match = find_action_semantics(message_buffer)
    verify_kill_fail_semantics(message_buffer, action_match)
    verify_server_relay(message_buffer, action_match)

    print("tile_manipulation_message_id=17")
    print("tile_manipulation_payload_bytes=8")
    print("tile_manipulation_wire=byte,int16,int16,int16,byte")
    print("tile_manipulation_action_0=KillTile")
    print("tile_manipulation_action_0_fail=data==1")
    print("tile_manipulation_action_0_failed_hit_relay=exclude_sender")
    print("tile_manipulation_action_1=PlaceTile")
    print("tile_manipulation_action_2=KillWall")
    print("tile_manipulation_action_3=PlaceWall")
    print("tile_manipulation_action_4=KillTileNoItem")
    print("tile_manipulation_server_relay=exclude_sender")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
