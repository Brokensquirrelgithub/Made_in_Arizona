import json
from pathlib import Path
import tempfile
import threading
import time
import unittest

from mia_mcp_server import request


class BridgeClientTest(unittest.TestCase):
    def test_round_trip(self):
        with tempfile.TemporaryDirectory(dir=Path(__file__).parent) as temp:
            root = Path(temp)
            (root / "session.json").write_text(json.dumps({"utcTicks": 621355968000000000 + time.time_ns() // 100}), encoding="utf-8")
            (root / "requests").mkdir()
            (root / "responses").mkdir()

            def fake_game():
                deadline = time.monotonic() + 2
                while time.monotonic() < deadline:
                    incoming = list((root / "requests").glob("*.json"))
                    if incoming:
                        data = json.loads(incoming[0].read_text(encoding="utf-8"))
                        (root / "responses" / incoming[0].name).write_text(
                            json.dumps({"id": data["id"], "ok": True, "message": data["command"]}), encoding="utf-8")
                        return
                    time.sleep(.01)

            worker = threading.Thread(target=fake_game)
            worker.start()
            result = request("status", bridge_dir=temp, timeout=2)
            worker.join()
            self.assertEqual(result["message"], "status")

    def test_rejects_stale_bridge(self):
        with tempfile.TemporaryDirectory(dir=Path(__file__).parent) as temp:
            (Path(temp) / "session.json").write_text('{"utcTicks":1}', encoding="utf-8")
            with self.assertRaisesRegex(RuntimeError, "stale"):
                request("status", bridge_dir=temp)


if __name__ == "__main__":
    unittest.main()
