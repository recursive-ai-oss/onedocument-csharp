#!/usr/bin/python3
"""Writes password-protected copies of a few small documents with LibreOffice, driven through its public UNO API (a black-box run).

usage: make-encrypted.py SOFFICE PROFILE_DIR WORK_DIR INPUT_DIR OUT_DIR
  SOFFICE      path of the soffice launcher
  PROFILE_DIR  an empty directory for a private LibreOffice profile
  WORK_DIR     where plain OOXML copies of the inputs are written
  INPUT_DIR    holds basic.xlsx, basic.doc and lo-slide-runs.ppt (the committed fixtures)
  OUT_DIR      receives the encrypted files and the plain OOXML twins (plain-*.docx/xlsx/pptx)

The password is `secret` everywhere. Needs the system python3 with the `uno` module (python3-uno).
"""
import os
import shutil
import subprocess
import sys
import time

import uno
from com.sun.star.beans import PropertyValue

PORT = int(os.environ.get("ONEDOCUMENT_LO_PORT", 2002 + (os.getpid() % 500)))
PASSWORD = "secret"


def prop(name, value):
    p = PropertyValue()
    p.Name = name
    p.Value = value
    return p


def main():
    soffice, profile, work, inputs, out = sys.argv[1:6]
    os.makedirs(work, exist_ok=True)
    os.makedirs(out, exist_ok=True)
    proc = subprocess.Popen([
        soffice, f"-env:UserInstallation=file://{profile}", "--headless", "--invisible", "--norestore",
        f"--accept=socket,host=127.0.0.1,port={PORT};urp;",
    ], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    try:
        local = uno.getComponentContext()
        resolver = local.ServiceManager.createInstanceWithContext("com.sun.star.bridge.UnoUrlResolver", local)
        ctx = None
        for _ in range(120):
            try:
                ctx = resolver.resolve(f"uno:socket,host=127.0.0.1,port={PORT};urp;StarOffice.ComponentContext")
                break
            except Exception:
                time.sleep(0.5)
        if ctx is None:
            raise SystemExit("soffice did not start")
        desktop = ctx.ServiceManager.createInstanceWithContext("com.sun.star.frame.Desktop", ctx)

        def convert(src, dst, filter_name, password=None):
            doc = desktop.loadComponentFromURL(uno.systemPathToFileUrl(os.path.abspath(src)), "_blank", 0, (prop("Hidden", True),))
            args = [prop("FilterName", filter_name)]
            if password:
                args.append(prop("Password", password))
            doc.storeToURL(uno.systemPathToFileUrl(os.path.abspath(dst)), tuple(args))
            doc.close(True)

        # Plain OOXML twins first: the encrypted OOXML files are written from them.
        convert(f"{inputs}/basic.doc", f"{out}/plain-basic.docx", "MS Word 2007 XML")
        convert(f"{inputs}/basic.xlsx", f"{out}/plain-basic.xlsx", "Calc MS Excel 2007 XML")
        convert(f"{inputs}/lo-slide-runs.ppt", f"{out}/plain-runs.pptx", "Impress MS PowerPoint 2007 XML")
        for name, filt in (("plain-basic.docx", "MS Word 2007 XML"), ("plain-basic.xlsx", "Calc MS Excel 2007 XML"), ("plain-runs.pptx", "Impress MS PowerPoint 2007 XML")):
            convert(f"{out}/{name}", f"{out}/secret-{name[6:]}", filt, PASSWORD)
        convert(f"{inputs}/basic.doc", f"{out}/secret-basic.doc", "MS Word 97", PASSWORD)
        convert(f"{inputs}/basic.xlsx", f"{out}/secret-basic.xls", "MS Excel 97", PASSWORD)
        convert(f"{inputs}/basic.xlsx", f"{out}/plain-basic.xls", "MS Excel 97")
        convert(f"{inputs}/lo-slide-runs.ppt", f"{out}/secret-runs.ppt", "MS PowerPoint 97", PASSWORD)
        shutil.copy(f"{inputs}/basic.doc", f"{out}/plain-basic.doc")
        try:
            desktop.terminate()
        except Exception:
            pass
    finally:
        try:
            proc.wait(timeout=20)
        except subprocess.TimeoutExpired:
            proc.kill()


main()
