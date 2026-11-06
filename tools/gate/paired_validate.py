#!/usr/bin/env python3
"""Paired schema validation: what our output breaks that the input did not.

    paired_validate.py --pairs LIST --schemas DIR... [--procs N] [--json FILE]

LIST is a text file, one `original<TAB>output` pair of paths per line. Each package is validated part by part against the loaded schemas
(ECMA-376 Transitional, OPC and the Microsoft extension schemas; markup compatibility applied first, see validate_ooxml.py). A part that
is invalid in the output but valid, or absent, in the input is "newly invalid"; a part invalid in both is the input's own defect and is
counted apart. Prints one JSON object: pairs, parts checked in the outputs, outputs with every part valid, parts invalid in the output
(total), of those the input already had, newly invalid parts (count and by part-name shape), and the first error text of the most common
newly invalid shapes.
"""
import argparse
import collections
import json
import multiprocessing as mp
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "validate"))
import validate_ooxml as v  # noqa: E402

XML_SPACE = "{http://www.w3.org/XML/1998/namespace}space': The attribute"
_S = None


def init(dirs):
    global _S
    _S = v.SchemaSet(dirs)


def parts(path):
    res = {}
    n = 0
    try:
        for name, data in v.package_parts(path):
            r = v.validate_part(name, data, _S, _S.namespaces)
            n += 1
            # Office writes xml:space="preserve" on text elements whose schema type declares no such attribute, and so do we; not counted.
            errs = [e for e in r["errors"] if XML_SPACE not in str(e)] if r["status"] == "invalid" else []
            if errs:
                res[name] = errs[0]
    except Exception as e:  # unreadable package: reported, never stops the run
        return None, 0, repr(e)[:120]
    return res, n, None


def one(pair):
    a, b = pair
    ia, _, ea = parts(a)
    ob, nb, eb = parts(b)
    return {"in": os.path.basename(a), "out": os.path.basename(b), "inErr": ea, "outErr": eb,
            "outInvalid": ob, "inInvalid": ia, "outParts": nb}


def shape(name):
    name = re.sub(r"\d+", "N", name)
    return name


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--pairs", required=True)
    ap.add_argument("--schemas", action="append", required=True)
    ap.add_argument("--procs", type=int, default=3)
    ap.add_argument("--json")
    a = ap.parse_args()
    pairs = [l.rstrip("\n").split("\t") for l in open(a.pairs) if "\t" in l]
    with mp.Pool(a.procs, initializer=init, initargs=(a.schemas,)) as pool:
        rows = pool.map(one, pairs, chunksize=4)
    checked = [r for r in rows if r["outInvalid"] is not None]
    all_valid = sum(1 for r in checked if not r["outInvalid"])
    inv_total = sum(len(r["outInvalid"]) for r in checked)
    inherited = new = 0
    newshape = collections.Counter()
    ex = {}
    files_new = 0
    for r in checked:
        ia = r["inInvalid"] or {}
        hit = False
        for name, err in r["outInvalid"].items():
            if name in ia:
                inherited += 1
            else:
                new += 1
                hit = True
                s = shape(name)
                newshape[s] += 1
                ex.setdefault(s, (r["out"], str(err)[:260]))
        files_new += hit
    res = {
        "pairs": len(pairs), "outputsChecked": len(checked), "unreadable": len(rows) - len(checked),
        "partsChecked": sum(r["outParts"] for r in checked), "outputsAllValid": all_valid,
        "invalidPartsInOutputs": inv_total, "inheritedFromInput": inherited,
        "newlyInvalidParts": new, "outputsWithNewlyInvalid": files_new,
        "newlyInvalidShapes": [{"part": k, "count": c, "example": ex[k]} for k, c in newshape.most_common(8)],
    }
    print(json.dumps(res))
    if a.json:
        json.dump(res, open(a.json, "w"), indent=1)


main()
