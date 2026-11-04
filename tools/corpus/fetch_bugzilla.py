#!/usr/bin/env python3
"""Fetch Office documents attached to bugs on public Bugzilla trackers.

Only the attached files are used, as test documents. Nothing else is stored:
no patches, no comment text, no source from any project repository. Obsolete
attachments are fine (they are still user-submitted documents); patches
(`is_patch`) are skipped.

Usage:
  fetch_bugzilla.py --out DIR [--tracker documentfoundation|apache]
                    [--exts docx,xlsx,pptx] [--count 500] [--max-bytes 25000000]

Output: DIR/<tracker>/<ext>/<sha256>.<ext> and DIR/manifest-bugzilla-<tracker>.jsonl
(bug id, attachment id, URL, content type, sha256, size). Re-runs skip files
already present. Be polite: requests are serial with a short pause.
"""
import argparse, hashlib, json, os, sys, time, urllib.parse, urllib.request

TRACKERS = {"documentfoundation": "https://bugs.documentfoundation.org",
            "apache": "https://bz.apache.org/bugzilla"}
UA = "oneoffice-corpus-fetch/1.0 (test documents)"
MAGIC = {"docx": b"PK", "xlsx": b"PK", "pptx": b"PK", "dotx": b"PK", "xlsm": b"PK", "docm": b"PK",
         "doc": b"\xd0\xcf\x11\xe0", "xls": b"\xd0\xcf\x11\xe0", "ppt": b"\xd0\xcf\x11\xe0"}


def get(url, tries=6):
    for i in range(tries):
        try:
            req = urllib.request.Request(url, headers={"User-Agent": UA})
            with urllib.request.urlopen(req, timeout=120) as r:
                return r.read()
        except Exception as e:
            if i == tries - 1:
                print("giving up", url, e, file=sys.stderr)
                return None
            time.sleep(min(60, 3 * 2 ** i))


def bug_ids(base, ext, limit_bugs):
    ids, offset = [], 0
    while len(ids) < limit_bugs:
        q = urllib.parse.urlencode({"f1": "attachments.filename", "o1": "substring", "v1": "." + ext,
                                    "include_fields": "id", "limit": 500, "offset": offset,
                                    "order": "bug_id DESC"})
        raw = get(f"{base}/rest/bug?{q}")
        if not raw:
            break
        page = json.loads(raw).get("bugs", [])
        if not page:
            break
        ids += [b["id"] for b in page]
        offset += len(page)
        time.sleep(1)
    return ids[:limit_bugs]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--tracker", default="documentfoundation", choices=TRACKERS)
    ap.add_argument("--exts", default="docx")
    ap.add_argument("--count", type=int, default=500)
    ap.add_argument("--max-bytes", type=int, default=25_000_000)
    a = ap.parse_args()
    base = TRACKERS[a.tracker]
    man = os.path.join(a.out, f"manifest-bugzilla-{a.tracker}.jsonl")
    os.makedirs(a.out, exist_ok=True)
    for ext in a.exts.split(","):
        d = os.path.join(a.out, a.tracker, ext)
        os.makedirs(d, exist_ok=True)
        have = {f.split(".")[0] for f in os.listdir(d)}
        got = len(have)
        for bug in bug_ids(base, ext, a.count * 3):
            if got >= a.count:
                break
            raw = get(f"{base}/rest/bug/{bug}/attachment?exclude_fields=data")
            if not raw:
                continue
            for atts in json.loads(raw).get("bugs", {}).values():
                for att in atts:
                    name = att.get("file_name", "")
                    if att.get("is_patch") or not name.lower().endswith("." + ext) or att.get("size", 0) > a.max_bytes:
                        continue
                    body = get(f"{base}/attachment.cgi?id={att['id']}")  # attachment file only
                    time.sleep(0.5)
                    if not body or not body.startswith(MAGIC[ext]):
                        continue
                    sha = hashlib.sha256(body).hexdigest()
                    if sha in have:
                        continue
                    have.add(sha)
                    with open(os.path.join(d, f"{sha}.{ext}"), "wb") as f:
                        f.write(body)
                    with open(man, "a") as mf:
                        mf.write(json.dumps(dict(tracker=a.tracker, bug=bug, attachment=att["id"],
                                                 url=f"{base}/attachment.cgi?id={att['id']}",
                                                 content_type=att.get("content_type"), sha256=sha,
                                                 size=len(body))) + "\n")
                    got += 1
            time.sleep(0.5)
        print(f"{a.tracker}/{ext}: {got} files in {d}")


if __name__ == "__main__":
    main()
