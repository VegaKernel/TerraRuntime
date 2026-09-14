"""Verify the authoritative Moon Lord death boundaries against the pinned local reference."""

import argparse
import hashlib
import json
from pathlib import Path
import re


def method(source: str, name: str) -> str:
    match = re.search(r"\bvoid\s+" + re.escape(name) + r"\s*\(", source)
    if match is None:
        raise SystemExit(f"Missing reference method: {name}")
    start = source.index("{", match.end())
    depth = 1
    end = start + 1
    while depth and end < len(source):
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return re.sub(r"\s+", " ", source[start:end]).replace("this.", "")


def require(block: str, expression: str, label: str) -> None:
    if re.search(expression, block) is None:
        raise SystemExit(f"Moon Lord reference contract changed: {label}")


def require_death_velocity(death: str) -> None:
    # On Windows ILSpy resolves XNA from the GAC. Linux lacks XNA and renders the same
    # verified constructor as a by-ref .ctor on a temporary. Pin the local identity,
    # adjacent constructor, both Lerp operands and amount; do not accept any nearby 0.98.
    patterns = (
        r"velocity = Vector2\.Lerp\(velocity, new Vector2\(0f, -0\.5f\), 0\.98f\);",
        r"velocity = Vector2\.Lerp\(value2: new Vector2\(0f, -0\.5f\), value1: velocity, amount: 0\.98f\);",
        r"Vector2 (?P<target>\w+) = default\(Vector2\); "
        r"\(\(Vector2\)\(ref (?P=target)\)\)\.\.ctor\(0f, -0\.5f\); "
        r"velocity = Vector2\.Lerp\(velocity, (?P=target), 0\.98f\);",
    )
    if not any(re.search(pattern, death) for pattern in patterns):
        raise SystemExit("Moon Lord reference contract changed: death velocity lerp")


def require_shell_translation(teleport: str, slot: int) -> None:
    delta = re.search(r"Vector2 (\w+) = Main.player\[target\].Center - Vector2.UnitY \* 150f - base.Center;", teleport)
    if delta is None:
        raise SystemExit("Moon Lord reference contract changed: teleport delta identity")
    node = rf"Main.npc\[\(int\)localAI\[{slot}\]\]"
    displacement = re.escape(delta.group(1))
    # Missing XNA metadata causes ILSpy to retain an NPC alias around the compound
    # assignment. Accept either spelling while pinning slot, alias, delta and sync.
    move = rf"(?:{node}\.position \+= {displacement};|NPC (?P<part>\w+) = {node}; (?P=part)\.position \+= {displacement};)"
    expression = rf"if \({node}\.active\) \{{ {move} {node}\.netUpdate = true; \}}"
    require(teleport, expression, f"translate shell slot {slot}")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--assembly", type=Path, required=True)
    parser.add_argument("--npc", type=Path, required=True)
    args = parser.parse_args()
    digest = hashlib.sha256(args.assembly.read_bytes()).hexdigest()
    if digest != "d87e3faf08637f6be8882c63e7f11fb7e792b0230006309618473ece0f863e1e":
        raise SystemExit("Reference assembly is not the pinned TerrariaServer 1.4.5.8")
    raw = args.npc.read_bytes()
    source = raw.decode("utf-16" if raw.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig")
    core = method(source, "AI_077_MoonLordCore")
    initialization = core[core.index("if (localAI[3] == 0f)"):core.index("if (ai[0] == -2f)")]
    require(initialization, r"localAI\[3\] = 1f;.*?ai\[0\] = -1f;", "initialize core before state branches")
    if "ai[1]" in initialization:
        raise SystemExit("Moon Lord reference contract changed: initialization preserves timer")
    intro = core[core.index("if (ai[0] == -1f)"):core.index("if (ai[0] == 0f)")]
    require(intro, r"if \(ai\[1\] == 60f\)", "exact introduction completion tick")
    if re.search(r"velocity\s*(?:=|\*=)", intro):
        raise SystemExit("Moon Lord reference contract changed: intro preserves velocity")
    require(core, r"ai\[0\] != -1f && ai\[0\] != 2f && Main.rand.Next\(200\) == 0", "sound RNG state gate")
    require(core, r"Main.rand.Next\(93, 100\)", "sound RNG continuation")
    returning = core[core.index("if (ai[0] == -2f)"):core.index("if (ai[0] == -1f)")]
    require(returning, r"ai\[2\] = Main.rand.Next\(3\); ai\[2\] = 0f;", "return RNG draw before forced zero")
    require(core, r"localAI\[k\] = array\[k\]", "retain allocated shell slots")
    shell = core[core.index("if (ai[0] == 0f)"):core.index("else if (ai[0] == 1f)")]
    for slot, type_id in ((0, 397), (1, 397), (2, 396)):
        require(shell, rf"localAI\[{slot}\] < 0f", f"missing shell slot {slot}")
        require(shell, rf"!Main.npc\[\(int\)localAI\[{slot}\]\].active", f"inactive shell slot {slot}")
        require(shell, rf"Main.npc\[\(int\)localAI\[{slot}\]\].type != {type_id}", f"shell type {slot}")
    require(shell, r"if \(flag\).*?life = 0;.*?HitEffect\(\);.*?active = false", "broken shell direct removal")
    death = core[core.index("else if (ai[0] == 2f)"):core.index("else if (ai[0] == 3f)")]
    require_death_velocity(death)
    require(death, r"ai\[1\](?: \+= 1f|\+\+)", "death clock advances")
    require(death, r"if \(ai\[1\] == 60f\)", "attack cleanup tick")
    cleanup = death[:death.index("if (ai[1] %")]
    for type_id in (456, 462, 455, 452, 454):
        require(cleanup, rf"projectile\.type == {type_id}\b", f"cleanup projectile {type_id}")
    require(cleanup, r"projectile\.Kill\(\)", "projectile kill boundary")
    require(cleanup, r"type == 400\b.*?active = false", "True Eye removal")
    require(death, r"if \(ai\[1\] >= 600f\).*?life = 0;.*?checkDead\(\)", "terminal death tick")
    require(core, r"ai\[0\] == 2f \|\| ai\[0\] == 3f", "death survives missing player target")
    departure = core[core.index("else if (ai[0] == 3f)"):]
    require(departure, r"if \(ai\[1\] == 40f\)", "departure attack cleanup tick")
    for type_id in (456, 462, 455, 452, 454):
        require(departure, rf"projectile\w*\.type == {type_id}\b", f"departure projectile {type_id}")
    require(departure, r"projectile\w*\.active = false;.*?NetMessage.SendData\(27,", "departure silent removal with packet27")
    require(departure, r"if \(ai\[1\] >= 60f\)", "departure terminal tick")
    for type_id in (400, 397, 396):
        require(departure, rf"\.type == {type_id}\b", f"departure global part {type_id}")
    require(departure, r"active = false;.*?NetMessage.SendData\(23,.*?LunarApocalypseIsUp = false;.*?NetMessage.SendData\(7\)",
            "departure NPC removal and event notification")
    teleport = core[core.index("Distance(Main.player[target].Center) > 2400f") :]
    require(teleport, r"ai\[0\] = -2f", "distance teleport state")
    if re.search(r"ai\[1\]\s*=", teleport):
        raise AssertionError("distance teleport must retain its timer")
    require(teleport, r"Main.player\[target\].Center - Vector2.UnitY \* 150f - base.Center", "teleport delta")
    for slot in range(3):
        require_shell_translation(teleport, slot)
    require(teleport, r"active && .*?type == 400.*?position \+=.*?netUpdate = true", "global True Eye teleport sync")
    for name in ("AI_078_MoonLordHands", "AI_079_MoonLordHead", "AI_081_TrueEyeOfCthulhu"):
        body = method(source, name)
        require(body, r"Main\.npc\[\(int\)ai\[3\]\]\.type != 398", name + " exact owner slot")
        require(body, r"life = 0;.*?active = false", name + " orphan removal")
    head = method(source, "AI_079_MoonLordHead")
    require(head, r"dontTakeDamage = localAI\[3\] >= 15f", "head incoming eyelid damage gate")
    require(head, r"velocity = Vector2.Zero", "head anchored velocity")
    require(head, r"ai\[0\] = -3f; return;", "head death transition early return")
    require(head, r"Main.rand.NextDouble\(\)", "head telegraph shared random draw")
    require(head, r"(?:new Vector2\(|\.ctor\()0f, 216f\)", "head leech mouth offset")
    require(head, r"456, 0, 0f, Main.myPlayer, whoAmI \+ 1,", "head addressed leech spawn")
    require(head, r"localAI\[2\] > 14f", "head mouth animation bound")

    print(json.dumps({"reference": "TerrariaServer 1.4.5.8", "sha256": digest,
                      "cleanup_tick": 60, "terminal_tick": 600, "orphan_families": [78, 79, 81]}))


if __name__ == "__main__":
    main()
