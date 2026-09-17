#!/usr/bin/env python3
"""Shrink a page that reproduces a rendering bug down to the part that causes it.

Why this exists
---------------
A real document that renders wrong is usually tens of kilobytes of markup and a
stylesheet nobody wrote for this purpose, and the interesting part is a handful of
elements or one CSS rule. Deleting by hand is slow and tends to break the structure,
which changes the layout and loses the repro.

This bisects instead. It keeps subsets of the children at a chosen nesting level, or
subsets of the top-level CSS rules, always emitting well-formed HTML, and asks a
command of your choosing whether the result still reproduces.

The predicate is any command that exits 0 when the reduction still reproduces. It
receives the candidate file's path as its last argument, so a one-liner is usually
enough:

    python3 tools/reduce-repro.py page.html --css \
        --test 'peachpdf render --input {} --output /tmp/o.pdf && ! pdftotext /tmp/o.pdf - | grep -q "EXPECTED TEXT"'

Listing, to choose a level before bisecting:

    python3 tools/reduce-repro.py page.html --list            # body's children
    python3 tools/reduce-repro.py page.html --path 0/0 --list # deeper
    python3 tools/reduce-repro.py page.html --css --list      # the stylesheet's rules

`{}` in the test command is replaced with the candidate path; without it the path is
appended.
"""
from __future__ import annotations

import argparse
import re
import subprocess
import sys
import tempfile
from pathlib import Path

# Elements with no closing tag, so the depth counter does not go negative on them.
VOID = {
    "area", "base", "br", "col", "embed", "hr", "img", "input",
    "link", "meta", "param", "source", "track", "wbr",
}


def children(markup: str) -> list[str]:
    """Split markup into its top-level elements, keeping each one whole."""
    parts: list[str] = []
    depth, buffer = 0, ""

    for token in re.split(r"(<[^>]+>)", markup):
        if not token:
            continue
        buffer += token
        if token.startswith("</"):
            depth -= 1
            if depth <= 0:
                parts.append(buffer)
                buffer, depth = "", 0
        elif token.startswith("<") and not token.startswith("<!"):
            name = re.match(r"<\s*([a-zA-Z0-9]+)", token)
            if name and name.group(1).lower() not in VOID and not token.rstrip().endswith("/>"):
                depth += 1

    if buffer.strip():
        parts.append(buffer)
    return parts


def split_element(markup: str):
    """(open tag, inner markup, close tag) of the single element in `markup`."""
    match = re.match(r"(\s*<\s*([a-zA-Z0-9]+)[^>]*>)(.*)(</\s*\2\s*>\s*)$", markup, re.S)
    if not match:
        raise SystemExit("cannot descend into that child: it is not a single element")
    return match.group(1), match.group(3), match.group(4)


def body_of(html: str):
    opening = re.search(r"<body[^>]*>", html)
    if not opening:
        raise SystemExit("no <body> in that file")
    close = html.rfind("</body>")
    close = close if close >= 0 else len(html)
    return html[: opening.end()], html[opening.end(): close], html[close:]


def descend(html: str, path: str):
    """Walk `path` (slash-separated child indices) and return the frames around it."""
    head, inner, tail = body_of(html)
    frames = []

    for step in [p for p in path.split("/") if p]:
        kids = children(inner)
        index = int(step)
        if index >= len(kids):
            raise SystemExit(f"no child {index} at that level (there are {len(kids)})")
        opening, deeper, closing = split_element(kids[index])
        frames.append(("".join(kids[:index]), opening, closing, "".join(kids[index + 1:])))
        inner = deeper

    return head, frames, inner, tail


def rebuild(head, frames, inner, tail, keep: list[str]) -> str:
    markup = "".join(keep)
    for before, opening, closing, after in reversed(frames):
        markup = before + opening + markup + closing + after
    return head + markup + tail


def css_rules(stylesheet: str) -> list[str]:
    rules, depth, buffer = [], 0, ""
    for character in stylesheet:
        buffer += character
        if character == "{":
            depth += 1
        elif character == "}":
            depth -= 1
            if depth == 0:
                rules.append(buffer)
                buffer = ""
    if buffer.strip():
        rules.append(buffer)
    return rules


def with_css(html: str, keep: list[str]) -> str:
    style = re.search(r"(<style[^>]*>)(.*?)(</style>)", html, re.S)
    if not style:
        raise SystemExit("no <style> block in that file")
    return html[: style.start(2)] + "".join(keep) + html[style.end(2):]


def reproduces(command: str, html: str, suffix=".html") -> bool:
    with tempfile.NamedTemporaryFile("w", suffix=suffix, delete=False) as handle:
        handle.write(html)
        candidate = handle.name
    try:
        line = command.replace("{}", candidate) if "{}" in command else f"{command} {candidate}"
        return subprocess.run(line, shell=True, capture_output=True).returncode == 0
    finally:
        Path(candidate).unlink(missing_ok=True)


def bisect(items: list[str], build, test) -> list[str]:
    """Delta-debug `items` down to a minimal subset that still reproduces.

    Halves first, then quarters, and so on: the same ddmin shape a test-case reducer
    uses, which matters because the trigger is often two items that only fail together.
    """
    granularity = 2
    while len(items) >= 2:
        chunk = max(1, len(items) // granularity)
        chunks = [items[i:i + chunk] for i in range(0, len(items), chunk)]
        reduced = False

        for piece in chunks:
            complement = [i for i in items if i not in piece]
            if complement and test(build(complement)):
                items, granularity = complement, max(granularity - 1, 2)
                reduced = True
                break

        if not reduced:
            if granularity >= len(items):
                break
            granularity = min(len(items), granularity * 2)

    return items


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("html")
    parser.add_argument("--path", default="", help="slash-separated child indices, e.g. 0/0")
    parser.add_argument("--css", action="store_true", help="reduce the stylesheet instead of the markup")
    parser.add_argument("--list", action="store_true", help="list what is at that level and stop")
    parser.add_argument("--test", help="command that exits 0 when the reduction still reproduces")
    parser.add_argument("--out", help="write the reduced document here")
    args = parser.parse_args()

    html = Path(args.html).read_text()

    if args.css:
        rules = css_rules(re.search(r"<style[^>]*>(.*?)</style>", html, re.S).group(1))
        if args.list:
            for index, rule in enumerate(rules):
                print(f"  [{index}] {rule.split('{')[0].strip()[:70]}")
            return
        build = lambda keep: with_css(html, keep)
        items = rules
    else:
        head, frames, inner, tail = descend(html, args.path)
        kids = children(inner)
        if args.list:
            for index, kid in enumerate(kids):
                tag = re.match(r"\s*<\s*([a-zA-Z0-9]+)([^>]*)", kid)
                opening = f"<{tag.group(1)}{(tag.group(2) or '')[:60]}>" if tag else kid[:40]
                print(f"  [{index}] {opening}  ({len(kid)} bytes)")
            return
        build = lambda keep: rebuild(head, frames, inner, tail, keep)
        items = kids

    if not args.test:
        raise SystemExit("--test is required unless --list is given")

    if not reproduces(args.test, build(items)):
        raise SystemExit("the unreduced document does not satisfy --test; check the command")

    smallest = bisect(items, build, lambda candidate: reproduces(args.test, candidate))
    result = build(smallest)

    print(f"reduced from {len(items)} to {len(smallest)} "
          f"({len(html)} to {len(result)} bytes)")
    if args.out:
        Path(args.out).write_text(result)
        print(f"wrote {args.out}")
    else:
        print(result)


if __name__ == "__main__":
    main()
