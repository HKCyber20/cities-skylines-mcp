"""TCP client for the in-game CS1 MCP Bridge mod.

Speaks the newline-delimited JSON protocol documented in PROTOCOL.md. One
request per line out, one response per line in. A single connection is reused
and guarded by a lock. Responses are checked against request IDs. An uncertain
write is never replayed automatically.
"""

from __future__ import annotations

import json
import socket
import threading
from typing import Any


class BridgeError(RuntimeError):
    """Raised when the bridge reports `ok: false` or is unreachable."""


class BridgeClient:
    def __init__(self, host: str = "127.0.0.1", port: int = 50545, timeout: float = 10.0):
        self._host = host
        self._port = port
        self._timeout = timeout
        self._sock: socket.socket | None = None
        self._reader = None  # buffered file object over the socket
        self._lock = threading.Lock()
        self._id = 0

    # -- connection management -------------------------------------------------
    def _connect(self) -> None:
        sock = socket.create_connection((self._host, self._port), timeout=self._timeout)
        sock.settimeout(self._timeout)
        self._sock = sock
        self._reader = sock.makefile("r", encoding="utf-8", newline="\n")

    def _reset(self) -> None:
        try:
            if self._reader:
                self._reader.close()
            if self._sock:
                self._sock.close()
        except OSError:
            pass
        finally:
            self._sock = None
            self._reader = None

    def close(self) -> None:
        with self._lock:
            self._reset()

    # -- request/response ------------------------------------------------------
    def _roundtrip(self, payload: str) -> str:
        if self._sock is None:
            self._connect()
        assert self._sock is not None and self._reader is not None
        self._sock.sendall(payload.encode("utf-8"))
        return self._reader.readline()

    def call(self, cmd: str, **args: Any) -> Any:
        with self._lock:
            self._id += 1
            payload = json.dumps({"id": self._id, "cmd": cmd, "args": args}) + "\n"

            try:
                line = self._roundtrip(payload)
            except (OSError, socket.timeout) as exc:
                # The request may already have reached the game. Never replay a
                # write command after a timeout or a dropped connection.
                self._reset()
                raise BridgeError(
                    f"bridge request {self._id} failed at {self._host}:{self._port}; "
                    f"the command may have executed, so check game state before retrying: {exc}"
                ) from exc

            if not line:
                self._reset()
                raise BridgeError(
                    "connection closed by the game; the command may have executed, "
                    "so check game state before retrying"
                )

            try:
                resp = json.loads(line)
            except (ValueError, TypeError) as exc:
                self._reset()
                raise BridgeError(f"invalid bridge response for request {self._id}: {exc}") from exc
            if not isinstance(resp, dict) or resp.get("id") != self._id:
                self._reset()
                raise BridgeError(f"bridge response id mismatch for request {self._id}")
            if not resp.get("ok"):
                raise BridgeError(resp.get("error", "unknown bridge error"))
            return resp.get("result")
