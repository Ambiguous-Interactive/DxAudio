#!/usr/bin/env python3
"""WebGL drive tests: report judgement, staging, CDP framing, and a fake browser."""

from __future__ import annotations

import base64
import hashlib
import json
import os
import socket
import sys
import tempfile
import threading
import unittest
from pathlib import Path
from typing import TYPE_CHECKING
from unittest.mock import patch

if TYPE_CHECKING:
    from collections.abc import Callable

TESTS_DIR = Path(__file__).resolve().parent
REPO_ROOT = TESTS_DIR.parent.parent
sys.path.insert(0, str(REPO_ROOT / "scripts" / "unity"))
sys.dont_write_bytecode = True

import webgl_drive  # noqa: E402 - the repository module is imported after its path is added.

INDEX_HTML = (
    '<!DOCTYPE html><html><head><meta charset="utf-8">'
    "<title>Unity WebGL</title></head><body>"
    '<script src="Build/smoke.loader.js"></script></body></html>'
)


def _expect_drive_failure(body: Callable[[], object]) -> str:
    try:
        body()
    except webgl_drive.DriveFailure as error:
        return str(error)
    raise AssertionError("expected DriveFailure")


def _event(kind: str, rms: float, when: float = 0.5, duration: float = 0.4) -> dict[str, object]:
    return {"kind": kind, "when": when, "rms": rms, "duration": duration, "channels": 2}


def _report(**overrides: object) -> dict[str, object]:
    report: dict[str, object] = {
        "started": 4,
        "underruns": 0,
        "rms": 0.3545,
        "events": [
            _event("start", 0.3530, 0.5),
            _event("start", 0.3545, 0.9),
            _event("start", 0.1760, 1.7),
            _event("start", 0.1780, 1.8),
            {"kind": "buffer", "rms": 0.3540, "duration": 0.8, "channels": 2},
        ],
        "played": {"dry": True, "shaped": True},
        "done": True,
        "errors": [],
    }
    report.update(overrides)
    return report


class StageServeCopyTests(unittest.TestCase):
    def setUp(self) -> None:
        directory = tempfile.TemporaryDirectory(prefix="dxaudio webgl drive ")
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        self.build = self.root / "build"
        (self.build / "Build").mkdir(parents=True)
        (self.build / "index.html").write_text(INDEX_HTML, encoding="utf-8")
        (self.build / "Build" / "smoke.wasm").write_bytes(b"wasm")
        (self.build / "Build" / "smoke.data").write_bytes(b"data")
        self.serve = self.root / "serve"
        self.shim = REPO_ROOT / "scripts" / "unity" / "webgl_shim.js"

    def test_injects_the_shim_before_the_loader(self) -> None:
        index = webgl_drive.stage_serve_copy(self.build, self.serve, self.shim)
        text = index.read_text(encoding="utf-8")
        assert text.index('src="shim.js"') < text.index("smoke.loader.js")
        assert (self.serve / "shim.js").is_file()
        assert 'src="shim.js"' not in (self.build / "index.html").read_text(encoding="utf-8")
        assert list(self.serve.rglob("*.wasm")), "the serve copy keeps the build files"

    def test_requires_an_index(self) -> None:
        message = _expect_drive_failure(
            lambda: webgl_drive.stage_serve_copy(self.root / "missing", self.serve, self.shim)
        )
        assert "no index.html" in message

    def test_rejects_a_headless_document(self) -> None:
        (self.build / "index.html").write_text("<html><body></body></html>", encoding="utf-8")
        message = _expect_drive_failure(
            lambda: webgl_drive.stage_serve_copy(self.build, self.serve, self.shim)
        )
        assert "no <head> tag" in message


class MeasureInertnessTests(unittest.TestCase):
    def _events(self, *triples: tuple[float, float, float]) -> list[webgl_drive.AudioEvent]:
        return [
            webgl_drive.AudioEvent(kind="start", rms=rms, duration=duration, when=when)
            for rms, duration, when in triples
        ]

    def test_splits_the_levels_and_reports_one_half(self) -> None:
        events = self._events(
            (0.3530, 0.4, 0.5),
            (0.3545, 0.4, 0.9),
            (0.1760, 0.4, 1.7),
            (0.1780, 0.4, 1.8),
        )
        measurement = webgl_drive.measure_inertness(events)
        assert measurement is not None
        assert abs(measurement.ratio - 0.5) <= 0.02
        assert measurement.dry_events == 2
        assert measurement.shaped_events == 2

    def test_labels_follow_the_time_gap_not_the_first_level(self) -> None:
        # A short fade-in chunk at the head of the dry cue sits below the
        # shaped level; the time-gap split must keep it in the dry window.
        events = self._events(
            (0.1000, 0.02, 0.5),
            (0.3530, 0.4, 0.52),
            (0.1760, 0.4, 1.7),
            (0.1780, 0.4, 1.72),
        )
        measurement = webgl_drive.measure_inertness(events)
        assert measurement is not None
        assert abs(measurement.ratio - 0.5) <= 0.05
        assert measurement.dry_events == 2

    def test_rejects_one_level_only(self) -> None:
        events = self._events((0.35, 0.4, 0.5), (0.36, 0.4, 0.9))
        assert webgl_drive.measure_inertness(events) is None

    def test_rejects_below_floor_or_without_duration(self) -> None:
        events = self._events((0.35, 0.4, 0.5), (0.01, 0.4, 0.9), (0.18, 0.0, 1.4))
        assert webgl_drive.measure_inertness(events) is None


class EvaluateReportTests(unittest.TestCase):
    def _findings(self, report: dict[str, object]) -> list[str]:
        return [finding.severity for finding in webgl_drive.evaluate_report(report)]

    def _messages(self, report: dict[str, object]) -> list[str]:
        return [finding.message for finding in webgl_drive.evaluate_report(report)]

    def test_passing_report_records_the_inert_finding(self) -> None:
        assert "fail" not in self._findings(_report())
        notes = self._messages(_report())
        assert any("inert on Web" in message for message in notes)

    def test_missing_done_and_played_flags_fail(self) -> None:
        assert "fail" in self._findings(_report(done=False))
        shaped_missing = _report(played={"dry": True, "shaped": False})
        assert any(
            "did not confirm both cues" in message for message in self._messages(shaped_missing)
        )

    def test_silent_output_fails(self) -> None:
        silent = _report(rms=0.0, events=[_event("start", 0.0, 0.5), _event("start", 0.0, 0.9)])
        assert "fail" in self._findings(silent)
        assert any("silent" in message for message in self._messages(silent))

    def test_a_shaped_level_outside_the_band_fails(self) -> None:
        shaped = _report(
            events=[
                _event("start", 0.3530, 0.5),
                _event("start", 0.3545, 0.9),
                _event("start", 0.9500, 1.7),
                _event("start", 0.9800, 1.8),
            ]
        )
        assert "fail" in self._findings(shaped)
        assert any("outside the inert band" in message for message in self._messages(shaped))

    def test_a_uniform_audible_level_fails(self) -> None:
        uniform = _report(
            events=[
                _event("start", 0.3000, 0.5),
                _event("start", 0.3010, 0.9),
                _event("start", 0.2990, 1.7),
                _event("start", 0.3005, 1.8),
            ]
        )
        assert "fail" in self._findings(uniform)
        assert any("outside the inert band" in message for message in self._messages(uniform))

    def test_no_surface_warns_and_stays_green_on_the_markers(self) -> None:
        bare = _report(events=[], started=0, rms=0.0)
        severities = self._findings(bare)
        assert "fail" not in severities
        assert "warning" in severities

    def test_underruns_and_page_errors_are_recorded(self) -> None:
        noisy = _report(underruns=2, errors=["first", "second", "third"])
        messages = self._messages(noisy)
        assert any("2 scheduled start(s)" in message for message in messages)
        assert any("3 error(s)" in message for message in messages)
        truncated = _report(droppedEvents=7)
        assert any("dropped 7 event(s)" in message for message in self._messages(truncated))

    def test_quiet_output_still_measures(self) -> None:
        quiet = _report(
            rms=0.0177,
            events=[
                _event("start", 0.0177, 0.5),
                _event("start", 0.0179, 0.9),
                _event("start", 0.0088, 1.7),
                _event("start", 0.0090, 1.8),
            ],
        )
        severities = self._findings(quiet)
        assert "fail" not in severities
        assert any("inert on Web" in message for message in self._messages(quiet))


class BrowserDiscoveryTests(unittest.TestCase):
    def setUp(self) -> None:
        directory = tempfile.TemporaryDirectory(prefix="dxaudio browser args ")
        self.addCleanup(directory.cleanup)
        self.root_temp = directory.name

    def test_override_must_exist(self) -> None:
        message = _expect_drive_failure(lambda: webgl_drive.find_browser("/missing/browser"))
        assert "does not exist" in message

    def test_override_returns_the_path(self) -> None:
        directory = tempfile.TemporaryDirectory(prefix="dxaudio browser ")
        self.addCleanup(directory.cleanup)
        browser = Path(directory.name) / "browser.sh"
        browser.write_text("#!/bin/sh\n", encoding="utf-8")
        browser.chmod(0o755)
        assert webgl_drive.find_browser(str(browser)) == str(browser)

    def test_launch_arguments_keep_the_lane_flags(self) -> None:
        arguments = webgl_drive.browser_arguments(
            9222, Path(self.root_temp) / "profile", "http://127.0.0.1:8000/index.html"
        )
        assert "--headless=new" in arguments
        assert "--autoplay-policy=no-user-gesture-required" in arguments
        assert "--remote-debugging-port=9222" in arguments
        assert arguments[-1] == "http://127.0.0.1:8000/index.html"


class CdpRoundTripTests(unittest.TestCase):
    """Speaks the DevTools websocket by hand to prove the client's framing."""

    def setUp(self) -> None:
        self.server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.server.bind(("127.0.0.1", 0))
        self.server.listen(1)
        self.port = int(self.server.getsockname()[1])
        self.pong_seen = threading.Event()
        thread = threading.Thread(target=self._serve, daemon=True)
        thread.start()
        self.addCleanup(self.server.close)

    def _read_exact(self, connection: socket.socket, count: int) -> bytes:
        data = b""
        while len(data) < count:
            chunk = connection.recv(count - len(data))
            if not chunk:
                break
            data += chunk
        return data

    def _read_frame(self, connection: socket.socket) -> tuple[int, bytes]:
        header = self._read_exact(connection, 2)
        length = header[1] & 0x7F
        mask = b""
        if header[1] & 0x80:
            mask = self._read_exact(connection, 4)
        payload = self._read_exact(connection, length)
        if mask:
            payload = bytes(byte ^ mask[index % 4] for index, byte in enumerate(payload))
        return header[0] & 0x0F, payload

    def _text_frame(self, text: str) -> bytes:
        payload = text.encode("utf-8")
        return bytes([0x81, len(payload)]) + payload

    def _serve(self) -> None:
        try:
            connection, _ = self.server.accept()
            data = b""
            while b"\r\n\r\n" not in data:
                chunk = connection.recv(4096)
                if not chunk:
                    return
                data += chunk
            headers: dict[str, str] = {}
            for line in data.decode("ascii", errors="replace").split("\r\n")[1:]:
                name, separator, value = line.partition(":")
                if separator:
                    headers[name.strip().lower()] = value.strip()
            key = headers.get("sec-websocket-key", "")
            accept = base64.b64encode(
                hashlib.sha1((key + webgl_drive.WEBSOCKET_GUID).encode("ascii")).digest()  # noqa: S324 - RFC 6455 mandates SHA-1.
            ).decode("ascii")
            response = (
                "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\n"
                f"Connection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n"
            )
            connection.sendall(response.encode("ascii"))
            connection.sendall(bytes([0x89, 4]) + b"ping")
            while True:
                opcode, payload = self._read_frame(connection)
                if opcode == 0x8:
                    break
                if opcode == 0xA:
                    self.pong_seen.set()
                    continue
                if opcode == 0x1:
                    message = json.loads(payload.decode("utf-8"))
                    reply = json.dumps(
                        {"id": message["id"], "result": {"result": {"type": "integer", "value": 2}}}
                    )
                    connection.sendall(self._text_frame(reply))
        except OSError:
            return

    def test_call_answers_one_id_and_pongs_the_ping(self) -> None:
        connection = webgl_drive.CdpConnection("127.0.0.1", self.port, "/devtools/page/1")
        self.addCleanup(connection.close)
        assert connection.evaluate("1+1") == 2
        assert self.pong_seen.wait(timeout=2), "the client never ponged the ping"


class FakeBrowserDriveTests(unittest.TestCase):
    """Drives the whole harness against a fake Chromium over real sockets."""

    def setUp(self) -> None:
        if os.name == "nt":
            self.skipTest("the fake browser needs a POSIX executable script")
        directory = tempfile.TemporaryDirectory(prefix="dxaudio webgl fake ")
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        build = self.root / "build"
        build.mkdir()
        (build / "index.html").write_text(INDEX_HTML, encoding="utf-8")
        (build / "Build").mkdir()
        (build / "Build" / "smoke.wasm").write_bytes(b"wasm")
        (build / "Build" / "smoke.data").write_bytes(b"data")
        self.build = build
        self.browser = self.root / "fake-browser.py"
        self.browser.write_text(FAKE_BROWSER, encoding="utf-8")
        self.browser.chmod(0o755)

    def _run(self, control: str, timeout_seconds: float) -> int:
        with patch.dict(os.environ, {"FAKE_CDP_CONTROL": control}):
            return webgl_drive.run_drive(
                webgl_drive.DriveOptions(
                    build_dir=self.build,
                    report_dir=self.root / "report",
                    browser=str(self.browser),
                    port=0,
                    cdp_port=0,
                    timeout_seconds=timeout_seconds,
                )
            )

    def test_ok_report_passes_and_keeps_the_build_pristine(self) -> None:
        status = self._run("ok", 30.0)
        assert status == 0
        assert 'src="shim.js"' not in (self.build / "index.html").read_text(encoding="utf-8")
        serve = self.root / "report" / "webgl-serve" / "index.html"
        assert 'src="shim.js"' in serve.read_text(encoding="utf-8")
        report = json.loads(
            (self.root / "report" / "webgl-drive-report.json").read_text(encoding="utf-8")
        )
        assert report["done"] is True

    def test_silent_output_fails(self) -> None:
        message = _expect_drive_failure(lambda: self._run("silent", 30.0))
        assert "silent" in message

    def test_shaped_audio_outside_the_band_fails(self) -> None:
        message = _expect_drive_failure(lambda: self._run("shaped-ran", 30.0))
        assert "outside the inert band" in message

    def test_played_markers_are_required(self) -> None:
        message = _expect_drive_failure(lambda: self._run("no-played", 30.0))
        assert "did not confirm both cues" in message

    def test_a_hung_smoke_times_out_but_keeps_its_report(self) -> None:
        message = _expect_drive_failure(lambda: self._run("no-done", 3.0))
        assert "done marker" in message
        report_path = self.root / "report" / "webgl-drive-report.json"
        report = json.loads(report_path.read_text(encoding="utf-8"))
        assert report["done"] is False

    def test_a_missing_shim_times_out_without_a_report(self) -> None:
        message = _expect_drive_failure(lambda: self._run("no-shim", 3.0))
        assert "never installed window.__audioReport" in message
        assert not (self.root / "report" / "webgl-drive-report.json").exists()


FAKE_BROWSER = r"""#!/usr/bin/env python3
import base64, hashlib, json, os, socket, struct, sys, threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

args = sys.argv[1:]

def value(flag):
    for index, item in enumerate(args):
        if item == flag:
            return args[index + 1] if index + 1 < len(args) else ""
        if item.startswith(flag + "="):
            return item[len(flag) + 1:]
    return ""

port = int(value("--remote-debugging-port") or "0")
control = os.environ.get("FAKE_CDP_CONTROL", "ok")

def block(kind, rms, when, duration=0.4):
    return {"kind": kind, "when": when, "rms": rms, "duration": duration, "channels": 2}

ok_events = [
    block("start", 0.3530, 0.5),
    block("start", 0.3545, 0.9),
    block("start", 0.1760, 1.4),
    block("start", 0.1780, 1.8),
]
REPORTS = {
    "ok": {
        "started": 4, "underruns": 0, "rms": 0.3545, "events": ok_events,
        "played": {"dry": True, "shaped": True}, "done": True, "errors": [],
    },
    "silent": {
        "started": 4, "underruns": 0, "rms": 0.0,
        "events": [block("start", 0.0, 0.5), block("start", 0.0, 0.9)],
        "played": {"dry": True, "shaped": True}, "done": True, "errors": [],
    },
    "shaped-ran": {
        "started": 4, "underruns": 0, "rms": 0.9800,
        "events": [
            block("start", 0.3530, 0.5),
            block("start", 0.3545, 0.9),
            block("start", 0.9500, 1.4),
            block("start", 0.9800, 1.8),
        ],
        "played": {"dry": True, "shaped": True}, "done": True, "errors": [],
    },
    "no-played": {
        "started": 4, "underruns": 0, "rms": 0.3545, "events": ok_events,
        "played": {"dry": True, "shaped": False}, "done": True, "errors": [],
    },
    "no-done": {
        "started": 0, "underruns": 0, "rms": 0.0, "events": [],
        "played": {"dry": False, "shaped": False}, "done": False, "errors": [],
    },
}
report = REPORTS.get(control, REPORTS["ok"])

class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path == "/json/list":
            body = json.dumps([
                {
                    "type": "page",
                    "url": "http://127.0.0.1:1/index.html",
                    "webSocketDebuggerUrl": "ws://127.0.0.1:%d/devtools/page/1" % ws_port,
                }
            ]).encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
        else:
            self.send_response(404)
            self.send_header("Content-Length", "0")
            self.end_headers()

    def log_message(self, fmt, *fmt_args):
        pass

def recv_exact(connection, count):
    data = b""
    while len(data) < count:
        chunk = connection.recv(count - len(data))
        if not chunk:
            break
        data += chunk
    return data

ws_server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
ws_server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
ws_server.bind(("127.0.0.1", 0))
ws_server.listen(1)
ws_port = ws_server.getsockname()[1]

def handle_ws():
    connection, _ = ws_server.accept()
    data = b""
    while b"\r\n\r\n" not in data:
        chunk = connection.recv(4096)
        if not chunk:
            return
        data += chunk
    headers = {}
    for line in data.decode("ascii", errors="replace").split("\r\n")[1:]:
        name, sep, val = line.partition(":")
        if sep:
            headers[name.strip().lower()] = val.strip()
    key = headers.get("sec-websocket-key", "")
    accept = base64.b64encode(
        hashlib.sha1((key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11").encode("ascii")).digest()
    ).decode("ascii")
    connection.sendall(
        (
            "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\n"
            "Connection: Upgrade\r\nSec-WebSocket-Accept: %s\r\n\r\n" % accept
        ).encode("ascii")
    )
    probes = 0

    def read_frame():
        first = b""
        while len(first) < 2:
            chunk = connection.recv(2 - len(first))
            if not chunk:
                return None, b""
            first += chunk
        masked = bool(first[1] & 0x80)
        length = first[1] & 0x7F
        extra = length
        if length == 126:
            extra = struct.unpack(">H", recv_exact(connection, 2))[0]
        elif length == 127:
            extra = struct.unpack(">Q", recv_exact(connection, 8))[0]
        mask = recv_exact(connection, 4) if masked else b""
        payload = recv_exact(connection, extra)
        if masked:
            payload = bytes(b ^ mask[i % 4] for i, b in enumerate(payload))
        return first[0] & 0x0F, payload

    def send_text(text):
        payload = text.encode("utf-8")
        header = bytes([0x81])
        if len(payload) < 126:
            header += bytes([len(payload)])
        else:
            header += bytes([126]) + struct.pack(">H", len(payload))
        connection.sendall(header + payload)

    while True:
        opcode, payload = read_frame()
        if opcode is None or opcode == 8:
            break
        if opcode != 1:
            continue
        try:
            message = json.loads(payload.decode("utf-8"))
        except ValueError:
            continue
        method = message.get("method", "")
        params = message.get("params") or {}
        expression = str(params.get("expression", ""))
        if "document.readyState" in expression:
            value_out = "complete"
        elif "JSON.stringify" in expression:
            value_out = None if control == "no-shim" else json.dumps(report)
        elif method == "Runtime.evaluate":
            probes += 1
            value_out = control not in ("no-done", "no-shim") and probes >= 3
        else:
            value_out = None
        send_text(
            json.dumps(
                {"id": message.get("id"), "result": {"result": {"type": "json", "value": value_out}}}
            )
        )

threading.Thread(target=handle_ws, daemon=True).start()
ThreadingHTTPServer(("127.0.0.1", port), Handler).serve_forever()
"""

if __name__ == "__main__":
    unittest.main()
