#!/usr/bin/env python3
"""Rewrites the code blocks of the documentation from the sample's source.

A Markdown block preceded by `<!-- snippet: name -->` gets the region marked in samples/GettingStarted with
`// <snippet:name>` ... `// </snippet:name>` (or `@* <snippet:name> *@` in Razor). The test
DocumentationSnippetsTests fails when a block and its region differ; this script makes them equal again.

Usage: python3 scripts/update-doc-snippets.py
"""
import pathlib
import re
import sys
import textwrap

ROOT = pathlib.Path(__file__).resolve().parent.parent
SOURCE = re.compile(
    r"(?://|@\*)\s*<snippet:(?P<name>[\w-]+)>(?:\s*\*@)?[^\n]*\n(?P<code>.*?)\n[^\n]*(?://|@\*)\s*</snippet:(?P=name)>",
    re.S)
# The code never crosses a fence, so an empty block does not swallow the next one.
BLOCK = re.compile(r"(?P<head><!--\s*snippet:\s*(?P<name>[\w-]+)\s*-->\s*\n```\w*\n)(?P<code>(?:(?!```).)*?)\n?```", re.S)


def regions():
    found = {}
    for path in (ROOT / "samples" / "GettingStarted").rglob("*"):
        if path.suffix not in (".cs", ".razor") or "bin" in path.parts or "obj" in path.parts:
            continue
        for m in SOURCE.finditer(path.read_text(encoding="utf-8")):
            found[m.group("name")] = textwrap.dedent(m.group("code")).strip("\n")
    return found


def main():
    code = regions()
    missing = []
    docs = list((ROOT / "docs").rglob("*.md")) + [ROOT / "README.md"]
    for doc in docs:
        text = doc.read_text(encoding="utf-8")

        def replace(m):
            name = m.group("name")
            if name not in code:
                missing.append(f"{doc.relative_to(ROOT)}: {name}")
                return m.group(0)
            return m.group("head") + code[name] + "\n```"

        updated = BLOCK.sub(replace, text)
        if updated != text:
            doc.write_text(updated, encoding="utf-8")
            print("updated", doc.relative_to(ROOT))
    if missing:
        print("no region for:", *missing, sep="\n  ", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
