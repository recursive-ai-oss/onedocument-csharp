#!/usr/bin/python3
"""Opens password-protected files with LibreOffice, through its public UNO API (a black-box run), and writes what it reads.

usage: lo-text.py SOFFICE PROFILE_DIR OUT_DIR PASSWORD FILE...
  SOFFICE      path of the soffice launcher
  PROFILE_DIR  an empty directory for a private LibreOffice profile
  OUT_DIR      receives FILE.txt for every file that opened: the text of a document, the cell values of the first sheet
               of a workbook, or the text of the shapes of a presentation
  PASSWORD     given to every file

Prints one line per file: NAME OK LENGTH, or NAME NOT-OPENED. Needs the system python3 with the `uno` module (python3-uno).
"""
import os
import subprocess
import sys
import time

import uno
import unohelper
from com.sun.star.beans import PropertyValue
from com.sun.star.task import XInteractionHandler

PORT = int(os.environ.get("ONEDOCUMENT_LO_PORT", 2600 + (os.getpid() % 300)))


def prop(name, value):
    p = PropertyValue()
    p.Name = name
    p.Value = value
    return p


class PasswordHandler(unohelper.Base, XInteractionHandler):
    """Answers a password request with the password given on the command line."""

    def __init__(self, password):
        self.password = password

    def handle(self, request):
        for continuation in request.getContinuations():
            if hasattr(continuation, "setPassword"):
                continuation.setPassword(self.password)
                continuation.select()
                return


def text_of(doc):
    if doc.supportsService("com.sun.star.text.TextDocument"):
        return doc.getText().getString()
    if doc.supportsService("com.sun.star.sheet.SpreadsheetDocument"):
        sheet = doc.getSheets().getByIndex(0)
        cursor = sheet.createCursor()
        cursor.gotoEndOfUsedArea(False)
        end = cursor.getRangeAddress()
        return repr(sheet.getCellRangeByPosition(0, 0, end.EndColumn, end.EndRow).getDataArray())
    text = ""
    pages = doc.getDrawPages()
    for i in range(pages.getCount()):
        page = pages.getByIndex(i)
        for j in range(page.getCount()):
            shape = page.getByIndex(j)
            if hasattr(shape, "getString"):
                text += shape.getString() + "\n"
    return text


def main():
    soffice, profile, out, password = sys.argv[1:5]
    os.makedirs(out, exist_ok=True)
    proc = subprocess.Popen(
        [soffice, f"-env:UserInstallation=file://{profile}", "--headless", "--invisible", "--norestore",
         f"--accept=socket,host=127.0.0.1,port={PORT};urp;"],
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
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
        for path in sys.argv[5:]:
            name = os.path.basename(path)
            try:
                doc = desktop.loadComponentFromURL(
                    uno.systemPathToFileUrl(os.path.abspath(path)), "_blank", 0,
                    (prop("Hidden", True), prop("Password", password), prop("InteractionHandler", PasswordHandler(password))))
            except Exception as e:
                print(name, "NOT-OPENED", e)
                continue
            if doc is None:
                print(name, "NOT-OPENED")
                continue
            text = text_of(doc)
            with open(os.path.join(out, name + ".txt"), "w", encoding="utf-8") as f:
                f.write(text)
            print(name, "OK", len(text))
            doc.close(True)
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
