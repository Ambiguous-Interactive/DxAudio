#!/usr/bin/env python3
# /// script
# requires-python = ">=3.11"
# dependencies = []
# ///
"""Serve a built WebGL smoke player and drive it in a headless browser.

This is the second half of the WebGL lane (PLAN.md W-CI, spec map 01, items 2
to 6). ``--webgl-build`` produces the player; this module serves that build
over a plain static file server, injects ``webgl_shim.js`` into a served copy,
opens the page in headless Chromium with the autoplay restriction lifted, and
reads ``window.__audioReport`` over the Chrome DevTools protocol.

The first run is the lane's red measurement on today's code: Unity does not
run ``OnAudioFilterRead`` on Web, so the cue shaped through a DSP chain and
graph must measure at exactly the level ratio of its dry counterpart (half,
because the smoke player authors the shaped clip at half amplitude). A ratio
outside the inert band means the chains actually ran, which contradicts the
recorded platform facts and fails the lane until the record is updated.

Examples::

    uv run --no-project scripts/unity/unity_validation.py drive \\
        --build-dir "$RUNNER_TEMP/dxaudio-results-6000.5.2f1-webgl/webgl-build"
"""

from __future__ import annotations

import base64
import contextlib
import hashlib
import http.client
import json
import os
import shutil
import socket
import struct
import subprocess
import sys
import time
import urllib.parse
import urllib.request
from dataclasses import dataclass
from math import sqrt
from pathlib import Path
from typing import TYPE_CHECKING, cast

if TYPE_CHECKING:
    from collections.abc import Callable, Mapping, Sequence

SHIM_SCRIPT_TAG = '<script src="shim.js"></script>'
SHIM_NAME = "webgl_shim.js"
SMOKE_MARKER = "dxaudio-web-smoke"
REPORT_EXPRESSION = "JSON.stringify(window.__audioReport)"
DONE_PROBE_EXPRESSION = (
    "(() => { const r = window.__audioReport; return Boolean(r) && r.done === true; })()"
)
READY_PROBE_EXPRESSION = "document.readyState"
LINUX_BROWSER_NAMES: tuple[str, ...] = (
    "google-chrome",
    "google-chrome-stable",
    "chromium",
    "chromium-browser",
    "chrome",
    "microsoft-edge",
    "msedge",
)
# Each entry is an environment variable plus the application-relative path to
# one Chromium-family executable the named variable's installation root owns.
WINDOWS_BROWSER_PATHS: tuple[tuple[str, str], ...] = (
    ("ProgramFiles", "Google/Chrome/Application/chrome.exe"),
    ("ProgramFiles(x86)", "Google/Chrome/Application/chrome.exe"),
    ("LocalAppData", "Google/Chrome/Application/chrome.exe"),
    ("ProgramFiles", "Microsoft/Edge/Application/msedge.exe"),
    ("ProgramFiles(x86)", "Microsoft/Edge/Application/msedge.exe"),
)
MAC_BROWSER_PATHS: tuple[str, ...] = (
    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
    "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
    "/Applications/Chromium.app/Contents/MacOS/Chromium",
)
WEBSOCKET_GUID = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"
# The smoke player authors the shaped clip at half amplitude, so an inert Web
# output measures a shaped-to-dry level ratio of exactly one half. The band is
# wide against that target and still two to four times away from any DSP run:
# the bit-crusher plus drive-20 distortion raises the level far past the band.
EXPECTED_RATIO = 0.5
INERT_BAND = (0.35, 0.65)
SILENT_RMS = 0.01
SHAPED_SILENCE_DIVISOR = 20.0
MAX_FRAME_BYTES = 64 * 1024 * 1024


class DriveFailure(Exception):
    """A rejected WebGL drive run."""


def _identity(text: str) -> str:
    return text


@dataclass(frozen=True)
class DriveOptions:
    """Where the build lives, where the evidence goes, and which browser runs."""

    build_dir: Path
    report_dir: Path
    browser: str = ""
    port: int = 0
    cdp_port: int = 0
    timeout_seconds: float = 120.0
    redact: Callable[[str], str] = _identity


@dataclass(frozen=True)
class AudioEvent:
    """One observed output-audio block or scheduled start."""

    kind: str
    rms: float
    duration: float
    when: float


@dataclass(frozen=True)
class Measurement:
    """The dry-versus-shaped level comparison the lane's red test reads."""

    dry_rms: float
    shaped_rms: float
    ratio: float
    dry_events: int
    shaped_events: int


@dataclass(frozen=True)
class Finding:
    """One verdict line; ``fail`` rejects the run, the rest record evidence."""

    severity: str
    message: str


def _fail(message: str) -> Finding:
    return Finding("fail", message)


def _warning(message: str) -> Finding:
    return Finding("warning", message)


def _note(message: str) -> Finding:
    return Finding("note", message)


def _as_mapping(value: object) -> dict[str, object] | None:
    """Narrow one decoded JSON value to a string-keyed mapping, else None."""
    return cast("dict[str, object]", value) if isinstance(value, dict) else None


def _as_list(value: object) -> list[object] | None:
    """Narrow one decoded JSON value to a list, else None."""
    return cast("list[object]", value) if isinstance(value, list) else None


def _coerce_float(value: object) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return 0.0
    return float(value)


def _coerce_int(value: object) -> int:
    if isinstance(value, bool) or not isinstance(value, int):
        return 0
    return int(value)


def _child_mapping(report: Mapping[str, object], key: str) -> dict[str, object]:
    child = _as_mapping(report.get(key))
    return child if child is not None else {}


def _played_flags(report: Mapping[str, object]) -> tuple[bool, bool]:
    played = _child_mapping(report, "played")
    return played.get("dry") is True, played.get("shaped") is True


def _audio_events(report: Mapping[str, object]) -> list[AudioEvent]:
    """Collect the output-audio observations, skipping the input-side buffers."""
    events = _as_list(report.get("events"))
    if events is None:
        return []
    result: list[AudioEvent] = []
    for entry in events:
        block = _as_mapping(entry)
        if block is None:
            continue
        kind = block.get("kind")
        if kind not in ("start", "process"):
            continue
        result.append(
            AudioEvent(
                kind=str(kind),
                rms=_coerce_float(block.get("rms")),
                duration=_coerce_float(block.get("duration")),
                when=_coerce_float(block.get("when")),
            )
        )
    return result


def _largest_gap_split(whens: Sequence[float]) -> int | None:
    """Return the split index after the largest gap between consecutive times."""
    if len(whens) < 2:
        return None
    best_index = None
    best_gap = 0.0
    for index in range(1, len(whens)):
        gap = whens[index] - whens[index - 1]
        if gap > best_gap:
            best_gap = gap
            best_index = index
    if best_index is None or best_gap <= 0.0:
        return None
    return best_index


def measure_inertness(events: Sequence[AudioEvent]) -> Measurement | None:
    """Compare the shaped and dry output levels by energy-weighted RMS.

    The cues play in sequence, so the window split follows the largest gap
    between scheduled times, or the observation halves when the times carry no
    gaps. The split runs BEFORE any level filtering, so a silent shaped window
    stays a window and reads as silent instead of vanishing into the other
    side. Inside a window, exact-zero blocks are skipped (gap silence carries
    no energy) and the window level is the energy-weighted RMS of the rest;
    short fade chunks carry little energy and barely move it.
    """
    ordered = [event for event in events if event.duration > 0.0]
    if len(ordered) < 2:
        return None
    split = _largest_gap_split([event.when for event in ordered])
    if split is None:
        split = len(ordered) // 2
    dry = ordered[:split]
    shaped = ordered[split:]
    if not dry or not shaped:
        return None
    dry_rms = _window_rms(dry)
    shaped_rms = _window_rms(shaped)
    if dry_rms <= 0.0:
        return None
    return Measurement(
        dry_rms=dry_rms,
        shaped_rms=shaped_rms,
        ratio=shaped_rms / dry_rms,
        dry_events=len(dry),
        shaped_events=len(shaped),
    )


def _window_rms(window: Sequence[AudioEvent]) -> float:
    """Return the energy-weighted RMS of one window's non-silent blocks."""
    energy = 0.0
    time = 0.0
    for event in window:
        if event.rms <= 0.0:
            continue
        energy += event.rms * event.rms * event.duration
        time += event.duration
    if time <= 0.0:
        return 0.0
    return sqrt(energy / time)


def _surface_inventory(report: Mapping[str, object]) -> int:
    """Count the Web Audio surfaces the shim saw, without output measurement."""
    contexts = _as_list(report.get("contexts"))
    return (
        (len(contexts) if contexts is not None else 0)
        + _coerce_int(report.get("worklets"))
        + _coerce_int(report.get("processors"))
    )


def evaluate_report(report: Mapping[str, object]) -> list[Finding]:
    """Judge one shim report against the lane's contract and record the red finding."""
    findings: list[Finding] = []
    dry_played, shaped_played = _played_flags(report)
    if report.get("done") is not True:
        findings.append(
            _fail("the shim never saw the smoke finish; the player did not reach its done marker")
        )
    if not dry_played or not shaped_played:
        findings.append(
            _fail(
                "the smoke player did not confirm both cues playing "
                f"(dry: {dry_played}, shaped: {shaped_played})"
            )
        )
    events = _audio_events(report)
    if not events:
        surface = _surface_inventory(report)
        if surface > 0:
            findings.append(
                _fail(
                    f"the player built a Web Audio surface (contexts, worklets, processors: "
                    f"{surface} item(s)) but the shim measured no output blocks; the "
                    "silent-output guard cannot run on an unmeasured surface, so the lane "
                    "fails closed until the shim measures this architecture"
                )
            )
        else:
            findings.append(
                _warning(
                    "no Web Audio output surface was observed; the lane fell back to the "
                    "API-level markers and the headless output stays unverified (spec 01 fallback)"
                )
            )
        return findings
    # The silence verdict reads the output-side events only: a build could
    # create loud clip buffers it never schedules, and input RMS must not
    # mask a silent output.
    peak = max((event.rms for event in events), default=0.0)
    if peak < SILENT_RMS:
        findings.append(
            _fail(f"the Web output measured silent (peak RMS {peak:.4f} under {SILENT_RMS})")
        )
        return findings
    measurement = measure_inertness(events)
    if measurement is None:
        findings.append(
            _fail(
                "the measurement could not attribute the cue windows from audible output; "
                "the dry window carried no audible block or the player broke the "
                "sequential playback this measurement reads"
            )
        )
    elif measurement.shaped_rms < measurement.dry_rms / SHAPED_SILENCE_DIVISOR:
        findings.append(
            _fail(
                f"the shaped output measured silent (RMS {measurement.shaped_rms:.4f} against "
                f"dry {measurement.dry_rms:.4f}): a silent-output regression or a muted shaped "
                "voice on Web"
            )
        )
    elif not INERT_BAND[0] <= measurement.ratio <= INERT_BAND[1]:
        findings.append(
            _fail(
                f"the shaped cue measured outside the inert band (ratio {measurement.ratio:.3f}, "
                f"band {INERT_BAND[0]:.2f}-{INERT_BAND[1]:.2f}): OnAudioFilterRead appears to run "
                "on Web, contradicting the recorded platform facts; update the W-CI record"
            )
        )
    else:
        findings.append(
            _note(
                "chains and graphs are inert on Web (measured): the shaped cue played "
                f"unshaped at ratio {measurement.ratio:.3f} of its dry level; this is the "
                "W-CI limitation the lane records"
            )
        )
    underruns = _coerce_int(report.get("underruns"))
    if underruns > 0:
        findings.append(_note(f"{underruns} scheduled start(s) fired behind the audio clock"))
    page_errors = _as_list(report.get("errors"))
    if page_errors:
        shown = "; ".join(str(entry) for entry in page_errors[:3])
        findings.append(_warning(f"the page reported {len(page_errors)} error(s): {shown}"))
    dropped = _coerce_int(report.get("droppedEvents"))
    if dropped > 0:
        findings.append(
            _warning(
                f"the shim dropped {dropped} event(s) at its record cap; the report is partial"
            )
        )
    return findings


def stage_serve_copy(build_dir: Path, serve_dir: Path, shim_path: Path) -> Path:
    """Copy the build, inject the shim ahead of Unity's loader, and return the index."""
    index_path = build_dir / "index.html"
    if not index_path.is_file():
        raise DriveFailure(f"the WebGL build has no index.html to serve: {build_dir}")
    if not shim_path.is_file():
        raise DriveFailure(f"the Web Audio shim is missing: {shim_path}")
    if serve_dir.exists():
        shutil.rmtree(serve_dir)
    shutil.copytree(build_dir, serve_dir)
    text = index_path.read_text(encoding="utf-8")
    lowered = text.lower()
    anchor = lowered.find("<head")
    if anchor < 0:
        raise DriveFailure("the served index.html has no <head> tag; the shim cannot load first.")
    closing = text.find(">", anchor)
    if closing < 0:
        raise DriveFailure("the served index.html has a malformed <head> tag.")
    insert_at = closing + 1
    patched = text[:insert_at] + "\n    " + SHIM_SCRIPT_TAG + text[insert_at:]
    (serve_dir / "index.html").write_text(patched, encoding="utf-8")
    shutil.copy2(shim_path, serve_dir / "shim.js")
    return serve_dir / "index.html"


def _free_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as probe:
        probe.bind(("127.0.0.1", 0))
        return int(probe.getsockname()[1])


def _url_ok(url: str) -> bool:
    request = urllib.request.Request(url, method="GET")  # noqa: S310 - fixed loopback URL.
    try:
        with urllib.request.urlopen(request, timeout=2) as response:  # noqa: S310 - fixed loopback URL.
            return bool(response.status == 200)
    except (OSError, http.client.HTTPException):
        return False


class HttpFileServer:
    """One ``http.server`` subprocess serving the patched build copy."""

    def __init__(self, root: Path, port: int = 0) -> None:
        self._root = root
        self._requested_port = port
        self._process: subprocess.Popen[bytes] | None = None
        self.port = 0

    def start(self) -> None:
        port = self._requested_port if self._requested_port > 0 else _free_port()
        command = [
            sys.executable,
            "-m",
            "http.server",
            str(port),
            "--bind",
            "127.0.0.1",
            "--directory",
            str(self._root),
        ]
        self._process = subprocess.Popen(  # noqa: S603 - fixed interpreter and repository layout.
            command,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            start_new_session=sys.platform != "win32",
        )
        self.port = port
        deadline = time.monotonic() + 15.0
        url = f"http://127.0.0.1:{port}/index.html"
        while time.monotonic() < deadline:
            if self._process.poll() is not None:
                raise DriveFailure(
                    f"the static file server exited with code {self._process.returncode}."
                )
            if _url_ok(url):
                return
            time.sleep(0.1)
        self.stop()
        raise DriveFailure("the static file server never served index.html.")

    def stop(self) -> None:
        _stop_process(self._process)
        self._process = None


def _stop_process(process: subprocess.Popen[bytes] | None) -> None:
    if process is None or process.poll() is not None:
        return
    if sys.platform == "win32":
        subprocess.run(  # noqa: S603 - fixed system command with a numeric process id.
            ["taskkill", "/PID", str(process.pid), "/T", "/F"],  # noqa: S607 - Windows system utility.
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            check=False,
        )
    else:
        with contextlib.suppress(ProcessLookupError):
            os.killpg(os.getpgid(process.pid), 9)
    with contextlib.suppress(subprocess.TimeoutExpired):
        process.wait(timeout=5)


def find_browser(override: str) -> str:
    """Locate one Chromium-family executable, failing with the searched paths."""
    candidate = override.strip()
    if candidate:
        if Path(candidate).is_file():
            return candidate
        resolved = shutil.which(candidate)
        if resolved:
            return resolved
        raise DriveFailure(f"the configured browser does not exist: {candidate}")
    searched: list[str] = []
    for name in LINUX_BROWSER_NAMES:
        path = shutil.which(name)
        if path:
            return path
        searched.append(name)
    if sys.platform == "win32":
        for variable, suffix in WINDOWS_BROWSER_PATHS:
            root = os.environ.get(variable, "")
            path = str(Path(root) / suffix) if root else ""
            searched.append(path or variable)
            if path and Path(path).is_file():
                return path
    elif sys.platform == "darwin":
        for path in MAC_BROWSER_PATHS:
            searched.append(path)
            if Path(path).is_file():
                return path
    listing = ", ".join(searched)
    raise DriveFailure(
        "no Chromium-family browser was found (searched: "
        f"{listing}). Install Chrome, Chromium, or Edge, or pass "
        "--browser / DXAUDIO_WEB_BROWSER."
    )


def browser_arguments(cdp_port: int, profile_dir: Path, url: str) -> list[str]:
    """The headless launch flags: autoplay lifted, CDP open, software WebGL."""
    return [
        "--headless=new",
        "--autoplay-policy=no-user-gesture-required",
        f"--remote-debugging-port={cdp_port}",
        f"--user-data-dir={profile_dir}",
        "--remote-allow-origins=*",
        "--no-sandbox",
        "--disable-gpu",
        "--enable-unsafe-swiftshader",
        "--disable-dev-shm-usage",
        "--no-first-run",
        "--disable-extensions",
        "--disable-background-networking",
        url,
    ]


class CdpConnection:
    """A minimal DevTools client: one websocket, evaluate calls, nothing else.

    The standard library has no websocket client, and the lane must stay
    dependency-free, so this speaks the frames the DevTools socket needs:
    client-side masking, text frames, ping/pong, close, and reassembly.
    """

    def __init__(self, host: str, port: int, path: str, timeout_seconds: float = 10.0) -> None:
        self._socket = socket.create_connection((host, port), timeout=timeout_seconds)
        try:
            self._timeout_seconds = timeout_seconds
            self._next_id = 0
            key = base64.b64encode(os.urandom(16)).decode("ascii")
            request = (
                f"GET {path} HTTP/1.1\r\n"
                f"Host: {host}:{port}\r\n"
                "Upgrade: websocket\r\n"
                "Connection: Upgrade\r\n"
                f"Sec-WebSocket-Key: {key}\r\n"
                "Sec-WebSocket-Version: 13\r\n"
                "\r\n"
            )
            self._send_all(request.encode("ascii"))
            expected = base64.b64encode(
                hashlib.sha1((key + WEBSOCKET_GUID).encode("ascii")).digest()  # noqa: S324 - RFC 6455 mandates SHA-1 here.
            ).decode("ascii")
            response = self._read_http_head()
            header_lines = response.split("\r\n")
            status_parts = header_lines[0].split() if header_lines else []
            if len(status_parts) < 2 or status_parts[1] != "101":
                raise DriveFailure(f"the browser refused the DevTools upgrade: {header_lines[0]!r}")
            headers: dict[str, str] = {}
            for line in header_lines[1:]:
                name, separator, value = line.partition(":")
                if separator:
                    headers[name.strip().lower()] = value.strip()
            if headers.get("sec-websocket-accept", "") != expected:
                raise DriveFailure("the browser returned a wrong Sec-WebSocket-Accept value.")
        except BaseException:
            self._socket.close()
            raise

    def call(self, method: str, **parameters: object) -> dict[str, object]:
        """Send one DevTools command and return its result, skipping events."""
        self._next_id += 1
        message_id = self._next_id
        request = json.dumps({"id": message_id, "method": method, "params": parameters})
        self._send_text(request)
        deadline = time.monotonic() + self._timeout_seconds
        while True:
            if time.monotonic() > deadline:
                raise DriveFailure(f"the browser did not answer {method} in time.")
            try:
                payload = _as_mapping(json.loads(self._read_message()))
            except ValueError as error:
                raise DriveFailure(f"the browser sent invalid JSON: {error}") from error
            if payload is None or payload.get("id") != message_id:
                continue
            if "error" in payload:
                raise DriveFailure(f"{method} failed: {payload['error']!s}")
            result = _as_mapping(payload.get("result"))
            return result if result is not None else {}

    def evaluate(self, expression: str) -> object:
        result = self.call(
            "Runtime.evaluate", expression=expression, returnByValue=True, awaitPromise=False
        )
        if "exceptionDetails" in result:
            raise DriveFailure(f"the page evaluation failed: {result['exceptionDetails']!s}")
        inner = _as_mapping(result.get("result"))
        if inner is None:
            return None
        return inner.get("value")

    def evaluate_text(self, expression: str) -> str:
        value = self.evaluate(expression)
        return value if isinstance(value, str) else ""

    def click(self, x: int, y: int) -> None:
        """Dispatch one synthetic left click in case the player waits for a gesture."""
        for phase in ("mousePressed", "mouseReleased"):
            self.call("Input.dispatchMouseEvent", type=phase, x=x, y=y, button="left", clickCount=1)

    def close(self) -> None:
        with contextlib.suppress(OSError, DriveFailure):
            self._send_frame(0x8, b"")
        with contextlib.suppress(OSError):
            self._socket.close()

    def _send_all(self, payload: bytes) -> None:
        try:
            self._socket.sendall(payload)
        except OSError as error:
            raise DriveFailure(f"the DevTools socket write failed: {error}") from error

    def _recv_exact(self, count: int) -> bytes:
        chunks: list[bytes] = []
        remaining = count
        while remaining > 0:
            try:
                chunk = self._socket.recv(min(remaining, 65536))
            except OSError as error:
                raise DriveFailure(f"the DevTools socket read failed: {error}") from error
            if not chunk:
                raise DriveFailure("the DevTools socket closed mid-frame.")
            chunks.append(chunk)
            remaining -= len(chunk)
        return b"".join(chunks)

    def _read_http_head(self) -> str:
        data = b""
        while b"\r\n\r\n" not in data and len(data) < 16384:
            data += self._recv_exact(1)
        return data.decode("ascii", errors="replace")

    def _send_frame(self, opcode: int, payload: bytes) -> None:
        header = bytes([0x80 | opcode])
        length = len(payload)
        mask_bit = 0x80
        if length < 126:
            header += bytes([mask_bit | length])
        elif length <= 0xFFFF:
            header += bytes([mask_bit | 126]) + struct.pack(">H", length)
        else:
            header += bytes([mask_bit | 127]) + struct.pack(">Q", length)
        mask = os.urandom(4)
        masked = bytes(byte ^ mask[index % 4] for index, byte in enumerate(payload))
        self._send_all(header + mask + masked)

    def _send_text(self, text: str) -> None:
        self._send_frame(0x1, text.encode("utf-8"))

    def _read_frame(self) -> tuple[int, bool, bytes]:
        first, second = self._recv_exact(2)
        opcode = first & 0x0F
        final = bool(first & 0x80)
        masked = bool(second & 0x80)
        length = second & 0x7F
        if length == 126:
            length = struct.unpack(">H", self._recv_exact(2))[0]
        elif length == 127:
            length = struct.unpack(">Q", self._recv_exact(8))[0]
        if length > MAX_FRAME_BYTES:
            raise DriveFailure(
                f"the browser sent a {length}-byte DevTools frame; the cap is {MAX_FRAME_BYTES}."
            )
        mask = self._recv_exact(4) if masked else b""
        payload = self._recv_exact(length)
        if masked:
            payload = bytes(byte ^ mask[index % 4] for index, byte in enumerate(payload))
        return opcode, final, payload

    def _read_message(self) -> str:
        fragments: list[bytes] = []
        while True:
            opcode, final, payload = self._read_frame()
            if opcode == 0x8:
                raise DriveFailure("the browser closed the DevTools socket.")
            if opcode == 0x9:
                self._send_frame(0xA, payload)
                continue
            if opcode in (0x1, 0x2, 0x0):
                fragments.append(payload)
                if final:
                    break
        return b"".join(fragments).decode("utf-8", errors="replace")


def _page_target_url(cdp_port: int, timeout_seconds: float) -> str:
    """Return the page target's websocket URL once the DevTools socket listens."""
    listing_url = f"http://127.0.0.1:{cdp_port}/json/list"
    deadline = time.monotonic() + timeout_seconds
    request = urllib.request.Request(listing_url, method="GET")
    while time.monotonic() < deadline:
        try:
            with urllib.request.urlopen(request, timeout=2) as response:  # noqa: S310 - fixed loopback URL.
                targets = _as_list(json.loads(response.read().decode("utf-8")))
        except (OSError, http.client.HTTPException, ValueError):
            targets = _as_list([])
        if targets is not None:
            for target in targets:
                mapping = _as_mapping(target)
                if mapping is None:
                    continue
                url = mapping.get("webSocketDebuggerUrl")
                if mapping.get("type") == "page" and isinstance(url, str) and url:
                    return url
        time.sleep(0.2)
    raise DriveFailure(f"the browser never exposed a page target on port {cdp_port}.")


def _connect_to_page(cdp_port: int, timeout_seconds: float) -> CdpConnection:
    websocket_url = _page_target_url(cdp_port, timeout_seconds)
    parts = urllib.parse.urlsplit(websocket_url)
    host = parts.hostname or "127.0.0.1"
    port = parts.port or cdp_port
    return CdpConnection(host, port, parts.path or "/", timeout_seconds=10.0)


def _wait_for_document_ready(connection: CdpConnection, timeout_seconds: float) -> bool:
    deadline = time.monotonic() + timeout_seconds
    while time.monotonic() < deadline:
        state = connection.evaluate_text(READY_PROBE_EXPRESSION)
        if state in ("interactive", "complete"):
            return True
        time.sleep(0.25)
    return False


def _read_report(connection: CdpConnection) -> tuple[dict[str, object], str]:
    """Read the shim report once, returning it (possibly empty) with a problem note.

    The report read never raises: a broken socket or malformed payload must
    still return whatever evidence exists so the caller can keep it.
    """
    try:
        raw = connection.evaluate_text(REPORT_EXPRESSION)
    except DriveFailure as error:
        return {}, f"the report could not be read ({error})"
    if not raw:
        return {}, "the shim never installed window.__audioReport"
    try:
        report = _as_mapping(json.loads(raw))
    except ValueError as error:
        return {}, f"the shim report was not valid JSON ({error})"
    if report is None:
        return {}, "the shim report is not a JSON object"
    return report, ""


def _stall_summary(report: Mapping[str, object]) -> str:
    """Summarize what the shim saw when the done marker never arrived."""
    markers = _as_list(report.get("events"))
    marker_count = len(markers) if markers is not None else 0
    return (
        f"started={report.get('started')}, peak RMS={report.get('rms')}, "
        f"done={report.get('done')}, played={report.get('played')}, "
        f"events={marker_count}, errors={report.get('errors')}"
    )


def _poll_for_report(
    connection: CdpConnection,
    browser_process: subprocess.Popen[bytes],
    timeout_seconds: float,
) -> tuple[dict[str, object], str]:
    """Poll until the shim records the done marker, then read the full report.

    The second element names why the marker never arrived; it stays empty on a
    finished run. Neither a broken socket nor a malformed report raises here:
    the caller keeps whatever partial evidence exists.
    """
    deadline = time.monotonic() + timeout_seconds
    while time.monotonic() < deadline:
        if browser_process.poll() is not None:
            report, _ = _read_report(connection)
            return report, (
                f"the browser exited with code {browser_process.returncode} "
                "before the smoke finished"
            )
        try:
            finished = connection.evaluate(DONE_PROBE_EXPRESSION)
        except DriveFailure as error:
            report, _ = _read_report(connection)
            return report, f"the page stopped answering ({error})"
        if finished is True:
            report, problem = _read_report(connection)
            if report:
                return report, ""
            return {}, problem or "the smoke finished but produced no report"
        time.sleep(0.5)
    report, _ = _read_report(connection)
    if report:
        return report, (
            f"the smoke never reached its done marker within {timeout_seconds:.0f} seconds "
            f"({_stall_summary(report)})"
        )
    return {}, (
        f"the smoke never reached its done marker within {timeout_seconds:.0f} seconds "
        "(the shim never installed window.__audioReport)"
    )


def _refuse_related_paths(build_dir: Path, workspace_dir: Path) -> None:
    """Refuse a report workspace and build directory that contain each other.

    The cleanup removes the workspace before staging the serve copy, and
    ``copytree`` recurses, so overlap in either direction would delete the
    build or copy it into itself.
    """
    build = build_dir.resolve()
    workspace = workspace_dir.resolve()
    if build == workspace or workspace in build.parents or build in workspace.parents:
        raise DriveFailure(
            f"the report workspace {workspace} overlaps the build directory {build}; "
            "pass a --report-dir outside the build."
        )


def run_drive(options: DriveOptions) -> int:
    """Serve the build, drive it headless, judge the report, and keep the evidence."""
    shim_path = Path(__file__).resolve().with_name(SHIM_NAME)
    started = time.monotonic()
    serve_dir = options.report_dir / "webgl-serve"
    profile_dir = options.report_dir / "browser-profile"
    report_path = options.report_dir / "webgl-drive-report.json"
    _refuse_related_paths(options.build_dir, serve_dir)
    _refuse_related_paths(options.build_dir, profile_dir)
    options.report_dir.mkdir(parents=True, exist_ok=True)
    report_path.unlink(missing_ok=True)
    index_path = stage_serve_copy(options.build_dir, serve_dir, shim_path)
    server = HttpFileServer(serve_dir, options.port)
    browser_process: subprocess.Popen[bytes] | None = None
    connection: CdpConnection | None = None
    try:
        server.start()
        url = f"http://127.0.0.1:{server.port}/{index_path.name}"
        browser = find_browser(options.browser)
        cdp_port = options.cdp_port if options.cdp_port > 0 else _free_port()
        arguments = browser_arguments(cdp_port, profile_dir, url)
        print(f"WebGL drive: serving {serve_dir} on port {server.port}.", flush=True)
        print(f"WebGL drive: launching {browser} headless on CDP port {cdp_port}.", flush=True)
        browser_process = subprocess.Popen(  # noqa: S603 - browser path is resolved by find_browser.
            [browser, *arguments],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            start_new_session=sys.platform != "win32",
        )
        connection = _connect_to_page(cdp_port, 30.0)
        if _wait_for_document_ready(connection, 30.0):
            try:
                connection.click(20, 20)
            except DriveFailure as error:
                print(f"WebGL drive: the synthetic click was refused ({error}).", flush=True)
        report, stall = _poll_for_report(connection, browser_process, options.timeout_seconds)
    finally:
        if connection is not None:
            connection.close()
        _stop_process(browser_process)
        server.stop()
    report_path = options.report_dir / "webgl-drive-report.json"
    if report:
        report_path.write_text(
            json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
        )
        print(
            f"WebGL drive: report read in {time.monotonic() - started:.1f}s -> {report_path}",
            flush=True,
        )
    else:
        print(
            f"WebGL drive: no report to keep after {time.monotonic() - started:.1f}s.",
            flush=True,
        )
    _print_measurement(report)
    findings = evaluate_report(report)
    if stall:
        findings.insert(0, _fail(stall))
    failed = ""
    for finding in findings:
        message = options.redact(finding.message)
        prefix = {"fail": "", "warning": "::warning::", "note": ""}.get(finding.severity, "")
        print(f"{prefix}{message}", flush=True)
        if finding.severity == "fail" and not failed:
            failed = message
    if failed:
        raise DriveFailure(failed)
    _write_step_summary(report_path, findings, options.redact)
    print("WebGL drive PASSED.", flush=True)
    return 0


def _print_measurement(report: Mapping[str, object]) -> None:
    started = _coerce_int(report.get("started"))
    peak = _coerce_float(report.get("rms"))
    print(
        f"WebGL audio surface: {started} scheduled start(s), peak RMS {peak:.4f}, "
        f"{_coerce_int(report.get('underruns'))} underrun(s).",
        flush=True,
    )
    measurement = measure_inertness(_audio_events(report))
    if measurement is None:
        print("WebGL inert measurement: unavailable for this report.", flush=True)
        return
    print(
        f"WebGL inert measurement (red): dry RMS {measurement.dry_rms:.4f} "
        f"({measurement.dry_events} block(s)), shaped RMS {measurement.shaped_rms:.4f} "
        f"({measurement.shaped_events} block(s)), ratio {measurement.ratio:.3f} "
        f"(expected {EXPECTED_RATIO}, band {INERT_BAND[0]:.2f}-{INERT_BAND[1]:.2f}).",
        flush=True,
    )


def _write_step_summary(
    report_path: Path, findings: Sequence[Finding], redact: Callable[[str], str]
) -> None:
    summary_path = os.environ.get("GITHUB_STEP_SUMMARY", "")
    if not summary_path:
        return
    lines = ["### WebGL drive", "", f"Report: `{report_path.name}`", ""]
    lines.extend(f"- **{finding.severity}**: {redact(finding.message)}" for finding in findings)
    lines.append("")
    with Path(summary_path).open("a", encoding="utf-8", newline="") as summary:
        summary.write("\n".join(lines))


if __name__ == "__main__":
    print("This module runs through unity_validation.py drive.", file=sys.stderr, flush=True)
    raise SystemExit(2)
