#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

PROJECTILE_COUNT = 1136
START_BRANCH = "type == 1"
STARTUP_FLAGS = {
    "Main.remixWorld": False,
    "Main.getGoodWorld": False,
}


def extract_method(text: str, signature: str) -> str:
    start = text.find(signature)
    if start < 0:
        raise RuntimeError(f"method signature not found: {signature}")
    opening = text.find("{", start)
    if opening < 0:
        raise RuntimeError("method opening brace not found")
    depth = 0
    for index in range(opening, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[opening + 1:index]
    raise RuntimeError("unterminated method")


def top_level_branches(method: str):
    lines = method.splitlines()
    branches = []
    depth = 0
    pending = None
    for index, line in enumerate(lines):
        stripped = line.strip()
        match = re.fullmatch(r"(if|else if)\s*\((.*)\)", stripped)
        if depth == 0 and match:
            pending = (match.group(1), match.group(2))
        elif depth == 0 and stripped == "else":
            pending = ("else", None)

        opens = line.count("{")
        closes = line.count("}")
        if pending is not None and depth == 0 and opens:
            branch_depth = 0
            for end in range(index, len(lines)):
                branch_depth += lines[end].count("{") - lines[end].count("}")
                if branch_depth == 0:
                    break
            else:
                raise RuntimeError("unterminated top-level branch")
            kind, condition = pending
            branches.append((kind, condition, "\n".join(lines[index + 1:end])))
            pending = None
        depth += opens - closes
    return branches


def compile_type_condition(condition: str):
    identifiers = set(re.findall(r"\b[A-Za-z_][A-Za-z0-9_.]*\b", condition))
    if identifiers - {"type"}:
        raise RuntimeError(f"unsupported type condition: {condition}")
    expression = condition.replace("||", " or ").replace("&&", " and ")
    expression = re.sub(r"!(?!=)", " not ", expression)
    return compile(expression, "<Projectile.SetDefaults type condition>", "eval")


def startup_hostile_value(body: str) -> bool:
    stack = []
    pending = None
    value = False
    for line in body.splitlines():
        stripped = line.strip()
        match = re.fullmatch(r"(?:else\s+)?if\s*\((.*)\)", stripped)
        if match:
            pending = ("if", match.group(1))
        elif stripped == "else":
            pending = ("else", None)

        for _ in range(line.count("{")):
            stack.append(pending)
            pending = None

        assignment = re.search(r"\bhostile\s*=\s*(true|false)\s*;", stripped)
        if assignment:
            execute = True
            for nested in stack:
                if nested is None:
                    continue
                kind, condition = nested
                if kind == "else":
                    raise RuntimeError("hostile assignment under nested else is unsupported")
                identifiers = set(re.findall(r"\b[A-Za-z_][A-Za-z0-9_.]*\b", condition))
                if identifiers - set(STARTUP_FLAGS):
                    raise RuntimeError(f"unsupported nested hostile condition: {condition}")
                expression = condition
                for name, flag_value in STARTUP_FLAGS.items():
                    expression = expression.replace(name, str(flag_value))
                expression = expression.replace("||", " or ").replace("&&", " and ")
                if not eval(expression, {"__builtins__": {}}, {}):
                    execute = False
                    break
            if execute:
                value = assignment.group(1) == "true"

        for _ in range(line.count("}")):
            if stack:
                stack.pop()
    return value


def extract(projectile_cs: Path) -> list[int]:
    text = projectile_cs.read_text(encoding="utf-8", errors="strict")
    method = extract_method(text, "void SetDefaults(int Type)")
    branches = top_level_branches(method)
    start = next((i for i, (_, condition, _) in enumerate(branches) if condition == START_BRANCH), None)
    if start is None:
        raise RuntimeError("Projectile.SetDefaults type chain was not found")

    prepared = []
    for kind, condition, body in branches[start:]:
        prepared.append((
            kind,
            None if condition is None else compile_type_condition(condition),
            startup_hostile_value(body),
        ))

    hostile = []
    for projectile_type in range(1, PROJECTILE_COUNT):
        for kind, condition, value in prepared:
            if condition is None or eval(condition, {"__builtins__": {}}, {"type": projectile_type}):
                if value:
                    hostile.append(projectile_type)
                break
        else:
            raise RuntimeError(f"type {projectile_type} matched no SetDefaults branch")
    return hostile


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("projectile_cs", type=Path)
    parser.add_argument("--expect-count", type=int)
    args = parser.parse_args()
    hostile = extract(args.projectile_cs)
    if args.expect_count is not None and len(hostile) != args.expect_count:
        raise SystemExit(f"expected {args.expect_count} hostile types, got {len(hostile)}")
    print(json.dumps({"count": len(hostile), "types": hostile}, separators=(",", ":")))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
