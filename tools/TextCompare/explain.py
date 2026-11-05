#!/usr/bin/env python3
"""Tag the worst files of a compare.py report with what Apache Tika reads that a document-text extractor does not.

    explain.py <report.json> <corpus dir> [--n 20]

For each of the worst word-processing files by similarity to Tika, lists the features of the package that account for text
Tika adds: embedded objects and pictures (Tika extracts embedded documents and the text of metafiles), an altChunk (imported
HTML or RTF), numbered paragraphs (Tika writes list labels; extraction does so only when asked), diagrams. Features are found
by looking at part names and a few element names; nothing here reads the text.
"""
import json
import re
import sys
import zipfile
from pathlib import Path


def features(path):
    tags = []
    try:
        z = zipfile.ZipFile(path)
    except zipfile.BadZipFile:
        return ["not-a-zip (truncated)"]
    names = z.namelist()
    if any(n.startswith("word/embeddings/") for n in names):
        tags.append("embedded-objects")
    if any(re.search(r"\.(emf|wmf)$", n, re.I) for n in names):
        tags.append("metafile-pictures")
    if any(n.startswith("word/diagrams/") for n in names):
        tags.append("diagram")
    try:
        doc = z.read("word/document.xml").decode("utf8", "replace")
    except KeyError:
        return tags + ["no-document-part"]
    if "altChunk" in doc:
        tags.append("altChunk")
    if "<w:numPr>" in doc:
        tags.append("numbered-paragraphs")
    if "<w:ins " in doc or "<w:del " in doc:
        tags.append("tracked-changes")
    if "txbxContent" in doc:
        tags.append("text-boxes")
    if "<w:fldChar" in doc or "<w:fldSimple" in doc:
        tags.append("fields")
    return tags or ["none-found"]


def main():
    report = json.load(open(sys.argv[1]))
    corpus = Path(sys.argv[2])
    n = int(sys.argv[sys.argv.index("--n") + 1]) if "--n" in sys.argv else 20
    for w in report["worstWordVsTika"][:n]:
        matches = list(corpus.glob(w["name"] + "*"))
        tags = features(matches[0]) if matches else ["file-missing"]
        print(f'{w["name"][:10]} ours={w["oursChars"]} tika={w["tikaChars"]} sim={w.get("vsTika", -1):.3f}  {", ".join(tags)}')


if __name__ == "__main__":
    main()
