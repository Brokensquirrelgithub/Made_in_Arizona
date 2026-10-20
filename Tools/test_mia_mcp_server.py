import json
from pathlib import Path
import tempfile
import threading
import time
import unittest
import zipfile

from mia_mcp_server import request
from hotfix_playtest import _zip_tree, hotfix_job_status


class BridgeClientTest(unittest.TestCase):
    def test_hotfix_zip_preserves_executable_mode(self):
        with tempfile.TemporaryDirectory(dir=Path(__file__).parent) as temp:
            root = Path(temp)
            app = root / "Made in Arizona.app"
            app.mkdir()
            binary = app / "Contents" / "MacOS" / "Arizona"
            binary.parent.mkdir(parents=True)
            binary.write_bytes(b"player")
            binary.chmod(0o755)
            archive = root / "mac.zip"
            _zip_tree(app, archive, "Made in Arizona.app/")
            with zipfile.ZipFile(archive) as package:
                member = package.getinfo("Made in Arizona.app/Contents/MacOS/Arizona")
                self.assertEqual(package.read(member), b"player")
                self.assertEqual((member.external_attr >> 16) & 0o777, 0o755)

    def test_hotfix_rejects_invalid_job_id(self):
        self.assertFalse(hotfix_job_status("../../other")["ok"])

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

    def test_join_code_is_sent_to_game(self):
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
                            json.dumps({"id": data["id"], "ok": data["command"] == "join_coop", "roomCode": data.get("roomCode")}), encoding="utf-8")
                        return
                    time.sleep(.01)

            worker = threading.Thread(target=fake_game)
            worker.start()
            result = request("join_coop", bridge_dir=temp, timeout=2, roomCode="ABCD12")
            worker.join()
            self.assertTrue(result["ok"])
            self.assertEqual(result["roomCode"], "ABCD12")


if __name__ == "__main__":
    unittest.main()
