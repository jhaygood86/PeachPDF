"""Renders every page of every PDF in a folder with PDFium (through pypdfium2) and writes <name>.<page>.png for each page.

This is the oracle for corpora that ship no reference images (the pdf.js regression PDFs): the runner compares PeachDrawing.PDF's
rendering of each page with it. One pixel per point, white background, annotations drawn, as PDFium's own test tool does by default.

For each PDF the script leaves one of:
  <name>.done          the page count, written when every page has been tried (so an interrupted run resumes after it)
  <name>.error         the PDF could not be opened, took longer than --timeout, or crashed PDFium
and, per page that could not be rendered, <name>.<page>.error. The runner skips what has an error file.

A PDF that hangs PDFium (tiling patterns with tiny steps take it minutes) must not hold a worker: each worker is watched, and one that has
spent longer than --timeout on a single file is killed, the file is recorded as an error, and a fresh worker takes its place.

    python3 -I render_pdfium.py <pdf folder> <output folder> [--max-bytes N] [--max-pages N] [--jobs N] [--timeout SECONDS]
"""

import argparse
import multiprocessing
import os
import queue
import sys
import time


def write_error(out_dir, name, text):
    with open(os.path.join(out_dir, name + ".error"), "w", encoding="utf-8") as f:
        f.write(text[:500])


def render_one(job):
    pdf_path, out_dir, max_pages = job
    name = os.path.splitext(os.path.basename(pdf_path))[0]
    try:
        import pypdfium2 as pdfium

        document = pdfium.PdfDocument(pdf_path)
    except Exception as e:  # noqa: BLE001 - a PDF that cannot be opened is recorded, not fatal
        write_error(out_dir, name, f"{type(e).__name__}: {e}")
        return 0, 1

    failed = 0
    try:
        count = len(document)
        limit = count if max_pages <= 0 else min(count, max_pages)
        for index in range(limit):
            try:
                page = document[index]
                try:
                    image = page.render(scale=1).to_pil().convert("RGB")
                finally:
                    page.close()
                image.save(os.path.join(out_dir, f"{name}.{index}.png"))
            except Exception as e:  # noqa: BLE001 - one bad page must not hide the rest
                failed += 1
                write_error(out_dir, f"{name}.{index}", f"{type(e).__name__}: {e}")
        with open(os.path.join(out_dir, name + ".done"), "w", encoding="utf-8") as f:
            f.write(str(limit))
        return limit, failed
    finally:
        document.close()


def worker(slot, jobs, results, state):
    """Takes jobs until the queue stays empty. state[2 * slot] is when the current job started (0 between jobs), state[2 * slot + 1] which it is."""
    while True:
        try:
            index, job = jobs.get(timeout=2)
        except queue.Empty:
            return
        state[2 * slot] = time.time()
        state[2 * slot + 1] = index
        pages, failed = render_one(job)
        state[2 * slot] = 0
        results.put((index, pages, failed))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("pdfs")
    parser.add_argument("out")
    parser.add_argument("--max-bytes", type=int, default=3_000_000)
    parser.add_argument("--max-pages", type=int, default=0, help="render at most this many pages of a file (default: all)")
    parser.add_argument("--jobs", type=int, default=os.cpu_count() or 1)
    parser.add_argument("--timeout", type=int, default=60, help="seconds one file may take before it is given up on (default 60)")
    args = parser.parse_args()

    os.makedirs(args.out, exist_ok=True)
    todo = []
    for entry in sorted(os.listdir(args.pdfs)):
        path = os.path.join(args.pdfs, entry)
        name = os.path.splitext(entry)[0]
        if not entry.lower().endswith(".pdf") or os.path.getsize(path) > args.max_bytes:
            continue
        if os.path.exists(os.path.join(args.out, name + ".done")) or os.path.exists(os.path.join(args.out, name + ".error")):
            continue  # an earlier run finished this file: the oracle is deterministic for a pinned PDFium
        todo.append((path, args.out, args.max_pages))

    if not todo:
        print("nothing left to render", file=sys.stderr)
        return

    context = multiprocessing.get_context("spawn")
    jobs = context.Queue()
    results = context.Queue()
    for index, job in enumerate(todo):
        jobs.put((index, job))
    count = max(1, min(args.jobs, len(todo)))
    state = context.Array("d", 2 * count, lock=False)

    def start(slot):
        process = context.Process(target=worker, args=(slot, jobs, results, state), daemon=True)
        process.start()
        return process

    processes = [start(slot) for slot in range(count)]
    finished = set()
    idle = 0
    pages = failed_pages = unreadable = 0

    def record(index, error_text):
        nonlocal unreadable
        if index in finished:
            return
        finished.add(index)
        name = os.path.splitext(os.path.basename(todo[index][0]))[0]
        write_error(args.out, name, error_text)
        unreadable += 1

    while len(finished) < len(todo):
        try:
            index, done_pages, failed = results.get(timeout=0.5)
            if index not in finished:
                finished.add(index)
                pages += done_pages
                failed_pages += failed
                unreadable += done_pages == 0
        except queue.Empty:
            pass

        now = time.time()
        for slot, process in enumerate(processes):
            started, index = state[2 * slot], int(state[2 * slot + 1])
            if started and now - started > args.timeout and process.is_alive():
                process.kill()
                process.join()
                record(index, f"timeout after {args.timeout}s")
                state[2 * slot] = 0
                processes[slot] = start(slot)
            elif not process.is_alive():
                if started:
                    # The worker died in the middle of a file (PDFium crashed): that file is an error, and the slot starts over.
                    record(index, "PDFium crashed")
                    state[2 * slot] = 0
                if len(finished) < len(todo) and not jobs.empty():
                    processes[slot] = start(slot)
        if all(not p.is_alive() for p in processes) and jobs.empty() and results.empty() and len(finished) < len(todo):
            # Nothing is running and nothing is queued; give a result still in flight a moment, then whatever is unaccounted for was lost with a dead worker.
            idle += 1
            if idle >= 6:
                for index in range(len(todo)):
                    record(index, "no result")
        else:
            idle = 0

    for process in processes:
        if process.is_alive():
            process.kill()
    print(f"{len(todo)} PDFs: {pages} pages rendered, {failed_pages} pages and {unreadable} files could not be", file=sys.stderr)


if __name__ == "__main__":
    main()
