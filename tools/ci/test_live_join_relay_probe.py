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
