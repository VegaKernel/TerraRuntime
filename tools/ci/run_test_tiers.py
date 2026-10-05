"""Run audited xUnit method tiers without deleting or sampling golden rows."""
from __future__ import annotations

import argparse
import fnmatch
import json
from pathlib import Path
import re
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
POLICY = Path(__file__).with_name("test_tiers.json")


def load_policy(path: Path) -> dict:
    policy = json.loads(path.read_text(encoding="utf-8"))
    if policy.get("version") != 1:
        raise ValueError("Unsupported test-tier policy version")
    domains = set(policy["domains"])
    names = set()
    for entry in policy["heavy_methods"]:
        name = entry["method"]
        if name in names or not name.startswith("TerraRuntime.Tests.") or "*" in name:
            raise ValueError(f"Invalid/duplicate heavy method: {name}")
        names.add(name)
        if not entry["domains"] or not set(entry["domains"]) <= domains:
            raise ValueError(f"Invalid domains: {name}")
    for route in policy["routes"]:
        if not route["domains"] or not set(route["domains"]) <= domains:
            raise ValueError(f"Invalid route: {route}")
    return policy


def matches(path: str, patterns: list[str]) -> bool:
    return any(fnmatch.fnmatchcase(path, pattern) for pattern in patterns)


def select(policy: dict, methods: set[str], tier: str, paths: list[str],
           root: Path = ROOT, fallback: str | None = None) -> dict:
    heavy = {entry["method"]: entry for entry in policy["heavy_methods"]}
    missing = set(heavy) - methods
    if tier != "full" and missing:
        raise ValueError("Stale heavy-method manifest; rebuild/update it: " + ", ".join(sorted(missing)))
    plan = {"tier": tier, "mode": tier, "reason": None, "changed_paths": sorted(set(paths)),
            "domains": [], "discovered_methods": len(methods)}
    selected_domains: set[str] = set()
    if tier == "affected" and not fallback:
        for raw_path in sorted(set(paths)):
            path = raw_path.replace("\\", "/")
            if path.startswith("/") or ".." in path.split("/"):
                fallback = f"Unrecognized path: {path}"
                break
            if matches(path, policy["full_paths"]):
                fallback = f"Shared/evidence/configuration change: {path}"
                break
            if matches(path, policy["ignored_paths"]):
                continue
            if path.startswith("tests/TerraRuntime.Tests/") and path.endswith(".cs"):
                test_file = root / path
                if not test_file.is_file():
                    fallback = f"Removed test/helper: {path}"
                    break
                text = test_file.read_text(encoding="utf-8-sig")
                declared = re.findall(r"\bclass\s+(\w+)", text)
                owners = {name for name in declared if any(
                    method.startswith(f"TerraRuntime.Tests.{name}.") for method in methods)}
                if len(declared) != 1 or not owners:
                    fallback = f"Shared or unclassified test helper: {path}"
                    break
                for entry in heavy.values():
                    if entry["file"] == path or any(entry["method"].startswith(
                            f"TerraRuntime.Tests.{owner}.") for owner in owners):
                        selected_domains.update(entry["domains"])
                # New/small methods run automatically in fast; no new matrix may be silently excluded.
                continue
            route = next((route for route in policy["routes"] if matches(path, route["paths"])), None)
            if route is None:
                fallback = f"Unclassified change: {path}"
                break
            selected_domains.update(route["domains"])
    if tier == "full" or (tier == "affected" and fallback):
        plan.update(mode="full", reason=fallback or "Explicit complete suite")
        selected = methods
    elif tier == "fast":
        selected = methods - set(heavy)
    else:
        selected = {name for name, entry in heavy.items()
                    if selected_domains.intersection(entry["domains"])}
        plan["reason"] = "Affected matrices; fast is a separate mandatory gate" if selected else "No additional matrices; fast remains mandatory"
    if not methods or (plan["mode"] in ("fast", "full") and not selected):
        raise ValueError("Empty discovery/required test selection")
    plan.update(domains=sorted(selected_domains), selected_methods=sorted(selected),
                selected_method_count=len(selected), excluded_method_count=len(methods - selected))
    return plan


def changed_paths(base: str | None, root: Path = ROOT) -> tuple[list[str], str | None]:
    if not base or not base.strip("0") or base.startswith("-"):
        return [], "Missing/zero/invalid comparison base"
    resolved = subprocess.run(["git", "rev-parse", "--verify", "--quiet", "--end-of-options", base + "^{commit}"],
                              cwd=root, capture_output=True)
    if resolved.returncode:
        return [], "Comparison base is unavailable; fetch history or run full"
    sha = resolved.stdout.decode().strip()
    commands = [["git", "diff", "--name-only", "--no-renames", "-z", sha + "...HEAD"],
                ["git", "diff", "--name-only", "--no-renames", "-z", "HEAD"],
                ["git", "ls-files", "--others", "--exclude-standard", "-z"]]
    paths: set[str] = set()
    for command in commands:
        result = subprocess.run(command, cwd=root, capture_output=True)
        if result.returncode:
            return [], "Cannot establish complete changed paths"
        paths.update(p for p in result.stdout.decode("utf-8").split("\0") if p)
    return sorted(paths), None


def runner_args(dll: Path, plan: dict, policy: dict, xml: Path, threads: int) -> list[str]:
    args = ["dotnet", str(dll), "-noLogo", "-noColor", "-maxThreads", str(threads), "-xml", str(xml)]
    if plan["mode"] == "fast":
        for entry in policy["heavy_methods"]:
            args.extend(["-method-", entry["method"]])
    elif plan["mode"] == "affected":
        for method in plan["selected_methods"]:
            args.extend(["-method", method])
    # Full has no test filters, including when it is the conservative affected fallback.
    return args


def summary(xml: Path, allowed_skips: list[str] | None = None) -> dict:
    assemblies = []
    skipped = []
    stack = []
    with xml.open("rb") as stream:
        for event, element in ET.iterparse(stream, events=("start", "end")):
            if event == "start":
                stack.append(element)
                continue
            if element.tag == "assembly":
                assemblies.append({key: element.get(key) for key in ["total", "passed", "failed", "errors", "skipped", "time"]})
            if element.tag == "test" and element.get("result") == "Skip":
                skipped.append(element.get("name"))
            element.clear()
            stack.pop()
            if stack:
                stack[-1].remove(element)
    if len(assemblies) != 1:
        raise ValueError("Runner must produce exactly one complete assembly summary")
    counts = assemblies[0]
    numeric = {key: int(counts[key]) for key in ["total", "passed", "failed", "errors", "skipped"]}
    if min(numeric.values()) < 0 or numeric["passed"] + numeric["failed"] + numeric["skipped"] != numeric["total"]:
        raise ValueError("Inconsistent assembly counts")
    if len(skipped) != numeric["skipped"]:
        raise ValueError("Skipped test identities do not match summary")
    if allowed_skips is not None and not set(skipped) <= set(allowed_skips):
        raise ValueError("Unexpected skipped test identity")
    counts["skipped_tests"] = skipped
    return counts


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--tier", choices=["fast", "affected", "full"], required=True)
    parser.add_argument("--base", help="Git comparison base for affected; absent/unavailable means full")
    parser.add_argument("--changed-path", action="append", default=[], help="Explicit local change paths (also recorded in evidence)")
    parser.add_argument("--dll", type=Path, default=ROOT / "tests/TerraRuntime.Tests/bin/Release/net11.0/TerraRuntime.Tests.dll")
    parser.add_argument("--output-dir", type=Path, default=ROOT / "artifacts/test-tiers")
    parser.add_argument("--max-threads", type=int, default=4)
    parser.add_argument("--dry-run", action="store_true")
    options = parser.parse_args(argv)
    if options.max_threads < 1 or not options.dll.is_file():
        parser.error("Build Release tests first and use a positive max-threads value")
    policy = load_policy(POLICY)
    discovery = subprocess.run(["dotnet", str(options.dll.resolve()), "-noLogo", "-noColor", "-list", "methods/json"],
                               cwd=ROOT, capture_output=True, check=True)
    names = json.loads(discovery.stdout.decode("utf-8-sig"))
    if not isinstance(names, list) or any(not isinstance(name, str) for name in names):
        raise ValueError("Unexpected xUnit method discovery format")
    paths, fallback = ([], None)
    if options.tier == "affected":
        paths, fallback = (options.changed_path, None) if options.changed_path and not options.base else changed_paths(options.base)
        paths = sorted(set(paths) | set(options.changed_path))
    plan = select(policy, set(names), options.tier, paths, fallback=fallback)
    out = options.output_dir.resolve()
    out.mkdir(parents=True, exist_ok=True)
    (out / "plan.json").write_text(json.dumps(plan, indent=2) + "\n", encoding="utf-8")
    print(f"{options.tier}: mode={plan['mode']}, methods={plan['selected_method_count']}/{len(names)}; {plan['reason'] or 'audited fast exclusions'}", flush=True)
    if options.dry_run or not plan["selected_methods"]:
        result = {"executed": False, "dry_run": options.dry_run, "reason": plan["reason"]}
        (out / "result.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
        return 0
    xml = out / "tests.xml"
    xml.unlink(missing_ok=True)  # A failed runner must never reuse a previous successful report.
    args = runner_args(options.dll.resolve(), plan, policy, xml, options.max_threads)
    started = time.perf_counter()
    with (out / "tests.log").open("wb") as log:
        run = subprocess.run(args, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT)
    result = {"executed": True, "exit_code": run.returncode, "wall_seconds": time.perf_counter() - started}
    try:
        result["tests"] = summary(xml, policy["allowed_skipped_tests"])
        counts = result["tests"]
        valid = int(counts["total"]) > 0 and int(counts["failed"]) == 0 and int(counts["errors"]) == 0
        valid = valid and int(counts["skipped"]) <= policy["maximum_existing_skips"]
    except (OSError, ET.ParseError, ValueError, TypeError) as error:
        valid = False
        result["summary_error"] = str(error)
    result["accepted"] = run.returncode == 0 and valid
    (out / "result.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result), flush=True)
    return 0 if result["accepted"] else 1


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (ValueError, OSError, subprocess.SubprocessError) as error:
        print(f"Test-tier selection failed: {error}", file=sys.stderr)
        sys.exit(1)
