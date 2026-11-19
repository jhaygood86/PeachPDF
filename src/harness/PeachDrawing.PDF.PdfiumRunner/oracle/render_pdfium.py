"""Renders every page of every PDF in a folder with PDFium (through pypdfium2) and writes <name>.<page>.png for each page.

This is the oracle for corpora that ship no reference images (the pdf.js regression PDFs): the runner compares PeachDrawing.PDF's
rendering of each page with it. One pixel per point, white background, annotations drawn, as PDFium's own test tool does by default.

For each PDF the script leaves one of:
  <name>.done          the page count, written when every page has been tried (so an interrupted run resumes after it)
  <name>.error         the PDF could not be opened (or the whole file timed out)
and, per page that could not be rendered, <name>.<page>.error. The runner skips what has an error file.

    python3 -I render_pdfium.py <pdf folder> <output folder> [--max-bytes N] [--max-pages N] [--jobs N]
"""

import argparse
import multiprocessing
import os
import sys

TIMEOUT_SECONDS = 900


def render_one(job):
    pdf_path, out_dir, max_pages = job
    name = os.path.splitext(os.path.basename(pdf_path))[0]
    try:
        import pypdfium2 as pdfium

        document = pdfium.PdfDocument(pdf_path)
    except Exception as e:  # noqa: BLE001 - a PDF that cannot be opened is recorded, not fatal
        with open(os.path.join(out_dir, name + ".error"), "w", encoding="utf-8") as f:
            f.write(f"{type(e).__name__}: {e}"[:500])
        return name, 0, 1

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
                with open(os.path.join(out_dir, f"{name}.{index}.error"), "w", encoding="utf-8") as f:
                    f.write(f"{type(e).__name__}: {e}"[:500])
        with open(os.path.join(out_dir, name + ".done"), "w", encoding="utf-8") as f:
            f.write(str(limit))
        return name, limit, failed
    finally:
        document.close()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("pdfs")
    parser.add_argument("out")
    parser.add_argument("--max-bytes", type=int, default=3_000_000)
    parser.add_argument("--max-pages", type=int, default=0, help="render at most this many pages of a file (default: all)")
    parser.add_argument("--jobs", type=int, default=os.cpu_count() or 1)
    args = parser.parse_args()

    os.makedirs(args.out, exist_ok=True)
    jobs = []
    for entry in sorted(os.listdir(args.pdfs)):
        path = os.path.join(args.pdfs, entry)
        name = os.path.splitext(entry)[0]
        if not entry.lower().endswith(".pdf") or os.path.getsize(path) > args.max_bytes:
            continue
        if os.path.exists(os.path.join(args.out, name + ".done")) or os.path.exists(os.path.join(args.out, name + ".error")):
            continue  # an earlier run finished this file: the oracle is deterministic for a pinned PDFium
        jobs.append((path, args.out, args.max_pages))

    files = pages = failed_pages = unreadable = 0
    # A hung PDF must not stop the run: each result is waited for with a limit, and the file is recorded as an error if it never returns.
    with multiprocessing.Pool(args.jobs, maxtasksperchild=10) as pool:
        pending = [(job, pool.apply_async(render_one, (job,))) for job in jobs]
        for job, result in pending:
            files += 1
            try:
                _, count, failed = result.get(timeout=TIMEOUT_SECONDS)
            except multiprocessing.TimeoutError:
                name = os.path.splitext(os.path.basename(job[0]))[0]
                with open(os.path.join(args.out, name + ".error"), "w", encoding="utf-8") as f:
                    f.write("timeout")
                unreadable += 1
                continue
            if count == 0:
                unreadable += 1
            pages += count
            failed_pages += failed
    print(f"{files} PDFs: {pages} pages rendered, {failed_pages} pages and {unreadable} files could not be", file=sys.stderr)


if __name__ == "__main__":
    main()
