"""Small process supervisor for Redis Mathematics workers."""

from __future__ import annotations

import multiprocessing as mp
import os
import signal

from .worker import main as worker_main


def main() -> None:
    worker_count = max(1, int(os.getenv("MATHEMATICS_WORKERS", "1")))
    if worker_count == 1:
        worker_main()
        return
    context = mp.get_context("spawn")
    processes = [
        context.Process(target=worker_main, name=f"mathematics-{index + 1}")
        for index in range(worker_count)
    ]
    for process in processes:
        process.start()

    stopping = False

    def stop(_signum: int, _frame: object) -> None:
        nonlocal stopping
        if stopping:
            return
        stopping = True
        for process in processes:
            if process.is_alive():
                process.terminate()

    signal.signal(signal.SIGTERM, stop)
    signal.signal(signal.SIGINT, stop)
    for process in processes:
        process.join()
    if any(process.exitcode not in (0, -signal.SIGTERM) for process in processes):
        raise SystemExit(1)


if __name__ == "__main__":
    main()
