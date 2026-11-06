#!/usr/bin/env python3
"""Checks the OpenDocument files in WORK/ours with the validator and the reference reader.

usage: check_with_reference.py WORK

1. validates WORK/ours/*.ods and *.fods against the ODF 1.3 RelaxNG schema (strictly, and again without the producers' extension attributes);
2. converts WORK/ours/*.ods to WORK/lo/*.xlsx with a spreadsheet application run headless, recalculating every formula on load,
   so that the OdsOracleTests.Compare run can set the values it read back against the values we wrote.
Locations come from ONEDOCUMENT_HOME (default ~/oneoffice): oracles/soffice.sh, oracles/jing.sh and schemas/odf/.
"""
import glob
import os
import subprocess
import sys

HOME = os.environ.get("ONEDOCUMENT_HOME", os.path.expanduser("~/oneoffice"))
HERE = os.path.dirname(os.path.abspath(__file__))

RECALC_PROFILE = """<?xml version="1.0" encoding="UTF-8"?>
<oor:items xmlns:oor="http://openoffice.org/2001/registry" xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
<item oor:path="/org.openoffice.Office.Calc/Formula/Load"><prop oor:name="ODFRecalcMode" oor:op="fuse"><value>0</value></prop></item>
<item oor:path="/org.openoffice.Office.Calc/Formula/Load"><prop oor:name="OOXMLRecalcMode" oor:op="fuse"><value>0</value></prop></item>
</oor:items>
"""


def main(argv):
    work = argv[0]
    ours = os.path.join(work, "ours")
    env = dict(os.environ, ONEDOCUMENT_HOME=HOME)
    py = sys.executable
    validate = os.path.join(HERE, "..", "validate", "validate_odf.py")
    files = sorted(glob.glob(os.path.join(ours, "*.ods")) + glob.glob(os.path.join(ours, "*.fods")))
    for flags, label in (([], "strict"), (["--ignore-extensions"], "without extension attributes")):
        out = subprocess.run([py, validate, *flags, *files], capture_output=True, text=True, env=env)
        with open(os.path.join(work, "validate-" + label.split()[0] + ".txt"), "w") as f:
            f.write(out.stdout + out.stderr)
        print(label + ":", (out.stdout.strip().splitlines() or [""])[-1])

    profile = os.path.join(work, "lo-profile")
    os.makedirs(os.path.join(profile, "user"), exist_ok=True)
    with open(os.path.join(profile, "user", "registrymodifications.xcu"), "w") as f:
        f.write(RECALC_PROFILE)
    lo = os.path.join(work, "lo")
    os.makedirs(lo, exist_ok=True)
    soffice = os.path.join(HOME, "oracles", "soffice.sh")
    env["LO_PROFILE"] = profile
    ods = [f for f in files if f.endswith(".ods")]
    for i in range(0, len(ods), 20):
        subprocess.run([soffice, "--convert-to", "xlsx", "--outdir", lo, *ods[i:i + 20]], env=env, capture_output=True, timeout=1500)
    print("converted", len(glob.glob(os.path.join(lo, "*.xlsx"))), "of", len(ods))


if __name__ == "__main__":
    main(sys.argv[1:])
