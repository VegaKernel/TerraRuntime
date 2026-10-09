"""Source-shaped pre-spawn frames; independent of the production C# encoder."""
import struct
import unittest
from unittest.mock import patch

import live_join_relay_probe as probe


def frame(packet_id, payload=b""):
    return struct.pack("<HB", len(payload) + 3, packet_id) + payload


# BannerSystem.Save: 293 Int32 kills, 293 UInt16 claims, all zero.
BANNER = bytes.fromhex("0b00002501") + bytes(293 * 4) + bytes.fromhex("2501") + bytes(293 * 2)
# NetBestiaryModule: kill netID 1/count 128, sight netID -65, chat netID 696.
BESTIARY = [bytes.fromhex(value) for value in ("04000001008001", "040001bfff", "040002b802")]

# Independent shipping socket capture: request 6 Time=7732, request 8 Time=7736.
# All other 177 payload bytes were identical (181-byte WorldInfo payload).
WORLD_INFO = bytes.fromhex(
    "341e00000103d02060091510a5018902af03865457741154657272615a5f2d5f537572766976616c"
    "02ee35374a1881384ea3179385dc4c7c4e010000003e01000008010c330602050704020400040201"
    "0102c9763e3ec8dc0600003d0e00009a1a0000030401028f0c0000ac1200008818000004050100"
    "030401020205070402040004020000000007201800240080244000000000a600a7000900a900"
    "ffffffffffff00000000000000000005aa353e0050066502"
)


class SocketBytes:
    def __init__(self, data):
        self.data = data
        self.sent = []
        self.closed = False

    def recv(self, size):
        result, self.data = self.data[:size], self.data[size:]
        return result

    def sendall(self, data):
        self.sent.append(data)

    def settimeout(self, timeout):
        pass

    def close(self):
        self.closed = True


class LiveJoinBaselineTests(unittest.TestCase):
    def read(self, frames, sections=1):
        return probe.receive_bootstrap(SocketBytes(b"".join(frame(*value) for value in frames)), sections)

    def test_full_handshake_source_banner_bestiary_then49_and129(self):
        world = b"independent-world-info"
        sock = SocketBytes(
            frame(3, b"\x00\x00") + frame(7, world) + frame(7, world)
            + frame(9, struct.pack("<i", 1)) + frame(10, b"section")
            + frame(82, BANNER) + b"".join(frame(82, value) for value in BESTIARY)
            + frame(49) + frame(129)
        )
        with patch.object(probe.socket, "create_connection", return_value=sock):
            returned, sections, count = probe.join_client("127.0.0.1", 1, 0)
        self.assertIs(sock, returned)
        self.assertEqual((1, 6), (sections, count))
        self.assertEqual([1, 6, 8, 12], [value[2] for value in sock.sent])
        self.assertFalse(sock.closed)
        self.assertEqual(b"", sock.data)
        self.assertEqual(1768, len(frame(82, BANNER)))

    def test_captured_world_info_clock_advance_preserves_complete_handshake(self):
        self.assertEqual(181, len(WORLD_INFO))
        self.assertEqual(7732, struct.unpack_from("<i", WORLD_INFO)[0])
        for clock in (7732, 7736):
            with self.subTest(clock=clock):
                repeated = struct.pack("<i", clock) + WORLD_INFO[4:]
                sock = SocketBytes(
                    frame(3, b"\x00\x00") + frame(7, WORLD_INFO) + frame(7, repeated)
                    + frame(9, struct.pack("<i", 1)) + frame(10, b"section")
                    + frame(82, BANNER) + b"".join(frame(82, value) for value in BESTIARY)
                    + frame(49) + frame(129)
                )
                with patch.object(probe.socket, "create_connection", return_value=sock):
                    try:
                        returned, sections, count = probe.join_client("127.0.0.1", 1, 0)
                    except SystemExit as error:
                        self.fail(f"valid captured WorldInfo clock transition rejected: {error}")
                self.assertIs(sock, returned)
                self.assertEqual((1, 6), (sections, count))
                self.assertEqual([1, 6, 8, 12], [value[2] for value in sock.sent])
                self.assertFalse(sock.closed)
                self.assertEqual(b"", sock.data)

    def test_world_info_nonclock_changes_wrong_id_and_shape_rejected(self):
        changed_identity = bytearray(WORLD_INFO)
        changed_identity[40] ^= 1
        for packet_id, initial, repeated in (
            (7, WORLD_INFO, bytes(changed_identity)),
            (9, WORLD_INFO, WORLD_INFO),
            (7, WORLD_INFO, WORLD_INFO[:-1]),
            (7, WORLD_INFO, WORLD_INFO + b"\x00"),
            (7, b"", b""),
            (7, b"\x00" * 3, b"\x00" * 3),
        ):
            with self.subTest(packet_id=packet_id, length=len(repeated)):
                self.assertFalse(probe.matching_world_info_repeat(packet_id, initial, repeated))
                sock = SocketBytes(frame(3, b"\x00\x00") + frame(7, initial)
                                   + frame(packet_id, repeated))
                with patch.object(probe.socket, "create_connection", return_value=sock):
                    with self.assertRaises(SystemExit) as rejected:
                        probe.join_client("127.0.0.1", 1, 0)
                self.assertTrue(sock.closed)
                self.assertIn("payload=" if len(initial) < 4 else "initial=", str(rejected.exception))

    def test_populated_baseline_at_structural_ceiling(self):
        frames = [(10, b"section")] * 63 + [(82, BANNER)]
        for kind in range(3):
            for net_id in range(-65, 697):
                payload = struct.pack("<HBh", 4, kind, net_id)
                if kind == 0:
                    payload += b"\xff\xff\xff\xff\x07"  # Int32.MaxValue
                frames.append((82, payload))
        frames.append((49, b""))
        self.assertEqual((63, 2351), self.read(frames, 63))
        self.assertEqual(2351, probe.BOOTSTRAP_FRAME_BUDGET)

    def test_missing_duplicate_or_early_baseline_and_unexpected_packets_rejected(self):
        bad_orders = [
            [(10, b""), (49, b"")],
            [(82, BANNER), (10, b"")],
            [(10, b""), (82, BESTIARY[0])],
            [(10, b""), (82, BANNER), (82, BANNER)],
            [(10, b""), (82, BANNER), (10, b"")],
            [(10, b""), (82, BANNER), (82, BESTIARY[0]), (82, BESTIARY[0])],
            [(10, b""), (82, bytes.fromhex("010000"))],
            [(10, b""), (23, b"")],
            [(10, b""), (82, BANNER), (49, b"unexpected")],
            [(10, b""), (10, b"")],
            [(10, b""), (82, b"\x0b")],
        ]
        for frames in bad_orders:
            with self.subTest(frames=frames), self.assertRaises(SystemExit):
                self.read(frames)
        for sections in (0, -1, 64, 2**31 - 1):
            with self.subTest(sections=sections), self.assertRaises(SystemExit):
                self.read([], sections)
        with self.assertRaises(SystemExit):
            self.read([(10, b""), (82, BANNER)], 2)

    def test_banner_lengths_subtype_counters_and_trailing_bytes_rejected(self):
        bad = [BANNER[:end] for end in (0, 2, 3, 4, 5, 1176, len(BANNER) - 1)]
        bad += [BANNER + b"\x00", bytes.fromhex("0b000125010000"),
                bytes.fromhex("0b0000ffff0000"), bytes.fromhex("0b000026010000"),
                bytes.fromhex("0b00000000ffff"), bytes.fromhex("0b000000002601"),
                bytes.fromhex("0b00000100ffffffff0000")]
        for payload in bad:
            with self.subTest(payload=payload[:8]), self.assertRaises(SystemExit):
                self.read([(10, b""), (82, payload)])

    def test_bestiary_unknown_identity_kind_count_and_trailing_bytes_rejected(self):
        bad = [bytes.fromhex(value) for value in (
            "0400030100", "040001beff", "040001b902", "0400000100",
            "040000010080", "04000001008000", "0400000100ffffffff08",
            "04000001008080808080", "04000001000100", "040001010000",
        )]
        bad += [BESTIARY[0][:end] for end in range(5)]
        for payload in bad:
            with self.subTest(payload=payload), self.assertRaises(SystemExit):
                self.read([(10, b""), (82, BANNER), (82, payload)])

    def test_handshake_failure_closes_socket(self):
        world = b"world"
        sock = SocketBytes(frame(3, b"\x00\x00") + frame(7, world) + frame(7, world)
                           + frame(9, struct.pack("<i", 1)) + frame(10) + frame(49))
        with patch.object(probe.socket, "create_connection", return_value=sock), self.assertRaises(SystemExit):
            probe.join_client("127.0.0.1", 1, 0)
        self.assertTrue(sock.closed)

    def test_bootstrap_deadline_and_frame_ceiling_remain_bounded(self):
        with patch.object(probe.time, "monotonic", side_effect=[0, 16]), self.assertRaises(SystemExit):
            self.read([])
        with patch.object(probe, "BOOTSTRAP_FRAME_BUDGET", 1), self.assertRaises(SystemExit):
            self.read([(10, b""), (82, BANNER), (49, b"")])


if __name__ == "__main__":
    unittest.main()
