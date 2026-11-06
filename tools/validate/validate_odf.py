#!/usr/bin/env python3
"""Validates OpenDocument files against the OASIS OpenDocument RelaxNG schemas with Jing.

usage: validate_odf.py [--version 1.3|1.2] FILE_OR_DIR...

Zip packages (.odt .ott .ods .ots .odp .otp) are unpacked: content.xml, styles.xml, meta.xml and settings.xml are checked against the
document schema and META-INF/manifest.xml against the manifest schema. Flat files (.fodt .fods .fodp) are checked whole.
ID/IDREF checking is switched off (jing -i): the schema pairs IDREF values with attributes in ways Jing rejects as a schema error.
Locations come from the environment: ONEDOCUMENT_HOME (default ~/oneoffice) holds oracles/jing.sh and schemas/odf/<version>/.
Prints one line per failing file part and a count line; exit status is 1 when anything failed.
"""
import os
import subprocess
import sys
import tempfile
import zipfile

HOME = os.environ.get("ONEDOCUMENT_HOME", os.path.expanduser("~/oneoffice"))
ZIP_KINDS = (".odt", ".ott", ".ods", ".ots", ".odp", ".otp")
FLAT_KINDS = (".fodt", ".fods", ".fodp")


def schema_path(version, kind):
    name = {"1.3": "OpenDocument-v1.3", "1.2": "OpenDocument-v1.2-os"}[version]
    suffix = {"document": "-schema.rng", "manifest": "-manifest-schema.rng"}[kind]
    return os.path.join(HOME, "schemas", "odf", version, name + suffix)


def jing(schema, xml):
    p = subprocess.run([os.path.join(HOME, "oracles", "jing.sh"), "-i", schema, xml], capture_output=True, text=True)
    return p.returncode == 0, (p.stdout + p.stderr).strip()


def main(argv):
    version = "1.3"
    if argv and argv[0] == "--version":
        version, argv = argv[1], argv[2:]
    files = []
    for a in argv:
        if os.path.isdir(a):
            files += [os.path.join(a, f) for f in sorted(os.listdir(a)) if f.lower().endswith(ZIP_KINDS + FLAT_KINDS)]
        else:
            files.append(a)
    bad = 0
    checked = 0
    for f in files:
        ext = os.path.splitext(f)[1].lower()
        parts = []
        tmp = tempfile.mkdtemp(prefix="odfval", dir=os.environ.get("TMPDIR"))
        if ext in FLAT_KINDS:
            parts.append(("(flat)", f, "document"))
        else:
            with zipfile.ZipFile(f) as z:
                for n, kind in (("content.xml", "document"), ("styles.xml", "document"), ("meta.xml", "document"), ("settings.xml", "document"), ("META-INF/manifest.xml", "manifest")):
                    if n in z.namelist():
                        out = os.path.join(tmp, n.replace("/", "_"))
                        with open(out, "wb") as o:
                            o.write(z.read(n))
                        parts.append((n, out, kind))
        for name, path, kind in parts:
            checked += 1
            ok, msg = jing(schema_path(version, kind), path)
            if not ok:
                bad += 1
                print("FAIL", os.path.basename(f), name)
                for line in msg.splitlines()[:6]:
                    print("   ", line[:300])
    print(f"checked {checked} parts of {len(files)} files, {bad} failed (ODF {version})")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
