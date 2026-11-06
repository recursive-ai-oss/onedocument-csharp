#!/usr/bin/env python3
"""Summarises veraPDF's XML (MRR) report over a batch of PDFs.

    verapdf_summary.py REPORT.xml [--label NAME]

Prints one JSON object: files, compliant, and the failed rules counted by (clause, test number) with the number of files they fail in,
their failed-check totals and the rule description, plus the names of the files that fail.
"""
import collections
import json
import sys
import xml.etree.ElementTree as ET

path = sys.argv[1]
label = sys.argv[sys.argv.index("--label") + 1] if "--label" in sys.argv else ""
root = ET.parse(path).getroot()


def local(tag):
    return tag.rsplit("}", 1)[-1]


files = compliant = 0
rules = collections.defaultdict(lambda: {"files": 0, "checks": 0, "description": ""})
failing = []
errors = 0
for job in root.iter():
    if local(job.tag) != "job":
        continue
    name = ""
    report = None
    for ch in job:
        if local(ch.tag) == "item":
            for n in ch:
                if local(n.tag) == "name":
                    name = (n.text or "").split("/")[-1]
        elif local(ch.tag) == "validationReport":
            report = ch
        elif local(ch.tag) == "taskException":
            errors += 1
    if report is None:
        continue
    files += 1
    if report.get("isCompliant") == "true":
        compliant += 1
        continue
    failing.append(name)
    for el in report.iter():
        if local(el.tag) == "rule" and el.get("status") == "failed":
            key = "%s #%s" % (el.get("clause"), el.get("testNumber"))
            r = rules[key]
            r["files"] += 1
            r["checks"] += int(el.get("failedChecks") or 0)
            for d in el:
                if local(d.tag) == "description":
                    r["description"] = (d.text or "")[:200]
top = sorted(rules.items(), key=lambda kv: -kv[1]["files"])
print(json.dumps({"label": label, "files": files, "compliant": compliant, "taskExceptions": errors,
                  "rules": [dict(rule=k, **v) for k, v in top[:25]], "failing": failing[:40]}))
