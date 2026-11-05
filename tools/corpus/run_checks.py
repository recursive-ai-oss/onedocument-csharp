#!/usr/bin/env python3
"""Run a check over a directory of documents in parallel shards and collect JSON-lines results.

  run_checks.py CHECK --input PATH [--glob '*.docx'] [--shards 4] [--timeout 120]
                [--max-minutes 25] [--out DIR] [--limit N] [--cmd TEMPLATE] [--schemas DIR]...

CHECK:
  lo        convert each file to PDF with headless LibreOffice (one private profile per shard);
            status opened / failed / timeout, plus the PDF page count when it opened.
  validate  validate every XML part against the ECMA-376 schemas (see validate_ooxml.py).
  cmd       run --cmd per file ({in} = the file, {out} = a scratch output path, {dir} = a scratch
            directory); status ok / failed / timeout. When the command wrote {out} and the input is a
            package, RoundTrip (--roundtrip DLL) compares them part by part. Used to drive the
            library's open/save through its command-line tool or a test host.

Environment:
  ONEDOCUMENT_SOFFICE   the headless LibreOffice launcher (a script taking the soffice arguments)
  LO_PROFILE_ROOT     directory for the per-shard LibreOffice profiles (default: a folder in --out)
  ONEDOCUMENT_XSD       schema directories for `validate`, as with validate_ooxml.py

The run is resumable and bounded: files already present in <out>/results-*.jsonl are skipped, and
shards stop picking up new files after --max-minutes so a job fits a fixed time slot. Re-run the same
command to continue. <out>/summary.json counts statuses over everything collected so far.
Corpus files are only read; nothing is copied out of the input directory.
"""
import argparse, glob, hashlib, json, multiprocessing as mp, os, re, shutil, subprocess, sys, tempfile, time

HERE = os.path.dirname(os.path.abspath(__file__))


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""):
            h.update(b)
    return h.hexdigest()


def pdf_pages(path):
    try:
        from pypdf import PdfReader
        return len(PdfReader(path).pages)
    except Exception:
        data = open(path, "rb").read()
        m = re.findall(rb"/Type\s*/Pages\b[^>]*?/Count\s+(\d+)", data)
        return int(m[0]) if m else None


def run_group(cmd, timeout, **kw):
    """Run cmd in its own process group; kill the whole group on timeout (LibreOffice forks)."""
    p = subprocess.Popen(cmd, start_new_session=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, **kw)
    try:
        out, _ = p.communicate(timeout=timeout)
        return p.returncode, out.decode("utf-8", "replace"), False
    except subprocess.TimeoutExpired:
        os.killpg(p.pid, 9)
        p.communicate()
        return -9, "", True


def check_lo(path, ctx):
    soffice = os.environ.get("ONEDOCUMENT_SOFFICE")
    if not soffice:
        raise SystemExit("set ONEDOCUMENT_SOFFICE to the headless LibreOffice launcher")
    work = tempfile.mkdtemp(prefix="lo-", dir=ctx["scratch"])
    try:
        t0 = time.time()
        env = dict(os.environ, LO_PROFILE=ctx["profile"])
        rc, out, timed_out = run_group([soffice, "--convert-to", "pdf", "--outdir", work, path], ctx["timeout"], env=env)
        secs = round(time.time() - t0, 2)
        pdfs = glob.glob(os.path.join(work, "*.pdf"))
        if timed_out:
            return {"status": "timeout", "seconds": secs}
        if rc == 0 and pdfs:
            return {"status": "opened", "pages": pdf_pages(pdfs[0]), "seconds": secs}
        return {"status": "failed", "exit": rc, "seconds": secs, "message": out.strip()[-300:]}
    finally:
        shutil.rmtree(work, ignore_errors=True)


def check_validate(path, ctx):
    sys.path.insert(0, os.path.join(HERE, "..", "validate"))
    import validate_ooxml as v
    if "schemas" not in ctx["cache"]:
        dirs = ctx["schemas"] or [d for d in os.environ.get("ONEDOCUMENT_XSD", "").split(os.pathsep) if d]
        s = v.SchemaSet(dirs)
        ctx["cache"]["schemas"] = (s, s.namespaces)
    s, understood = ctx["cache"]["schemas"]
    counts = {"valid": 0, "invalid": 0, "no-schema": 0}
    bad = []
    try:
        for name, data in v.package_parts(path):
            r = v.validate_part(name, data, s, understood)
            counts[r["status"]] += 1
            if r["status"] == "invalid":
                bad.append({"uri": name, "errors": r["errors"][:3]})
    except Exception as e:  # unreadable package: report, never stop the run
        return {"status": "unreadable", "message": str(e)[:200]}
    return {"status": "invalid" if counts["invalid"] else "valid", **counts, "invalidParts": bad[:5]}


def check_cmd(path, ctx):
    work = tempfile.mkdtemp(prefix="cmd-", dir=ctx["scratch"])
    try:
        ext = os.path.splitext(path)[1]
        out = os.path.join(work, "out" + ext)
        cmd = ctx["cmd"].format(**{"in": path, "out": out, "dir": work})
        t0 = time.time()
        rc, text, timed_out = run_group(cmd, ctx["timeout"], shell=True)
        r = {"seconds": round(time.time() - t0, 2)}
        if timed_out:
            return {**r, "status": "timeout"}
        if rc != 0:
            return {**r, "status": "failed", "exit": rc, "message": text.strip()[-300:]}
        r["status"] = "ok"
        if ctx["roundtrip"] and os.path.exists(out):
            rt = os.path.join(work, "rt.jsonl")
            rrc, _, _ = run_group(["dotnet", ctx["roundtrip"], path, out, "--quiet", "--json", rt], 120)
            if os.path.exists(rt):
                rows = [json.loads(l) for l in open(rt)]
                r["roundtrip"] = rows[-1]
                r["changedParts"] = [x["uri"] for x in rows if x.get("status") in ("changed", "added", "removed")][:20]
            r["roundtripExit"] = rrc
        return r
    finally:
        shutil.rmtree(work, ignore_errors=True)


CHECKS = {"lo": check_lo, "validate": check_validate, "cmd": check_cmd}


def shard_main(args):
    i, files, a, deadline = args
    out = os.path.join(a.out, "results-%s-%d.jsonl" % (a.check, i))
    scratch = os.path.join(a.out, "scratch", str(i))
    os.makedirs(scratch, exist_ok=True)
    profile = os.path.join(os.environ.get("LO_PROFILE_ROOT", os.path.join(a.out, "profiles")), "p%d" % i)
    ctx = dict(timeout=a.timeout, scratch=scratch, profile=profile, cache={}, schemas=a.schemas, cmd=a.cmd, roundtrip=a.roundtrip)
    fn = CHECKS[a.check]
    done = 0
    with open(out, "a") as fh:
        for f in files:
            if time.time() > deadline:
                break
            try:
                r = fn(f, ctx)
            except Exception as e:
                r = {"status": "error", "message": repr(e)[:300]}
            r.update(file=os.path.relpath(f, a.base), sha256=sha256(f), size=os.path.getsize(f), check=a.check)
            fh.write(json.dumps(r) + "\n")
            fh.flush()
            done += 1
    if a.check == "lo":  # stop any LibreOffice left on this shard's profile
        subprocess.run(["pkill", "-f", profile], check=False)
    return done


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("check", choices=CHECKS)
    ap.add_argument("--input", required=True)
    ap.add_argument("--glob", default="*")
    ap.add_argument("--shards", type=int, default=4)
    ap.add_argument("--timeout", type=int, default=120)
    ap.add_argument("--max-minutes", type=float, default=25)
    ap.add_argument("--out", required=True)
    ap.add_argument("--limit", type=int)
    ap.add_argument("--cmd")
    ap.add_argument("--roundtrip")
    ap.add_argument("--schemas", action="append", default=[])
    a = ap.parse_args()
    if a.check == "cmd" and not a.cmd:
        ap.error("cmd needs --cmd")
    os.makedirs(a.out, exist_ok=True)
    a.base = a.input if os.path.isdir(a.input) else os.path.dirname(a.input)
    files = [a.input] if os.path.isfile(a.input) else sorted(glob.glob(os.path.join(a.input, "**", a.glob), recursive=True))
    files = [f for f in files if os.path.isfile(f)]
    seen = set()
    for r in glob.glob(os.path.join(a.out, "results-%s-*.jsonl" % a.check)):
        for line in open(r):
            try:
                seen.add(json.loads(line)["file"])
            except (ValueError, KeyError):
                pass
    todo = [f for f in files if os.path.relpath(f, a.base) not in seen]
    if a.limit:
        todo = todo[:max(0, a.limit - len(seen))]
    deadline = time.time() + a.max_minutes * 60
    n = max(1, min(a.shards, len(todo)))
    # round-robin keeps shards balanced when sizes correlate with name order
    chunks = [todo[k::n] for k in range(n)]
    with mp.Pool(n) as pool:
        ran = sum(pool.map(shard_main, [(k, chunks[k], a, deadline) for k in range(n)]))
    counts, total = {}, 0
    for r in glob.glob(os.path.join(a.out, "results-%s-*.jsonl" % a.check)):
        for line in open(r):
            st = json.loads(line)["status"]
            counts[st] = counts.get(st, 0) + 1
            total += 1
    summary = {"check": a.check, "input": os.path.basename(a.input.rstrip("/")), "files": len(files), "collected": total,
               "remaining": max(0, len(files) - total), "ranThisInvocation": ran, "statuses": counts}
    with open(os.path.join(a.out, "summary-%s.json" % a.check), "w") as fh:
        json.dump(summary, fh, indent=1)
    shutil.rmtree(os.path.join(a.out, "scratch"), ignore_errors=True)
    print(json.dumps(summary))
    return 0


if __name__ == "__main__":
    sys.exit(main())
