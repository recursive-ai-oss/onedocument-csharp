#!/usr/bin/env python3
"""Fetch Office documents from a Common Crawl crawl.

Finds records through the columnar (parquet) index filtered on the detected
media type, reads the WARC records by byte range from data.commoncrawl.org and
stores the payloads. Per-domain caps keep the sample varied. The corpus is for
local testing only: nothing fetched here is committed or redistributed.

Usage:
  fetch_commoncrawl.py --out DIR [--crawl CC-MAIN-2026-43] [--types docx,xlsx]
                       [--count 2000] [--index-parts 6] [--per-domain 15]
                       [--max-bytes 25000000] [--threads 8]

Output: DIR/<type>/<sha256>.<type> and DIR/manifest-<type>.jsonl (url, crawl,
warc file, offset, length, sha256, size). Re-running skips files already
present and tops up to --count. Requires the `duckdb` Python package.
Terms: Common Crawl Terms of Use; honour copyrights, no personal-data harvesting.
"""
import argparse, subprocess, gzip, hashlib, io, json, os, sys, time, urllib.request
from concurrent.futures import ThreadPoolExecutor

BASE = "https://data.commoncrawl.org/"
UA = "onedocument-corpus-fetch/1.0 (test corpus)"

MIMES = {
    "docx": ["application/vnd.openxmlformats-officedocument.wordprocessingml.document",
             "application/vnd.ms-word.document.macroenabled.12"],
    "xlsx": ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
             "application/vnd.ms-excel.sheet.macroenabled.12"],
    "pptx": ["application/vnd.openxmlformats-officedocument.presentationml.presentation",
             "application/vnd.ms-powerpoint.presentation.macroenabled.12"],
    "doc": ["application/msword"],
    "xls": ["application/vnd.ms-excel"],
    "ppt": ["application/vnd.ms-powerpoint"],
}
MAGIC = {"docx": b"PK", "xlsx": b"PK", "pptx": b"PK",
         "doc": b"\xd0\xcf\x11\xe0", "xls": b"\xd0\xcf\x11\xe0", "ppt": b"\xd0\xcf\x11\xe0"}


def get(url, headers=None, tries=14):
    for i in range(tries):
        try:
            req = urllib.request.Request(url, headers={"User-Agent": UA, **(headers or {})})
            with urllib.request.urlopen(req, timeout=90) as r:
                return r.read()
        except Exception as e:  # network-bound; back off and retry
            if i == tries - 1:
                raise
            time.sleep(min(60, 3 * 2 ** i))  # 503 SlowDown means: back off


def index_parts(crawl, n):
    raw = gzip.decompress(get(f"{BASE}crawl-data/{crawl}/cc-index-table.paths.gz")).decode()
    parts = [l for l in raw.split() if "/subset=warc/" in l]
    if not parts:
        sys.exit("no parquet parts found for " + crawl)
    step = max(1, len(parts) // n)  # spread over the sorted-by-URL index for variety
    return [BASE + p for p in parts[::step][:n]]


def download(url, cache):
    os.makedirs(cache, exist_ok=True)
    path = os.path.join(cache, url.rsplit("/", 1)[-1])
    if not os.path.exists(path):
        for i in range(14):
            r = subprocess.run(["curl", "-sf", "-A", UA, "-o", path + ".tmp", url])
            if r.returncode == 0:
                os.replace(path + ".tmp", path)
                break
            time.sleep(min(60, 3 * 2 ** i))
        else:
            raise RuntimeError("download failed: " + url)
    return path


def candidates(parts, typ, per_domain, want, cache):
    import duckdb
    con = duckdb.connect()
    con.execute("SET threads=2")
    mimes = ",".join("'%s'" % m for m in MIMES[typ])
    ext = "." + typ
    seen, out = {}, []
    for url in parts:
        # DuckDB's parallel range reads trip the host's rate limit, so fetch the
        # part file once, serially, and query the local copy.
        try:
            p = download(url, cache)
        except RuntimeError as e:
            print(e, file=sys.stderr)
            continue
        q = f"""select url, url_host_registered_domain, warc_filename, warc_record_offset, warc_record_length
                from read_parquet('{p}')
                where fetch_status = 200
                  and (content_mime_detected in ({mimes})
                       or lower(url_path) like '%{ext}')
                  and warc_record_length < 30000000"""
        try:
            rows = con.execute(q).fetchall()
        except Exception as e:
            print("index part failed", p, e, file=sys.stderr)
            continue
        print(f"{typ}: {len(rows)} candidates in {p.rsplit('/', 1)[-1]}", file=sys.stderr)
        os.remove(p)
        for url, dom, fn, off, ln in rows:
            if seen.get(dom, 0) >= per_domain:
                continue
            seen[dom] = seen.get(dom, 0) + 1
            out.append((url, fn, off, ln))
        if len(out) >= want * 2:
            break
    return out


def parse_warc(blob):
    """Return (http_status, body) from one gzip-member WARC response record."""
    data = gzip.decompress(blob)
    i = data.find(b"\r\n\r\n")           # end of WARC headers
    j = data.find(b"\r\n\r\n", i + 4)    # end of HTTP headers
    if i < 0 or j < 0:
        return None
    status = int(data[i + 4:i + 16].split(b" ")[1]) if data[i + 4:i + 9].startswith(b"HTTP/") else 0
    return status, data[j + 4:]


def fetch_one(args, typ, c):
    url, fn, off, ln = c
    try:
        blob = get(BASE + fn, {"Range": f"bytes={off}-{off + ln - 1}"})
        r = parse_warc(blob)
        if not r or r[0] != 200:
            return None
        body = r[1]
        if not body.startswith(MAGIC[typ]) or len(body) > args.max_bytes or len(body) < 1000:
            return None
        sha = hashlib.sha256(body).hexdigest()
        return dict(url=url, crawl=args.crawl, warc=fn, offset=off, length=ln, sha256=sha, size=len(body)), body
    except Exception as e:
        print("fetch failed", url, e, file=sys.stderr)
        return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--crawl", default="CC-MAIN-2026-43")
    ap.add_argument("--types", default="docx")
    ap.add_argument("--count", type=int, default=2000)
    ap.add_argument("--index-parts", type=int, default=6)
    ap.add_argument("--per-domain", type=int, default=15)
    ap.add_argument("--max-bytes", type=int, default=25_000_000)
    ap.add_argument("--threads", type=int, default=8)
    a = ap.parse_args()
    parts = index_parts(a.crawl, a.index_parts)
    for typ in a.types.split(","):
        d = os.path.join(a.out, typ)
        os.makedirs(d, exist_ok=True)
        man = os.path.join(a.out, f"manifest-{typ}.jsonl")
        have = set(f.split(".")[0] for f in os.listdir(d))
        got = len(have)
        if got >= a.count:
            print(typ, "already has", got)
            continue
        cands = candidates(parts, typ, a.per_domain, a.count - got, os.path.join(a.out, ".index"))
        if os.path.exists(man):  # already fetched in an earlier run
            old = {json.loads(l)["url"] for l in open(man)}
            cands = [c for c in cands if c[0] not in old]
        print(f"{typ}: fetching from {len(cands)} candidates, have {got}", file=sys.stderr)
        with ThreadPoolExecutor(a.threads) as ex, open(man, "a") as mf:
            for res in ex.map(lambda c: fetch_one(a, typ, c), cands):
                if not res:
                    continue
                meta, body = res
                if meta["sha256"] in have:
                    continue
                have.add(meta["sha256"])
                with open(os.path.join(d, f"{meta['sha256']}.{typ}"), "wb") as f:
                    f.write(body)
                mf.write(json.dumps(meta) + "\n")
                mf.flush()
                got += 1
                if got >= a.count:
                    break
        print(f"{typ}: {got} files in {d}")


if __name__ == "__main__":
    main()
