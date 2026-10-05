import copy
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET

import run_test_tiers as tiers


class TestTierSelection(unittest.TestCase):
    def setUp(self):
        self.methods = {"TerraRuntime.Tests.SmallTests.Guard", "TerraRuntime.Tests.NpcTests.Matrix",
                        "TerraRuntime.Tests.WorldTests.Matrix"}
        self.policy = {"version": 1, "domains": ["npc", "world"], "maximum_existing_skips": 1,
                       "full_paths": ["tests/TerraRuntime.Tests/Fixtures/**", "src/Shared/**"],
                       "ignored_paths": ["docs/**"],
                       "routes": [{"paths": ["src/Npc/**"], "domains": ["npc"]},
                                  {"paths": ["src/World/**"], "domains": ["world"]}],
                       "heavy_methods": [
                           {"method": "TerraRuntime.Tests.NpcTests.Matrix", "file": "tests/TerraRuntime.Tests/NpcTests.cs", "domains": ["npc"]},
                           {"method": "TerraRuntime.Tests.WorldTests.Matrix", "file": "tests/TerraRuntime.Tests/WorldTests.cs", "domains": ["world"]}]}

    def plan(self, tier, paths=(), **kwargs):
        return tiers.select(self.policy, self.methods, tier, list(paths), **kwargs)

    def test_fast_and_all_domain_matrices_exactly_partition_full(self):
        fast = self.plan("fast")
        affected = self.plan("affected", ["src/Npc/a.cs", "src/World/a.cs"])
        full = self.plan("full")
        self.assertFalse(set(fast["selected_methods"]) & set(affected["selected_methods"]))
        self.assertEqual(set(full["selected_methods"]), set(fast["selected_methods"]) | set(affected["selected_methods"]))

    def test_only_affected_matrix_selected(self):
        self.assertEqual(["TerraRuntime.Tests.NpcTests.Matrix"], self.plan("affected", ["src/Npc/a.cs"])["selected_methods"])

    def test_unknown_path_requires_unfiltered_full(self):
        plan = self.plan("affected", ["src/NewSubsystem/a.cs"])
        self.assertEqual("full", plan["mode"])
        self.assertEqual(self.methods, set(plan["selected_methods"]))

    def test_shared_changes_and_fixtures_require_full(self):
        for path in ["src/Shared/random.cs", "tests/TerraRuntime.Tests/Fixtures/new.json.gz"]:
            with self.subTest(path=path):
                self.assertEqual("full", self.plan("affected", [path])["mode"])

    def test_route_order_keeps_specific_scope(self):
        self.policy["routes"].append({"paths": ["src/**"], "domains": ["world"]})
        self.assertEqual(["npc"], self.plan("affected", ["src/Npc/a.cs"])["domains"])

    def test_docs_only_has_no_additional_matrix_but_keeps_fast_nonempty(self):
        self.assertEqual([], self.plan("affected", ["docs/en/testing.md"])["selected_methods"])
        self.assertEqual(["TerraRuntime.Tests.SmallTests.Guard"], self.plan("fast")["selected_methods"])

    def test_new_methods_automatically_stay_in_fast(self):
        self.methods.add("TerraRuntime.Tests.NewTests.NewGolden")
        self.assertIn("TerraRuntime.Tests.NewTests.NewGolden", self.plan("fast")["selected_methods"])

    def test_renamed_heavy_method_cannot_silently_disappear(self):
        self.methods.remove("TerraRuntime.Tests.NpcTests.Matrix")
        with self.assertRaisesRegex(ValueError, "Stale heavy-method"):
            self.plan("fast")
        self.assertEqual("full", self.plan("full")["mode"])

    def test_empty_required_discovery_is_failure(self):
        with self.assertRaises(ValueError):
            tiers.select(self.policy, set(), "full", [])

    def test_missing_base_is_a_full_fallback(self):
        for base in [None, "", "000000", "--help"]:
            paths, reason = tiers.changed_paths(base)
            self.assertEqual([], paths)
            self.assertIsNotNone(reason)
            self.assertEqual("full", self.plan("affected", fallback=reason)["mode"])

    def test_test_helpers_and_removed_tests_cannot_select_nothing(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            file = root / "tests/TerraRuntime.Tests/NpcTests.cs"
            file.parent.mkdir(parents=True)
            file.write_text("class NpcTests {} class SharedFixture {}")
            self.assertEqual("full", self.plan("affected", [file.relative_to(root).as_posix()], root=root)["mode"])
            file.unlink()
            self.assertEqual("full", self.plan("affected", [file.relative_to(root).as_posix()], root=root)["mode"])

    def test_edited_test_selects_its_cross_domain_matrices(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            file = root / "tests/TerraRuntime.Tests/NpcTests.cs"
            file.parent.mkdir(parents=True)
            file.write_text("namespace TerraRuntime.Tests; class NpcTests {}")
            self.policy["heavy_methods"][0]["domains"].append("world")
            plan = self.plan("affected", [file.relative_to(root).as_posix()], root=root)
            self.assertEqual(["npc", "world"], plan["domains"])
            self.assertEqual(2, plan["selected_method_count"])

    def test_full_args_never_contain_filters_even_on_fallback(self):
        for plan in [self.plan("full"), self.plan("affected", ["unknown.file"])]:
            args = tiers.runner_args(Path("test.dll"), plan, self.policy, Path("out.xml"), 4)
            self.assertFalse(any(arg.startswith("-method") or arg.startswith("-class") for arg in args))

    def test_moved_heavy_test_keeps_method_owner_domains(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            file = root / "tests/TerraRuntime.Tests/Relocated.cs"
            file.parent.mkdir(parents=True)
            file.write_text("namespace TerraRuntime.Tests; class NpcTests {}")
            plan = self.plan("affected", [file.relative_to(root).as_posix()], root=root)
            self.assertIn("TerraRuntime.Tests.NpcTests.Matrix", plan["selected_methods"])

    def test_fast_uses_exact_exclusions_affected_uses_exact_inclusions(self):
        fast = tiers.runner_args(Path("test.dll"), self.plan("fast"), self.policy, Path("out.xml"), 4)
        affected = tiers.runner_args(Path("test.dll"), self.plan("affected", ["src/Npc/a.cs"]), self.policy, Path("out.xml"), 4)
        self.assertEqual(2, fast.count("-method-"))
        self.assertEqual(1, affected.count("-method"))
        self.assertNotIn("-method-", affected)

    def test_summary_reads_counts_and_rejects_missing_assembly(self):
        with tempfile.TemporaryDirectory() as directory:
            xml = Path(directory) / "tests.xml"
            xml.write_text('<assemblies><assembly total="4" passed="3" failed="0" errors="0" skipped="1" time="1.2"><collection><test name="KnownSkip" result="Skip" /></collection></assembly></assemblies>')
            self.assertEqual("4", tiers.summary(xml)["total"])
            self.assertEqual(["KnownSkip"], tiers.summary(xml)["skipped_tests"])
            self.assertEqual("4", tiers.summary(xml, ["KnownSkip"])["total"])
            with self.assertRaisesRegex(ValueError, "Unexpected skipped"):
                tiers.summary(xml, ["OtherSkip"])
            xml.write_text("<assemblies />")
            with self.assertRaises(ValueError):
                tiers.summary(xml)

    def test_truncated_or_inconsistent_xml_cannot_be_accepted(self):
        with tempfile.TemporaryDirectory() as directory:
            xml = Path(directory) / "tests.xml"
            for body in [
                '<assemblies><assembly total="1" passed="1" failed="0" errors="0" skipped="0" time="1">',
                '<assemblies><assembly total="2" passed="1" failed="0" errors="0" skipped="0" time="1" /></assemblies>',
                '<assemblies><assembly total="1" passed="0" failed="0" errors="0" skipped="1" time="1" /></assemblies>'
            ]:
                xml.write_text(body)
                with self.assertRaises((ValueError, ET.ParseError)):
                    tiers.summary(xml)

    def test_policy_rejects_duplicate_and_unclassified_heavy_methods(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "policy.json"
            for mutate in [lambda p: p["heavy_methods"].append(copy.deepcopy(p["heavy_methods"][0])),
                           lambda p: p["heavy_methods"][0].update(domains=[]),
                           lambda p: p["heavy_methods"][0].update(domains=["unknown"])]:
                policy = copy.deepcopy(self.policy)
                mutate(policy)
                path.write_text(json.dumps(policy))
                with self.assertRaises(ValueError):
                    tiers.load_policy(path)

    def test_git_diff_includes_both_sides_of_renames_and_worktree(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            def git(*args):
                return subprocess.check_output(["git", *args], cwd=root).decode().strip()
            git("init", "--quiet")
            git("config", "user.email", "tests@example.invalid")
            git("config", "user.name", "Tier tests")
            (root / "old.cs").write_text("unchanged body")
            git("add", "."); git("commit", "-qm", "base")
            base = git("rev-parse", "HEAD")
            git("mv", "old.cs", "new.cs"); git("commit", "-qm", "rename")
            (root / "new.cs").write_text("worktree change")
            (root / "untracked.cs").write_text("new")
            paths, reason = tiers.changed_paths(base, root)
            self.assertIsNone(reason)
            self.assertEqual(["new.cs", "old.cs", "untracked.cs"], paths)

    def test_real_manifest_methods_have_source_owners_and_full_domains(self):
        policy = tiers.load_policy(tiers.POLICY)
        self.assertEqual(166, len(policy["heavy_methods"]))
        for entry in policy["heavy_methods"]:
            self.assertTrue((tiers.ROOT / entry["file"]).is_file(), entry["file"])
            self.assertTrue(entry["domains"])


if __name__ == "__main__":
    unittest.main()
