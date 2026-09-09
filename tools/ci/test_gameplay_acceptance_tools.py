"""Regression contracts for the executable gameplay/source acceptance helpers."""

import struct
import re
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

from check_moon_lord_death_source import require_death_velocity
from live_dirt_kill_probe import send_selected_movement, select_item, complete_pickup, COPPER_PICKAXE_ITEM
from probe_worldgen_dungeon_graph import read_source, require_runtime_graph, require_runtime_features


class MoonLordDecompilationTests(unittest.TestCase):
    positional = "velocity = Vector2.Lerp(velocity, new Vector2(0f, -0.5f), 0.98f);"
    named = "velocity = Vector2.Lerp(value2: new Vector2(0f, -0.5f), value1: velocity, amount: 0.98f);"
    lowered = ("Vector2 target = default(Vector2); "
               "((Vector2)(ref target))..ctor(0f, -0.5f); "
               "velocity = Vector2.Lerp(velocity, target, 0.98f);")

    def test_resolved_and_missing_xna_representations(self):
        for expression in (self.positional, self.named, self.lowered):
            with self.subTest(expression=expression):
                require_death_velocity(expression)

    def test_wrong_target_speed_or_amount_rejected_in_every_representation(self):
        for expression in (self.positional, self.named, self.lowered):
            for old, new in (("-0.5f", "0.5f"), ("0.98f", "0.02f"), ("0f", "1f")):
                with self.subTest(expression=expression, mutation=(old, new)):
                    with self.assertRaises(SystemExit):
                        require_death_velocity(expression.replace(old, new))

    def test_swapped_operands_and_unrelated_or_overwritten_temporary_rejected(self):
        for expression in (
            self.positional.replace("velocity, new Vector2(0f, -0.5f)", "new Vector2(0f, -0.5f), velocity"),
            self.lowered.replace("velocity, target", "target, velocity"),
            self.lowered.replace("ref target", "ref unrelated"),
            self.lowered.replace("velocity =", "target = other; velocity ="),
        ):
            with self.subTest(expression=expression), self.assertRaises(SystemExit):
                require_death_velocity(expression)


class LiveMiningProbeTests(unittest.TestCase):
    def test_compact_pickup_sends_official_literal_and_checks_both_observers(self):
        origin, peer = Mock(), Mock()
        with patch("live_dirt_kill_probe.recv_until_packet", side_effect=[
            (bytes.fromhex("110000"), []), (bytes.fromhex("1100"), []), (bytes.fromhex("1100"), [])
        ]) as receive:
            complete_pickup(origin, peer, 17)
        origin.sendall.assert_called_once_with(bytes.fromhex("0500971100"))
        self.assertEqual([call.args[1] for call in receive.call_args_list], [22, 151, 151])

    def test_pickup_rejects_wrong_reservation_or_wrong_removed_slot(self):
        for responses in (
            [(bytes.fromhex("110001"), [])],
            [(bytes.fromhex("110000"), []), (bytes.fromhex("1200"), [])],
        ):
            with patch("live_dirt_kill_probe.recv_until_packet", side_effect=responses):
                with self.assertRaises(SystemExit):
                    complete_pickup(Mock(), Mock(), 17)

    def test_movement_literal_shape_and_admissible_position(self):
        client = Mock()
        send_selected_movement(client, 2, 1, 45, 46)
        # Packet 13: claimed slot, controlUseItem, normal gravity, two misc bytes,
        # selected hotbar slot, then X/Y little-endian floats. No codec-under-test.
        expected = bytes.fromhex("11000d0220100000010000344400003844")
        client.sendall.assert_called_once_with(expected)

    def test_tool_switch_uses_distinct_hotbar_slot(self):
        origin, peer = Mock(), Mock()
        inventory = bytes.fromhex("000100010000b50d00")  # slot1, Copper Pickaxe3509
        movement = bytes.fromhex("0020100000010000344400003444")
        with patch("live_dirt_kill_probe.recv_until_packet", side_effect=[(inventory, []), (movement, [])]):
            select_item(origin, peer, 0, COPPER_PICKAXE_ITEM, 45, 45)
        frames = [call.args[0] for call in origin.sendall.call_args_list]
        self.assertEqual(struct.unpack_from("<h", frames[0], 4)[0], 1)
        self.assertEqual(frames[1][8], 1)

    def test_uncommitted_position_rejected(self):
        inventory = bytes.fromhex("000100010000b50d00")
        stale_movement = bytes.fromhex("0020100000010000204300002043")  # old160/160
        with patch("live_dirt_kill_probe.recv_until_packet", side_effect=[(inventory, []), (stale_movement, [])]):
            with self.assertRaises(SystemExit):
                select_item(Mock(), Mock(), 0, COPPER_PICKAXE_ITEM, 45, 45)


class WorkflowOwnershipTests(unittest.TestCase):
    def test_generated_world_acceptance_selects_existing_tests_and_source_paths(self):
        root = Path(__file__).resolve().parents[2]
        workflow = (root / ".github/workflows/terraria-vanilla-generated-world-acceptance.yml").read_text(encoding="utf-8")
        names = re.findall(r"-class TerraRuntime\.Tests\.(\w+)", workflow)
        self.assertIn("VanillaWorldGenerationFullIntegrationTests", names)
        self.assertIn("UnderworldTerrain1458Tests", names)
        for name in names:
            with self.subTest(test=name):
                self.assertTrue((root / "tests/TerraRuntime.Tests" / f"{name}.cs").is_file())
        for path in re.findall(r"- '(src/[^']+)'", workflow):
            with self.subTest(path=path):
                self.assertTrue((root / path.removesuffix("/**")).exists())

    def test_dungeon_workflow_points_at_existing_runtime_sources(self):
        root = Path(__file__).resolve().parents[2]
        workflow = (root / ".github/workflows/terraria-worldgen-dungeon-graph.yml").read_text(encoding="utf-8")
        paths = re.findall(r"src/[^\s']+\.cs\b", workflow)
        self.assertGreaterEqual(len(paths), 5)
        for relative in paths:
            with self.subTest(path=relative):
                self.assertTrue(any(path.is_file() for path in root.glob(relative)))
        for relative in re.findall(r"tests/[^\s']+\.cs\b", workflow):
            self.assertTrue(any(path.is_file() for path in root.glob(relative)))
        self.assertIn("-class '*Dungeon*'", workflow)

    def test_dungeon_contract_checks_current_owners_and_rejects_missing_handoffs(self):
        root = Path(__file__).resolve().parents[2] / "src/TerraRuntime.WorldGeneration/Generation/Vanilla"
        graph = (root / "DungeonGraphGenerator1458.cs").read_text(encoding="utf-8-sig")
        room = (root / "DungeonLegacyRoom1458.cs").read_text(encoding="utf-8-sig")
        hall = (root / "DungeonLegacyHall1458.cs").read_text(encoding="utf-8-sig")
        require_runtime_graph(graph, room, hall)
        for marker in (
            "var components = GenerateLayout(renderer, sharedRandom",
            "int roomRoll = random.Next(DungeonGenerationCatalog1458.RoomChance)",
            "renderer.RenderRoom(cursor, random.Next(), startingRoom: true)",
            "renderer.RenderHall(cursor, lastHall, random.Next())",
            "location.X - 10 + random.Next(20), location.Y + 30",
            "component.InnerBounds ?? throw",
            "DungeonEntranceKind1458.Dome => builder.GenerateDome(anchor, seed, leftDungeon)",
            "DungeonEntranceKind1458.Tower => builder.GenerateTower(anchor, seed, leftDungeon)",
            "entrance.BuildingPlatforms",
            "_ = sharedRandom.Next();",
        ):
            with self.subTest(marker=marker), self.assertRaises(SystemExit):
                require_runtime_graph(graph.replace(marker, "invalid"), room, hall)
        for broken_room, broken_hall in ((room.replace("new DungeonUnifiedRandom1458(seed)", "shared"), hall),
                                         (room, hall.replace("new DungeonUnifiedRandom1458(seed)", "shared"))):
            with self.assertRaises(SystemExit):
                require_runtime_graph(graph, broken_room, broken_hall)

    def test_dungeon_features_keep_order_and_source_sampling_area(self):
        path = Path(__file__).resolve().parents[2] / "src/TerraRuntime.WorldGeneration/Generation/Vanilla/DungeonFeaturePipeline1458.cs"
        source = path.read_text(encoding="utf-8-sig")
        require_runtime_features(source)
        for marker in ("DungeonFeatureCandidates1458.Collect", "padding: 0", "new DungeonSpikes1458",
                       "new DungeonDoors1458", "new DungeonWallVariants1458", "new DungeonPlatforms1458",
                       "new DungeonPitTraps1458", "chests.PlaceBiome", "new DungeonBookshelves1458",
                       "chests.PlaceBasic", "new DungeonLights1458", "new DungeonTraps1458",
                       "new DungeonFurniture1458", "new DungeonPaintings1458", "new DungeonBanners1458",
                       "new DungeonLateDoors1458", "random, cancellationToken, pits"):
            with self.subTest(marker=marker), self.assertRaises(SystemExit):
                require_runtime_features(source.replace(marker, "invalid"))
        with self.assertRaises(SystemExit):
            require_runtime_features(source.replace("new DungeonSpikes1458", "new SWAP")
                                     .replace("new DungeonDoors1458", "new DungeonSpikes1458")
                                     .replace("new SWAP", "new DungeonDoors1458"))

    def test_decompiler_redirection_encodings(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "reference.cs"
            for encoding in ("utf-8", "utf-8-sig", "utf-16"):
                source.write_text("named source contract", encoding=encoding)
                self.assertEqual(read_source(str(source)), "named source contract")


if __name__ == "__main__":
    unittest.main()
