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
    for name in ("AI_078_MoonLordHands", "AI_079_MoonLordHead", "AI_081_TrueEyeOfCthulhu"):
        body = method(source, name)
        require(body, r"Main\.npc\[\(int\)ai\[3\]\]\.type != 398", name + " exact owner slot")
        require(body, r"life = 0;.*?active = false", name + " orphan removal")
    print(json.dumps({"reference": "TerrariaServer 1.4.5.8", "sha256": digest,
                      "cleanup_tick": 60, "terminal_tick": 600, "orphan_families": [78, 79, 81]}))


if __name__ == "__main__":
    main()
