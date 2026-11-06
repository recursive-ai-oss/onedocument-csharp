#!/usr/bin/env python3
"""Rewrites the public-API listings (src/<Project>/PublicAPI.Shipped.txt) from the code.

Each library assembly keeps a listing of its public surface, checked on every build by
Microsoft.CodeAnalysis.PublicApiAnalyzers: a public type or member that is added, removed or changed
without a matching listing edit fails the build (RS0016, RS0017, RS0036 and friends).

Normal workflow after an intended API change: add the lines the build reports to
PublicAPI.Unshipped.txt (the IDE quick fix does it; so does `--unshipped` here). At a release, the
Unshipped lines move into Shipped (`--release`).

This script does the two bulk jobs:
  update-public-api.py --baseline   rebuild every Shipped listing from the code (first release)
  update-public-api.py --unshipped  put every newly public symbol into PublicAPI.Unshipped.txt
  update-public-api.py --release    append Unshipped to Shipped and empty Unshipped

Run it from the repository root with the .NET SDK on PATH. Each build starts from the listings as
they are, so the reported symbols are exactly the difference between the code and the listing.
"""
import os
import re
import subprocess
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
PROJECTS = [
    "OneDocument",
    "OneDocument.Documents",
    "OneDocument.Spreadsheets",
    "OneDocument.Presentations",
    "OneDocument.Schema",
    "OneDocument.Rendering",
]
HEADER = "#nullable enable\n"
MESSAGE = re.compile(r"^(?P<file>.+?)\(\d+,\d+\): (?:warning|error) RS0016: Symbol '(?P<symbol>.*)' is not part of the declared public API")


def listing(project, name):
    return os.path.join(ROOT, "src", project, name)


def read_lines(path):
    if not os.path.exists(path):
        return []
    with open(path, encoding="utf-8") as f:
        return [l.rstrip("\n") for l in f if l.strip() and l.strip() != "#nullable enable"]


def write_lines(path, lines):
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(HEADER)
        for l in sorted(set(lines), key=lambda s: s):
            f.write(l + "\n")


def build_and_collect(log):
    cmd = [
        "dotnet", "build", os.path.join(ROOT, "src", "OneDocument.Rendering", "OneDocument.Rendering.csproj"),
        "-c", "Release", "--no-incremental", "-m:2", "-p:TreatWarningsAsErrors=false", "-v:q", "-nologo",
        f"-flp:logfile={log};warningsonly;verbosity=quiet",
    ]
    subprocess.run(cmd, cwd=ROOT, check=False, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    found = {p: [] for p in PROJECTS}
    with open(log, encoding="utf-8") as f:
        for line in f:
            m = MESSAGE.match(line)
            if not m:
                continue
            rel = os.path.relpath(m.group("file"), os.path.join(ROOT, "src"))
            project = rel.split(os.sep)[0]
            if project in found:
                found[project].append(m.group("symbol"))
    return found


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else ""
    if mode == "--release":
        for p in PROJECTS:
            shipped = read_lines(listing(p, "PublicAPI.Shipped.txt")) + read_lines(listing(p, "PublicAPI.Unshipped.txt"))
            write_lines(listing(p, "PublicAPI.Shipped.txt"), shipped)
            write_lines(listing(p, "PublicAPI.Unshipped.txt"), [])
        return 0
    if mode not in ("--baseline", "--unshipped"):
        print(__doc__)
        return 2
    if mode == "--baseline":
        for p in PROJECTS:
            write_lines(listing(p, "PublicAPI.Shipped.txt"), [])
            write_lines(listing(p, "PublicAPI.Unshipped.txt"), [])
    log = os.environ.get("TMPDIR", "/tmp") + "/public-api-build.log"
    found = build_and_collect(log)
    for p in PROJECTS:
        if mode == "--baseline":
            write_lines(listing(p, "PublicAPI.Shipped.txt"), found[p])
        else:
            unshipped = read_lines(listing(p, "PublicAPI.Unshipped.txt")) + found[p]
            write_lines(listing(p, "PublicAPI.Unshipped.txt"), unshipped)
        print(f"{p}: {len(set(found[p]))} symbols")
    return 0


if __name__ == "__main__":
    sys.exit(main())
