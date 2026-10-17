#!/usr/bin/env python3
import argparse
import json
import time
import urllib.error
import urllib.request


def post_enqueue(base_url: str, timeout: float) -> tuple[bool, str]:
    url = f"{base_url.rstrip('/')}/enqueue-job-a"
    request = urllib.request.Request(url=url, method="POST")

    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            body = response.read().decode("utf-8", errors="replace")
            if not body.strip():
                body = "<empty>"
            return True, body
    except urllib.error.HTTPError as exc:
        body = exc.read().decode("utf-8", errors="replace")
        return False, f"HTTP {exc.code}: {body}"
    except urllib.error.URLError as exc:
        return False, f"Connection error: {exc.reason}"


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Enqueue Hangfire sample jobs for JobSearch dashboard validation."
    )
    parser.add_argument("--base-url", default="http://127.0.0.1:5099", help="Example app base URL.")
    parser.add_argument("--count", type=int, default=25, help="How many jobs to enqueue.")
    parser.add_argument("--delay-ms", type=int, default=0, help="Delay between requests in milliseconds.")
    parser.add_argument("--timeout", type=float, default=5.0, help="Request timeout in seconds.")
    args = parser.parse_args()

    if args.count <= 0:
        print("count must be > 0")
        return 1

    success = 0
    failures = 0
    delay_seconds = max(0, args.delay_ms) / 1000.0

    for index in range(1, args.count + 1):
        ok, message = post_enqueue(args.base_url, args.timeout)
        if ok:
            success += 1
            print(f"[{index}/{args.count}] enqueued: {message}")
        else:
            failures += 1
            print(f"[{index}/{args.count}] failed: {message}")

        if delay_seconds > 0 and index < args.count:
            time.sleep(delay_seconds)

    summary = {"requested": args.count, "success": success, "failed": failures}
    print(json.dumps(summary))
    return 0 if failures == 0 else 2


if __name__ == "__main__":
    raise SystemExit(main())
