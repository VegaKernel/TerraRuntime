#!/usr/bin/env python3
import argparse
import pathlib
import re


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--world-gen", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()

    source = pathlib.Path(args.world_gen).read_text(encoding="utf-8")
    assignments = []
    for name in ("waterLine", "lavaLine"):
        pattern = re.compile(rf"GenVars\.{name}\s*=\s*[^;]+;")
        matches = list(pattern.finditer(source))
        if not matches:
            raise SystemExit(f"Could not locate GenVars.{name} assignment in pinned WorldGen source")
        for match in matches:
            start = max(0, match.start() - 240)
            end = min(len(source), match.end() + 240)
            context = " ".join(source[start:end].split())
            assignments.append((name, match.group(0), context))

    output = pathlib.Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    lines = [
        "source=TerrariaServer 1.4.5.8",
        "scope=JunglePass layer prerequisites",
    ]
    for index, (name, statement, context) in enumerate(assignments):
        lines.append(f"assignment_{index:03d}_name={name}")
        lines.append(f"assignment_{index:03d}_statement={statement}")
        lines.append(f"assignment_{index:03d}_context={context}")

    output.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("\n".join(lines))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
