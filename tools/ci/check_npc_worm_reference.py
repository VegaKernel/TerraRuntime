#!/usr/bin/env python3
import argparse
import hashlib
import json
import pathlib
import re


def extract_method(text: str, name: str) -> str:
    match = re.search(rf"\b(?:private|public|internal)?\s*(?:unsafe\s+)?void\s+{re.escape(name)}\s*\([^)]*\)", text)
    if not match:
        raise SystemExit(f"{name} method was not found")
    opening = text.find("{", match.end())
    if opening < 0:
        raise SystemExit(f"{name} opening brace was not found")
    depth = 0
    for index in range(opening, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[match.start():index + 1]
    raise SystemExit(f"{name} method body is truncated")


def compact(value: str) -> str:
    return re.sub(r"\s+", "", value)


parser = argparse.ArgumentParser()
parser.add_argument("npc_cs")
parser.add_argument("--json")
args = parser.parse_args()

source = pathlib.Path(args.npc_cs).read_text(encoding="utf-8", errors="ignore")
body = extract_method(source, "AI_006_Worms")
normalized = compact(body)

facts = {
    "head_initializes_root_ai3": compact("ai[3] = whoAmI;") in normalized,
    "child_copies_root_ai3": compact("Main.npc[(int)ai[0]].ai[3] = ai[3];") in normalized,
    "ordinary_predecessor_requires_active_same_ai_style": compact("!Main.npc[(int)ai[1]].active || Main.npc[(int)ai[1]].aiStyle != aiStyle") in normalized,
    "ordinary_successor_requires_active_same_ai_style": compact("!Main.npc[(int)ai[0]].active || Main.npc[(int)ai[0]].aiStyle != aiStyle") in normalized,
    "eow_single_segment_death_is_active_only": compact("!Main.npc[(int)ai[1]].active && !Main.npc[(int)ai[0]].active") in normalized,
    "eow_head_death_is_successor_active_only": compact("type == 13 && !Main.npc[(int)ai[0]].active") in normalized,
    "eow_tail_death_is_predecessor_active_only": compact("type == 15 && !Main.npc[(int)ai[1]].active") in normalized,
    "eow_body_predecessor_split_checks_ai_style": compact("type == 14 && (!Main.npc[(int)ai[1]].active || Main.npc[(int)ai[1]].aiStyle != aiStyle)") in normalized,
    "eow_body_predecessor_split_transforms_head": compact("Transform(13, ai[0]);") in normalized,
    "eow_body_successor_split_checks_ai_style": compact("type == 14 && (!Main.npc[(int)ai[0]].active || Main.npc[(int)ai[0]].aiStyle != aiStyle)") in normalized,
    "eow_body_successor_split_transforms_tail": compact("Transform(15, 0f, ai[1]);") in normalized,
}

order_tokens = [
    compact("!Main.npc[(int)ai[1]].active && !Main.npc[(int)ai[0]].active"),
    compact("type == 13 && !Main.npc[(int)ai[0]].active"),
    compact("type == 15 && !Main.npc[(int)ai[1]].active"),
    compact("type == 14 && (!Main.npc[(int)ai[1]].active || Main.npc[(int)ai[1]].aiStyle != aiStyle)"),
    compact("Transform(13, ai[0]);"),
    compact("type == 14 && (!Main.npc[(int)ai[0]].active || Main.npc[(int)ai[0]].aiStyle != aiStyle)"),
    compact("Transform(15, 0f, ai[1]);"),
]
positions = [normalized.find(token) for token in order_tokens]
facts["eow_lifecycle_branch_order"] = all(pos >= 0 for pos in positions) and positions == sorted(positions)

failed = [name for name, passed in facts.items() if not passed]
report = {
    "schemaVersion": 1,
    "reference": "TerrariaServer 1.4.5.8 / NPC.AI_006_Worms",
    "methodSha256": hashlib.sha256(body.encode("utf-8")).hexdigest(),
    "methodLines": len(body.splitlines()),
    "facts": facts,
    "passed": not failed,
}
print("NPC_WORM_SOURCE_CONTRACT=" + json.dumps(report, sort_keys=True))
if args.json:
    output = pathlib.Path(args.json)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2, sort_keys=True), encoding="utf-8")
if failed:
    raise SystemExit("NPC worm source contract failed: " + ", ".join(failed))
