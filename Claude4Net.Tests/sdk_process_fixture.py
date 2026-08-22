# /// script
# requires-python = ">=3.10"
# dependencies = []
# ///
# How to run: python sdk_process_fixture.py --port -4

import argparse
import os
import subprocess
import sys
import time
from typing import assert_never


def main() -> int:
    if sys.stdin.buffer.read(1) != b"\x01":
        return 1
    print("FIXTURE_FIRST_ACTION", flush=True)

    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, required=True)
    args = parser.parse_args()

    if not os.environ.get("CLAUDE4NET_TEST_API_KEY"):
        print("missing child API key", file=sys.stderr)
        return 8

    match args.port:
        case -1:
            child = subprocess.Popen(
                [sys.executable, "-c", "import time; time.sleep(30)"],
                stdin=subprocess.DEVNULL,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
            )
            print(f"CHILD_PID={child.pid}", flush=True)
            time.sleep(30)
        case -2:
            print("fixture failure", file=sys.stderr)
            return 7
        case -3:
            print("WRONG_MARKER")
        case -4:
            for index in range(20_000):
                print(f"stdout-{index}")
                print(f"stderr-{index}", file=sys.stderr)
            print("FIXTURE_OK")
        case -5:
            child = subprocess.Popen(
                [sys.executable, "-c", "import time; time.sleep(30)"],
                stdin=subprocess.DEVNULL,
            )
            print(f"CHILD_PID={child.pid}", flush=True)
            time.sleep(30)
        case -6:
            subprocess.Popen(
                [sys.executable, "-c", "import time; time.sleep(30)"],
                stdin=subprocess.DEVNULL,
            )
            print("FIXTURE_OK", flush=True)
        case unreachable:
            assert_never(unreachable)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
