import os
import signal
import socket
import subprocess
import sys
import threading
import time
from pathlib import Path

import webview

DASHBOARD_DIR = Path(__file__).parent
PORT = 8501
URL  = f"http://localhost:{PORT}"


def _server_ready(timeout: float = 20.0) -> bool:
    deadline = time.time() + timeout
    while time.time() < deadline:
        try:
            with socket.create_connection(("localhost", PORT), timeout=1):
                return True
        except OSError:
            time.sleep(0.25)
    return False


def _start_streamlit() -> subprocess.Popen:
    return subprocess.Popen(
        [sys.executable, "-m", "streamlit", "run",
         str(DASHBOARD_DIR / "app.py"),
         "--server.headless", "true",
         "--server.port", str(PORT)],
        cwd=str(DASHBOARD_DIR),
        creationflags=subprocess.CREATE_NO_WINDOW,
    )


def main():
    # Don't start a second server if one is already running
    already_running = False
    try:
        with socket.create_connection(("localhost", PORT), timeout=0.5):
            already_running = True
    except OSError:
        pass

    server_proc = None
    if not already_running:
        server_proc = _start_streamlit()
        if not _server_ready():
            webview.create_window(
                "HackNSLASH Dashboard — Error",
                html="<h2>Streamlit failed to start. Check your Python environment.</h2>",
                width=600, height=200,
            )
            webview.start()
            return

    window = webview.create_window(
        "HackNSLASH Dashboard",
        URL,
        resizable=True,
        min_size=(900, 600),
    )

    def on_closed():
        if server_proc:
            server_proc.send_signal(signal.CTRL_C_EVENT)

    def on_started():
        window.maximize()

    window.events.closed += on_closed
    webview.start(on_started)

    if server_proc:
        try:
            server_proc.terminate()
            server_proc.wait(timeout=5)
        except Exception:
            server_proc.kill()


if __name__ == "__main__":
    main()
