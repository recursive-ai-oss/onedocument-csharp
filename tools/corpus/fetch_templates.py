#!/usr/bin/env python3
"""Fetch publicly downloadable Office templates published by Microsoft.

The template gallery is a script-driven site without a stable listing API, so this
tool takes the places to look from the caller and stays out of the gallery's way:

  fetch_templates.py --out DIR (--urls FILE | --page URL)... [--exts dotx,docx,xltx,potx]

  --urls FILE  one direct file URL per line (blank lines and # comments ignored)
  --page URL   a page that links straight to template files; every link ending in one
               of --exts is followed (repeatable)

Output: DIR/<ext>/<sha256>.<ext> and DIR/manifest-templates.jsonl (source URL, sha256,
size). The files are for local testing only and are never committed or redistributed;
check each page's terms before adding it to a list.
"""
import argparse, hashlib, json, os, re, sys, time, urllib.parse, urllib.request

UA = "onedocument-corpus-fetch/1.0 (test documents)"
MAGIC = (b"PK", b"\xd0\xcf\x11\xe0")


def get(url, tries=4):
    for i in range(tries):
        try:
            with urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": UA}), timeout=120) as r:
                return r.read()
        except Exception as e:
            if i == tries - 1:
                print("giving up", url, e, file=sys.stderr)
                return None
            time.sleep(3 * 2 ** i)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--urls")
    ap.add_argument("--page", action="append", default=[])
    ap.add_argument("--exts", default="dotx,docx,dotm,xltx,xltm,potx,potm,ppsx")
    a = ap.parse_args()
    exts = tuple("." + e for e in a.exts.split(","))
    urls = []
    if a.urls:
        urls += [l.strip() for l in open(a.urls) if l.strip() and not l.startswith("#")]
    for page in a.page:
        html = get(page)
        if html:
            for href in re.findall(r'href=["\']([^"\']+)["\']', html.decode("utf-8", "replace")):
                full = urllib.parse.urljoin(page, href.replace("&amp;", "&"))
                if urllib.parse.urlparse(full).path.lower().endswith(exts):
                    urls.append(full)
    os.makedirs(a.out, exist_ok=True)
    got = 0
    with open(os.path.join(a.out, "manifest-templates.jsonl"), "a") as mf:
        for u in dict.fromkeys(urls):
            body = get(u)
            if not body or not body.startswith(MAGIC):
                continue
            ext = os.path.splitext(urllib.parse.urlparse(u).path)[1].lower().lstrip(".")
            sha = hashlib.sha256(body).hexdigest()
            d = os.path.join(a.out, ext)
            os.makedirs(d, exist_ok=True)
            with open(os.path.join(d, f"{sha}.{ext}"), "wb") as f:
                f.write(body)
            mf.write(json.dumps({"url": u, "sha256": sha, "size": len(body)}) + "\n")
            got += 1
            time.sleep(1)
    print(f"{got} templates in {a.out}")


if __name__ == "__main__":
    main()
