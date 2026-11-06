#!/usr/bin/env python3
"""Serves a published browser-wasm app on localhost, opens it in headless Chromium and collects what the page reports.

The page (smoke/wasm/wwwroot/main.js) POSTs every console line to /log and the process exit code to /exit. This script exits 0 only
when the app exited 0 and printed a `SMOKE-RESULT ok` line; it writes every line to --log. Chromium is told to resolve nothing but
localhost, so the run uses no network.
"""
import argparse
import http.server
import os
import shutil
import subprocess
import sys
import tempfile
import threading
import time

TYPES = {
    ".html": "text/html", ".js": "text/javascript", ".mjs": "text/javascript", ".json": "application/json",
    ".wasm": "application/wasm", ".dat": "application/octet-stream", ".map": "application/json",
}


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", required=True, help="the published wwwroot directory")
    ap.add_argument("--chrome", required=True, help="the chrome executable")
    ap.add_argument("--log", required=True, help="file that receives every console line")
    ap.add_argument("--timeout", type=int, default=1200, help="seconds before the run is abandoned")
    args = ap.parse_args()

    lines: list[str] = []
    done = threading.Event()
    exit_code: list[int] = []

    class Handler(http.server.BaseHTTPRequestHandler):
        def log_message(self, *a):  # keep the console quiet
            pass

        def _send(self, status, body=b"", ctype="text/plain"):
            self.send_response(status)
            self.send_header("Content-Type", ctype)
            self.send_header("Content-Length", str(len(body)))
            # Cross-origin isolation, as a host would send it to enable shared memory (threads).
            self.send_header("Cross-Origin-Opener-Policy", "same-origin")
            self.send_header("Cross-Origin-Embedder-Policy", "require-corp")
            self.send_header("Cache-Control", "no-store")
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self):
            rel = self.path.split("?")[0].lstrip("/") or "index.html"
            full = os.path.realpath(os.path.join(args.root, rel))
            if not full.startswith(os.path.realpath(args.root) + os.sep) or not os.path.isfile(full):
                self._send(404)
                return
            with open(full, "rb") as f:
                self._send(200, f.read(), TYPES.get(os.path.splitext(full)[1], "application/octet-stream"))

        def do_POST(self):
            body = self.rfile.read(int(self.headers.get("Content-Length", 0))).decode("utf-8", "replace")
            if self.path == "/log":
                lines.append(body)
            elif self.path == "/exit":
                exit_code.append(int(body) if body.lstrip("-").isdigit() else 98)
                done.set()
            self._send(204)

    server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    port = server.server_address[1]
    threading.Thread(target=server.serve_forever, daemon=True).start()

    profile = tempfile.mkdtemp(prefix="chrome-profile-", dir=os.environ.get("TMPDIR"))
    chrome = subprocess.Popen(
        [
            args.chrome, "--headless=new", "--no-sandbox", "--disable-gpu", "--disable-dev-shm-usage", "--no-first-run",
            "--disable-extensions", "--disable-background-networking", "--user-data-dir=" + profile,
            "--host-resolver-rules=MAP * ~NOTFOUND, EXCLUDE 127.0.0.1",
            f"http://127.0.0.1:{port}/index.html",
        ],
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
    )
    started = time.time()
    try:
        while not done.wait(1):
            if chrome.poll() is not None:
                lines.append(f"RUNNER chromium exited early with {chrome.returncode}")
                break
            if time.time() - started > args.timeout:
                lines.append(f"RUNNER timed out after {args.timeout}s")
                break
    finally:
        chrome.terminate()
        try:
            chrome.wait(15)
        except subprocess.TimeoutExpired:
            chrome.kill()
        server.shutdown()
        shutil.rmtree(profile, ignore_errors=True)

    with open(args.log, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    for l in lines:
        print(l)
    ok = exit_code == [0] and any(l.startswith("SMOKE-RESULT ok") for l in lines)
    print(f"RUNNER exit={exit_code} elapsed={time.time() - started:.0f}s -> {'PASS' if ok else 'FAIL'}")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
