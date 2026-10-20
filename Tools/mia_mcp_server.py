"""Local MCP tools for an opt-in Made in Arizona development player.

Start the game with -miaAutomationBridge -miaBridgeDir=<absolute path>, then set
MIA_BRIDGE_DIR to that same path before running this server over stdio.
"""

import json
import os
from pathlib import Path
import time
from uuid import uuid4

from hotfix_playtest import start_hotfix_job, hotfix_job_status


def request(command: str, *, bridge_dir: str | None = None, timeout: float = 8.0, **fields):
    root = Path(bridge_dir or os.environ.get("MIA_BRIDGE_DIR", ""))
    if not str(root) or str(root) == ".":
        raise RuntimeError("Set MIA_BRIDGE_DIR to the game's -miaBridgeDir path")
    session = root / "session.json"
    if not session.is_file():
        raise RuntimeError(f"No game bridge at {root}; launch a development build with -miaAutomationBridge")
    heartbeat = json.loads(session.read_text(encoding="utf-8"))
    now_dotnet_ticks = 621355968000000000 + time.time_ns() // 100
    if abs(now_dotnet_ticks - int(heartbeat.get("utcTicks", 0))) > 50_000_000:
        raise RuntimeError(f"Game bridge is stale: {root}")
    request_id = uuid4().hex
    incoming = root / "requests" / f"{request_id}.json"
    outgoing = root / "responses" / f"{request_id}.json"
    incoming.parent.mkdir(parents=True, exist_ok=True)
    payload = {"id": request_id, "command": command, **fields}
    temporary = incoming.with_suffix(".json.tmp")
    temporary.write_text(json.dumps(payload), encoding="utf-8")
    temporary.replace(incoming)
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if outgoing.is_file():
            response = json.loads(outgoing.read_text(encoding="utf-8"))
            outgoing.unlink()
            if response.get("id") != request_id:
                raise RuntimeError("Game bridge response ID mismatch")
            return response
        time.sleep(0.05)
    incoming.unlink(missing_ok=True)
    raise TimeoutError(f"Game did not answer {command!r} within {timeout:g} seconds")


def main():
    from mcp.server import MCPServer

    server = MCPServer("Made in Arizona live test")

    @server.tool()
    def game_status() -> dict:
        """Read game state, co-op room code and connection, update progress, ping, and trace location."""
        return request("status")

    @server.tool()
    def host_coop_game() -> dict:
        """Create a Relay room from the menu or garage. Poll game_status for the room code."""
        return request("host_coop")

    @server.tool()
    def join_coop_game(room_code: str) -> dict:
        """Join a friend's Relay room from the menu or garage. Poll game_status for connection."""
        return request("join_coop", roomCode=room_code)

    @server.tool()
    def check_game_updates() -> dict:
        """Refresh the published build list; poll game_status for the result."""
        return request("check_updates")

    @server.tool()
    def update_game_to_latest() -> dict:
        """Check, download, verify, install, and reopen the newest build from the menu while offline."""
        return request("update_game")

    @server.tool()
    def leave_coop_game() -> dict:
        """Leave the current room; the host returns to the garage for an update."""
        return request("leave_coop")

    @server.tool()
    def set_coop_ready(ready: bool = True, guest_slot: int = 0) -> dict:
        """Set ready in the co-op garage on this player or an opted-in guest; all ready players launch the sortie."""
        command = "ready" if ready else "unready"
        return request(("guest_" if guest_slot else "") + command,
                       timeout=15 if guest_slot else 8, guestSlot=guest_slot)

    @server.tool()
    def start_code_hotfix() -> dict:
        """Build Windows and macOS from the clean, pushed commit and publish a verified update in the background."""
        return start_hotfix_job()

    @server.tool()
    def code_hotfix_status(job_id: str) -> dict:
        """Read phase, result, release URL, and the last build-log lines for a code hotfix job."""
        return hotfix_job_status(job_id)

    @server.tool()
    def list_runtime_tuning() -> dict:
        """List validated slider fields that can change immediately in the running player."""
        return request("list_tuning")

    @server.tool()
    def get_runtime_tuning(field: str, guest_slot: int = 0) -> dict:
        """Read one live tuning field locally, or from a connected opted-in guest slot 2–4."""
        return request("guest_get_tuning" if guest_slot else "get_tuning",
                       timeout=15 if guest_slot else 8, field=field, guestSlot=guest_slot)

    @server.tool()
    def set_runtime_tuning(field: str, value: float, guest_slot: int = 0) -> dict:
        """Apply a range-checked live tuning value locally or on an opted-in guest. Shared fields belong to the host."""
        return request("guest_set_tuning" if guest_slot else "set_tuning",
                       timeout=15 if guest_slot else 8, field=field, value=value, guestSlot=guest_slot)

    @server.tool()
    def inspect_player_peers(guest_slot: int = 0) -> dict:
        """Read car positions and input/snapshot counters as seen by this player or a connected guest."""
        result = request("guest_peers" if guest_slot else "peers", timeout=15 if guest_slot else 8,
                         guestSlot=guest_slot)
        if result.get("peerSnapshot"):
            result["peerSnapshot"] = json.loads(result["peerSnapshot"])
        return result

    @server.tool()
    def recent_player_errors(guest_slot: int = 0) -> dict:
        """Read the newest Unity errors from this player or a connected opted-in guest."""
        return request("guest_errors" if guest_slot else "errors", timeout=15 if guest_slot else 8,
                       guestSlot=guest_slot)

    @server.tool()
    def guest_player_status(guest_slot: int) -> dict:
        """Ask a connected opted-in guest for its own live state, ping, build, position, and peer view."""
        result = request("guest_status", timeout=15, guestSlot=guest_slot)
        if result.get("peerSnapshot"):
            result["peerSnapshot"] = json.loads(result["peerSnapshot"])
        return result

    @server.tool()
    def drive_guest_car(guest_slot: int, x: float, y: float, seconds: float = 1.0,
                        boost: bool = False, drift: bool = False) -> dict:
        """Ask an opted-in guest to drive its own car for at most five seconds."""
        return request("guest_drive", timeout=15, guestSlot=guest_slot,
                       moveX=x, moveY=y, seconds=seconds, boost=boost, drift=drift)

    @server.tool()
    def stop_guest_car(guest_slot: int) -> dict:
        """Stop automated driving on an opted-in guest immediately."""
        return request("guest_stop", timeout=15, guestSlot=guest_slot)

    @server.tool()
    def update_guest_game(guest_slot: int) -> dict:
        """Ask an opted-in guest to leave co-op, install the newest published build, and restart."""
        return request("guest_update_game", timeout=15, guestSlot=guest_slot)

    @server.tool()
    def run_coop_benchmark() -> dict:
        """Start a 12-second benchmark and collect traces from every connected player."""
        return request("coopbench")

    @server.tool()
    def capture_coop_trace() -> dict:
        """Save the preceding co-op trace from host and all connected guests."""
        return request("cooplog")

    @server.tool()
    def drive_local_car(x: float, y: float, seconds: float = 1.0,
                        boost: bool = False, drift: bool = False) -> dict:
        """Drive only this machine's car. World X/Z input is clamped; duration is at most 5 seconds."""
        return request("drive", moveX=x, moveY=y, seconds=seconds, boost=boost, drift=drift)

    @server.tool()
    def stop_local_car() -> dict:
        """End an active automation drive immediately."""
        return request("stop")

    @server.tool()
    def capture_game_screen() -> dict:
        """Queue a screenshot in the local bridge directory and return its path."""
        return request("screenshot")

    server.run()


if __name__ == "__main__":
    main()
