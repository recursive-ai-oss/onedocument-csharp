#!/usr/bin/env python3
"""Checks over the output of `XlsxProbe sweep`: schema validity of rewritten sheets and LibreOffice value agreement.

  xlsx_edit_checks.py validate SWEEPDIR CORPUSDIR [--schemas DIR]...
      For every file in the sweep, validates the original and the forced-rewrite output (edited/<id>.xlsx) and counts the
      parts whose error count rose. Prints one JSON summary; a non-zero "newInvalidParts" is a finding.
  xlsx_edit_checks.py lo SWEEPDIR CORPUSDIR [--sample 200] [--seed 1]
      Converts the original and the rewrite of a deterministic sample to CSV (every sheet) with headless LibreOffice
      (ONEDOCUMENT_SOFFICE) and compares the CSV files.
"""
import argparse, glob, json, os, random, shutil, subprocess, sys, tempfile, time

XML_SPACE = "{http://www.w3.org/XML/1998/namespace}space': The attribute"
HERE = os.path.dirname(os.path.abspath(__file__))


def rows(sweep):
    for p in sorted(glob.glob(os.path.join(sweep, "sweep-*.jsonl"))):
        for line in open(p):
            r = json.loads(line)
            if r.get("kind") == "file" and r.get("status") == "ok":
                yield r


def validate(a):
    sys.path.insert(0, os.path.join(HERE, "..", "validate"))
    import validate_ooxml as v
    dirs = a.schemas or [d for d in os.environ.get("ONEDOCUMENT_XSD", "").split(os.pathsep) if d]
    s = v.SchemaSet(dirs)
    understood = s.namespaces

    def errs(path):
        out = {}
        for name, data in v.package_parts(path):
            r = v.validate_part(name, data, s, understood)
            # Office writes xml:space="preserve" on the text elements of strings, though the schema's ST_Xstring element declares no
            # such attribute; the library does the same for text with edge spaces, so those errors are not counted (here or in originals).
            out[name] = sum(1 for e in r["errors"] if XML_SPACE not in str(e)) if r["status"] == "invalid" else 0
        return out

    files = new = origInvalid = editedInvalid = skipped = 0
    detail = []
    t0 = time.time()
    cands = list(rows(a.sweep))
    if a.sample_validate:
        random.Random(a.seed).shuffle(cands)
    for r in cands:
        if a.sample_validate and files >= a.sample_validate:
            break
        if time.time() - t0 > a.max_minutes * 60:
            detail.append({"note": "time budget reached"})
            break
        edited = os.path.join(a.sweep, getattr(a, "edited", "edited"), r["id"] + ".xlsx")
        orig = os.path.join(a.corpus, r["file"])
        if not os.path.exists(edited) or not os.path.exists(orig):
            continue
        if os.path.getsize(orig) > a.max_bytes:
            skipped += 1
            continue
        files += 1
        try:
            eo, ee = errs(orig), errs(edited)
        except Exception as e:
            detail.append({"file": r["file"], "error": str(e)[:200]})
            continue
        origInvalid += sum(1 for n in eo.values() if n)
        editedInvalid += sum(1 for n in ee.values() if n)
        for name, n in ee.items():
            if n > eo.get(name, 0):
                new += 1
                detail.append({"file": r["file"], "part": name, "orig": eo.get(name, 0), "edited": n})
    print(json.dumps({"files": files, "skippedLarge": skipped, "originalInvalidParts": origInvalid, "editedInvalidParts": editedInvalid, "newInvalidParts": new, "detail": detail[:30]}))
    return 0


def errors(a):
    """Prints the schema errors of one corpus file and of its rewrite, for the parts that have any."""
    sys.path.insert(0, os.path.join(HERE, "..", "validate"))
    import validate_ooxml as v
    s = v.SchemaSet([d for d in os.environ.get("ONEDOCUMENT_XSD", "").split(os.pathsep) if d])
    for r in rows(a.sweep):
        if r["file"] != a.file:
            continue
        for label, path in (("original", os.path.join(a.corpus, r["file"])), ("rewrite", os.path.join(a.sweep, getattr(a, "edited", "edited"), r["id"] + ".xlsx"))):
            for name, data in v.package_parts(path):
                res = v.validate_part(name, data, s, s.namespaces)
                if res["status"] == "invalid":
                    print(label, name, len(res["errors"]))
                    for e in res["errors"][:5]:
                        print("   ", e)
    return 0


def lo_csv(path, work):
    soffice = os.environ["ONEDOCUMENT_SOFFICE"]
    os.makedirs(work, exist_ok=True)
    local = os.path.join(work, "f.xlsx")  # same name for both sides so the CSV names (f-<sheet>.csv) line up
    shutil.copyfile(path, local)
    out = os.path.join(work, "csv")
    # Filter options: separator 44, quote 34, UTF-8, first line 1, raw values (not as shown), all sheets (-1).
    flt = 'csv:Text - txt - csv (StarCalc):44,34,76,1,,0,false,false,false,false,false,-1'
    p = subprocess.Popen([soffice, "--convert-to", flt, "--outdir", out, local], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, start_new_session=True)
    try:
        p.wait(timeout=60)
    except subprocess.TimeoutExpired:
        os.killpg(p.pid, 9)  # soffice leaves its worker process behind otherwise
        p.wait()
    return {os.path.basename(f): open(f, "rb").read() for f in sorted(glob.glob(os.path.join(out, "*.csv")))}


def lo(a):
    cands = list(rows(a.sweep))
    random.Random(a.seed).shuffle(cands)
    ok = diff = fail = 0
    detail = []
    t0 = time.time()
    for r in cands:
        if ok + diff + fail >= a.sample or time.time() - t0 > a.max_minutes * 60:
            break
        edited = os.path.join(a.sweep, getattr(a, "edited", "edited"), r["id"] + ".xlsx")
        orig = os.path.join(a.corpus, r["file"])
        if not (os.path.exists(edited) and os.path.exists(orig)) or os.path.getsize(orig) > min(a.max_bytes, 2 << 20):
            continue
        work = tempfile.mkdtemp(prefix="xlsxlo-", dir=os.environ.get("TMPDIR"))
        try:
            co = lo_csv(orig, os.path.join(work, "o"))
            ce = lo_csv(edited, os.path.join(work, "e"))
        except Exception as e:
            fail += 1
            detail.append({"file": r["file"], "error": str(e)[:100]})
            continue
        finally:
            shutil.rmtree(work, ignore_errors=True)
        if not co:
            fail += 1  # the original itself did not convert: no comparison possible
            detail.append({"file": r["file"], "error": "original did not convert"})
        elif getattr(a, "open_only", False) and ce:
            ok += 1  # the edit adds cells, so only "the edited file opens and converts" is checked
        elif co == ce:
            ok += 1
        else:
            diff += 1
            detail.append({"file": r["file"], "error": "csv differs", "sheetsOrig": len(co), "sheetsEdited": len(ce)})
    print(json.dumps({"sample": ok + diff + fail, "same": ok, "different": diff, "unconvertible": fail, "detail": detail[:30]}))
    return 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("mode", choices=["validate", "lo", "errors"])
    ap.add_argument("sweep")
    ap.add_argument("corpus")
    ap.add_argument("--schemas", action="append")
    ap.add_argument("--file", help="errors: the file name as listed in the sweep")
    ap.add_argument("--edited", default="edited", help="the sweep sub-directory that holds the edited files (the feature sweep writes ed-style, ed-cf, ...)")
    ap.add_argument("--sample", type=int, default=200)
    ap.add_argument("--seed", type=int, default=1)
    ap.add_argument("--open-only", action="store_true", help="lo: count a file as fine when the edited file converts, without comparing values")
    ap.add_argument("--sample-validate", type=int, default=0, help="validate: check a deterministic sample of this many files instead of all")
    ap.add_argument("--max-minutes", type=float, default=20, help="stop starting new files after this long and report what was checked")
    ap.add_argument("--max-bytes", type=int, default=6 << 20, help="skip files whose original is larger (schema validation and LibreOffice are slow on big sheets)")
    a = ap.parse_args()
    return {"validate": validate, "lo": lo, "errors": errors}[a.mode](a)


if __name__ == "__main__":
    sys.exit(main())
