"""Real-host chest probes emit the independently captured context3 packet stream."""
import gzip
import json
from pathlib import Path
import struct
import sys
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parent))
from live_chest_open_probe import send_chest_transfer


class SocketCapture:
    def __init__(self):
        self.frames = []

    def sendall(self, data):
        while data:
            size = struct.unpack_from('<H', data)[0]
            self.frames.append(data[:size].hex().upper())
            data = data[size:]


class LiveChestTransferTests(unittest.TestCase):
    def test_source_withdraw_deposit_and_right_click_frames(self):
        folder = Path(__file__).resolve().parents[2] / 'tests/TerraRuntime.Tests/Fixtures'
        for platform in ['Linux', 'Windows']:
            with gzip.open(folder / f'WorldChestCursor{platform}1458.json.gz', 'rt') as source:
                rows = json.load(source)['rows']
            for row in rows:
                name = row['spec']['Name']
                if name not in ['withdraw', 'deposit', 'right-one', 'right-last']:
                    continue
                with self.subTest(platform=platform, operation=name):
                    chest = row['afterSync']['chest'][0]
                    cursor = row['afterSync']['mouse']
                    capture = SocketCapture()
                    with patch('live_chest_open_probe.time.sleep') as wait:
                        send_chest_transfer(capture, 0,
                                            [(0, chest['stack'], chest['prefix'], chest['type'])],
                                            (cursor['stack'], cursor['prefix'], cursor['type']))
                    self.assertEqual(row['operationFrames'] + row['syncFrames'], capture.frames)
                    wait.assert_called_once_with(0.5)

    def test_persistence_checks_both_legs_and_uses_observed_destination_after_restart(self):
        root = Path(__file__).resolve().parents[2]
        probe = (root / 'tools/ci/live_chest_persistence_probe.py').read_text()
        workflow = (root / '.github/workflows/live-chest-persistence.yml').read_text()
        self.assertIn('committed_items[item_slot] != (0, 0, 0)', probe)
        self.assertNotIn('--allow-empty', workflow)
        self.assertNotIn('ITEM_STACK + 1', workflow)
        self.assertEqual(2, workflow.count('--assert "$CHEST_ID" "$COMMITTED_ITEM_SLOT"'))
        self.assertEqual(1, workflow.count('--item-slot "$COMMITTED_ITEM_SLOT"'))
        self.assertIn('COMMITTED_ITEM_SLOT=$committed_slot', workflow)


if __name__ == '__main__':
    unittest.main()
