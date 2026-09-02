#!/usr/bin/env python3
"""Validate TerraRuntime source-project dependency direction and reject cycles."""

from __future__ import annotations

from pathlib import Path
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / "src"

# Lower numbers are more foundational. A project may reference the same or a lower layer,
# but never a higher layer. Same-layer references are intentional adapter/domain peers.
LAYERS = {
    "TerraRuntime.Contracts": 0,
    "TerraRuntime.Gameplay": 1,
    "TerraRuntime.Protocol": 1,
    "TerraRuntime.World": 1,
    "TerraRuntime.WorldGeneration": 2,
    "TerraRuntime.HostContracts": 1,
    "TerraRuntime.Transport": 1,
    "TerraRuntime.Schematics": 1,
    "TerraRuntime.Core": 2,
    "TerraRuntime.Network": 2,
    "TerraRuntime.Protocol.Multiplicity": 2,
    "TerraRuntime.Application": 3,
    "TerraRuntime": 4,
    "TerraRuntime.Extensibility": 4,
    "TerraRuntime.ExtensibleHost": 5,
}


def project_name(path: Path) -> str:
    return path.stem


def load_graph() -> tuple[dict[str, Path], dict[str, set[str]]]:
    projects = {project_name(path): path for path in sorted(SRC.glob("*/*.csproj"))}
    unknown = sorted(set(projects) - set(LAYERS))
    missing = sorted(set(LAYERS) - set(projects))
    if unknown or missing:
        details = []
        if unknown:
            details.append(f"unclassified source projects: {', '.join(unknown)}")
        if missing:
            details.append(f"classified projects missing from src: {', '.join(missing)}")
        raise ValueError("; ".join(details))

    graph: dict[str, set[str]] = {name: set() for name in projects}
    by_path = {path.resolve(): name for name, path in projects.items()}
    for name, path in projects.items():
        root = ET.parse(path).getroot()
        for reference in root.findall(".//ProjectReference"):
            include = reference.get("Include")
            if not include:
                continue
            target_path = (path.parent / include).resolve()
            target = by_path.get(target_path)
            if target is None:
                raise ValueError(f"{name} references source project outside the classified graph: {include}")
            graph[name].add(target)
    return projects, graph


def find_cycle(graph: dict[str, set[str]]) -> list[str] | None:
    visiting: set[str] = set()
    visited: set[str] = set()
    stack: list[str] = []

    def visit(node: str) -> list[str] | None:
        if node in visiting:
            start = stack.index(node)
            return stack[start:] + [node]
        if node in visited:
            return None
        visiting.add(node)
        stack.append(node)
        for target in sorted(graph[node]):
            cycle = visit(target)
            if cycle:
                return cycle
        stack.pop()
        visiting.remove(node)
        visited.add(node)
        return None

    for node in sorted(graph):
        cycle = visit(node)
        if cycle:
            return cycle
    return None


def main() -> int:
    try:
        _, graph = load_graph()
    except (ET.ParseError, OSError, ValueError) as exc:
        print(f"project-reference gate failed: {exc}", file=sys.stderr)
        return 1

    failures: list[str] = []
    cycle = find_cycle(graph)
    if cycle:
        failures.append("project-reference cycle: " + " -> ".join(cycle))

    for source, targets in sorted(graph.items()):
        for target in sorted(targets):
            if LAYERS[target] > LAYERS[source]:
                failures.append(
                    f"upward project reference: {source} (layer {LAYERS[source]}) -> "
                    f"{target} (layer {LAYERS[target]})"
                )

    if failures:
        for failure in failures:
            print(failure, file=sys.stderr)
        return 1

    edge_count = sum(len(targets) for targets in graph.values())
    print(f"project-reference gate passed: {len(graph)} projects, {edge_count} edges, no cycles/upward references")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
