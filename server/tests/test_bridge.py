"""Protocol transport tests; no network, game, or FastMCP needed."""

import json
import pathlib
import socket
import sys
import unittest
from unittest.mock import patch

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

from cs1_mcp.bridge import BridgeClient, BridgeError


class FakeReader:
    def __init__(self, reply):
        self.reply = reply

    def readline(self):
        if isinstance(self.reply, Exception):
            raise self.reply
        return self.reply

    def close(self):
        pass


class FakeSocket:
    def __init__(self, reply):
        self.reader = FakeReader(reply)
        self.requests = []

    def settimeout(self, timeout):
        pass

    def makefile(self, *args, **kwargs):
        return self.reader

    def sendall(self, payload):
        self.requests.append(json.loads(payload))

    def close(self):
        pass


class BridgeTests(unittest.TestCase):
    def test_success_and_request_id(self):
        sock = FakeSocket('{"id":1,"ok":true,"result":"pong"}\n')
        client = BridgeClient()
        with patch("cs1_mcp.bridge.socket.create_connection", return_value=sock) as connect:
            self.assertEqual(client.call("ping"), "pong")
        self.assertEqual(sock.requests, [{"id": 1, "cmd": "ping", "args": {}}])
        self.assertEqual(connect.call_count, 1)
        client.close()

    def test_dropped_write_is_not_replayed(self):
        sock = FakeSocket("")
        client = BridgeClient()
        with patch("cs1_mcp.bridge.socket.create_connection", return_value=sock) as connect:
            with self.assertRaises(BridgeError):
                client.call("add_money", amount=100)
        self.assertEqual(len(sock.requests), 1)
        self.assertEqual(connect.call_count, 1)

    def test_mismatched_response_id_is_rejected(self):
        sock = FakeSocket('{"id":9,"ok":true,"result":"pong"}\n')
        client = BridgeClient()
        with patch("cs1_mcp.bridge.socket.create_connection", return_value=sock):
            with self.assertRaisesRegex(BridgeError, "id mismatch"):
                client.call("ping")

    def test_timeout_does_not_replay_write(self):
        sock = FakeSocket(socket.timeout("waiting for game"))
        client = BridgeClient()
        with patch("cs1_mcp.bridge.socket.create_connection", return_value=sock) as connect:
            with self.assertRaisesRegex(BridgeError, "may have executed"):
                client.call("place_road", start_x=0)
        self.assertEqual(len(sock.requests), 1)
        self.assertEqual(connect.call_count, 1)


if __name__ == "__main__":
    unittest.main()
