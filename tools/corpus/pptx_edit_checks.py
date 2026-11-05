#!/usr/bin/env python3
"""Checks over the output of `PptxProbe sweep`: schema validity of edited presentations and whether LibreOffice opens them.

  pptx_edit_checks.py validate SWEEPDIR CORPUSDIR [--schemas DIR]... [--ops text,cell,dup,add,move,delete]
      For every file in the sweep, validates the original and each edited output (edited/<id>.<op>.pptx) and counts the parts
      whose error count rose, and the added parts that have any error. A non-zero "newInvalidParts" is a finding.
  pptx_edit_checks.py lo SWEEPDIR CORPUSDIR [--sample 100] [--seed 1]
      Converts the original and the edited outputs of a deterministic sample to PDF with headless LibreOffice (ONEDOCUMENT_SOFFICE)
      and reports the ones that convert as the original does not.
"""
import argparse, glob, json, os, random, shutil, subprocess, sys, tempfile, time

HERE = os.path.dirname(os.path.abspath(__file__))


def rows(sweep):
    for p in sorted(glob.glob(os.path.join(sweep, "sweep-*.jsonl"))):
        for line in open(p):
            r = json.loads(line)
            if r.get("kind") == "file" and r.get("status") in ("ok", "untouched-diff"):
                yield r


def edited_files(a, r):
    for op in a.ops.split(","):
        path = os.path.join(a.sweep, "edited", "%s.%s.pptx" % (r["id"], op))
        if os.path.exists(path):
            yield op, path


def validate(a):
    sys.path.insert(0, os.path.join(HERE, "..", "validate"))
    import validate_ooxml as v
    dirs = a.schemas or [d for d in os.environ.get("ONEDOCUMENT_XSD", "").split(os.pathsep) if d]
    s = v.SchemaSet(dirs)
    understood = s.namespaces

    msgs = {}

    def errs(path):
        out = {}
        msgs.clear()
        for name, data in v.package_parts(path):
            r = v.validate_part(name, data, s, understood)
            out[name] = len(r["errors"]) if r["status"] == "invalid" else 0
            msgs[name] = sorted(e["message"] for e in r["errors"]) if r["status"] == "invalid" else []
        return out

    inherited = files = outputs = new = newParts = origInvalid = editedInvalid = skipped = 0
    detail = []
    t0 = time.time()
    for r in rows(a.sweep):
        if time.time() - t0 > a.max_minutes * 60:
            detail.append({"note": "time budget reached"})
            break
        orig = os.path.join(a.corpus, r["file"])
        if not os.path.exists(orig) or os.path.getsize(orig) > a.max_bytes:
            skipped += 1
            continue
        try:
            eo = errs(orig)
            origMsgs = {tuple(m) for m in msgs.values() if m}
        except Exception as e:
            detail.append({"file": r["file"], "error": str(e)[:200]})
            continue
        files += 1
        origInvalid += sum(1 for n in eo.values() if n)
        for op, path in edited_files(a, r):
            outputs += 1
            try:
                ee = errs(path)
                editedMsgs = dict(msgs)
            except Exception as e:
                detail.append({"file": r["file"], "op": op, "error": str(e)[:200]})
                continue
            editedInvalid += sum(1 for n in ee.values() if n)
            for name, n in ee.items():
                if name not in eo and n and tuple(editedMsgs[name]) in origMsgs:
                    inherited += 1  # a copy of a source part that already had exactly these errors
                elif name not in eo and n:
                    newParts += 1
                    detail.append({"file": r["file"], "op": op, "addedPart": name, "errors": n})
                elif name in eo and n > eo[name]:
                    new += 1
                    detail.append({"file": r["file"], "op": op, "part": name, "orig": eo[name], "edited": n})
    print(json.dumps({"files": files, "outputs": outputs, "skippedLarge": skipped, "originalInvalidParts": origInvalid, "editedInvalidParts": editedInvalid,
                      "newInvalidParts": new, "addedPartsInvalid": newParts, "addedPartsInheritedErrors": inherited, "detail": detail[:30]}))
    return 0


def to_pdf(path, work):
    soffice = os.environ["ONEDOCUMENT_SOFFICE"]
    os.makedirs(work, exist_ok=True)
    local = os.path.join(work, "f.pptx")
    shutil.copyfile(path, local)
    p = subprocess.Popen([soffice, "--convert-to", "pdf", "--outdir", work, local], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, start_new_session=True)
    try:
        p.wait(timeout=90)
    except subprocess.TimeoutExpired:
        os.killpg(p.pid, 9)  # soffice leaves its worker process behind otherwise
        p.wait()
    pdf = os.path.join(work, "f.pdf")
    return os.path.getsize(pdf) if os.path.exists(pdf) else 0


def lo(a):
    cands = list(rows(a.sweep))
    random.Random(a.seed).shuffle(cands)
    ok = failedAsOriginal = fail = 0
    detail = []
    t0 = time.time()
    for r in cands:
        if ok + failedAsOriginal + fail >= a.sample or time.time() - t0 > a.max_minutes * 60:
            break
        orig = os.path.join(a.corpus, r["file"])
        if not os.path.exists(orig) or os.path.getsize(orig) > a.max_bytes:
            continue
        work = tempfile.mkdtemp(prefix="pptxlo-", dir=os.environ.get("TMPDIR"))
        try:
            original_ok = to_pdf(orig, os.path.join(work, "o")) > 0
            for op, path in edited_files(a, r):
                edited_ok = to_pdf(path, os.path.join(work, op)) > 0
                if edited_ok:
                    ok += 1
                elif not original_ok:
                    failedAsOriginal += 1
                else:
                    fail += 1
                    detail.append({"file": r["file"], "op": op, "error": "edited file does not convert"})
        finally:
            shutil.rmtree(work, ignore_errors=True)
    print(json.dumps({"converted": ok, "failedAsOriginalDid": failedAsOriginal, "failed": fail, "detail": detail[:30]}))
    return 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("mode", choices=["validate", "lo"])
    ap.add_argument("sweep")
    ap.add_argument("corpus")
    ap.add_argument("--schemas", action="append")
    ap.add_argument("--ops", default="text,cell,dup,add,move,delete")
    ap.add_argument("--sample", type=int, default=100, help="lo: edited outputs to convert (counted per output)")
    ap.add_argument("--seed", type=int, default=1)
    ap.add_argument("--max-minutes", type=float, default=20)
    ap.add_argument("--max-bytes", type=int, default=8 << 20)
    a = ap.parse_args()
    return {"validate": validate, "lo": lo}[a.mode](a)


if __name__ == "__main__":
    sys.exit(main())
