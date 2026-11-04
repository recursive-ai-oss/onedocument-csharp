#!/usr/bin/env python3
"""Accept or reject every tracked change in Word files with a headless LibreOffice, and save each result's text.

  lo_resolve.py accept|reject OUT_DIR FILE...

Drives the office black-box through its public UNO interface (the `.uno:AcceptAllTrackedChanges` and
`.uno:RejectAllTrackedChanges` commands) and writes OUT_DIR/<file name>.<mode>.lo.txt. Used to compare the
revision model's accepted and rejected text with another implementation; the result is information, not a gate.
Needs python3 with the `uno` module, and `soffice` on PATH (or SOFFICE=...). LO_PROFILE names the profile directory.
"""
import os, subprocess, sys, time, uuid

import uno
from com.sun.star.beans import PropertyValue


def prop(name, value):
    p = PropertyValue()
    p.Name, p.Value = name, value
    return p


def main():
    mode, out_dir, files = sys.argv[1], sys.argv[2], sys.argv[3:]
    os.makedirs(out_dir, exist_ok=True)
    pipe = "lores" + uuid.uuid4().hex[:8]
    profile = os.environ.get("LO_PROFILE", os.path.join(out_dir, ".profile"))
    soffice = os.environ.get("SOFFICE", "soffice")
    proc = subprocess.Popen(
        [soffice, "--headless", "--invisible", "--norestore", "--nologo", "--nodefault",
         f"-env:UserInstallation=file://{os.path.abspath(profile)}", f"--accept=pipe,name={pipe};urp;"],
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, start_new_session=True)
    try:
        local = uno.getComponentContext()
        resolver = local.ServiceManager.createInstanceWithContext("com.sun.star.bridge.UnoUrlResolver", local)
        ctx = None
        for _ in range(120):
            try:
                ctx = resolver.resolve(f"uno:pipe,name={pipe};urp;StarOffice.ComponentContext")
                break
            except Exception:
                time.sleep(0.5)
        if ctx is None:
            print("could not connect", file=sys.stderr)
            return 2
        smgr = ctx.ServiceManager
        desktop = smgr.createInstanceWithContext("com.sun.star.frame.Desktop", ctx)
        dispatcher = smgr.createInstanceWithContext("com.sun.star.frame.DispatchHelper", ctx)
        command = ".uno:AcceptAllTrackedChanges" if mode == "accept" else ".uno:RejectAllTrackedChanges"
        for f in files:
            name = os.path.basename(f)
            target = os.path.join(out_dir, f"{name}.{mode}.lo.txt")
            try:
                doc = desktop.loadComponentFromURL(uno.systemPathToFileUrl(os.path.abspath(f)), "_blank", 0,
                                                   (prop("Hidden", True), prop("ReadOnly", False)))
                if doc is None:
                    print(f"FAIL {name} load", flush=True)
                    continue
                frame = doc.getCurrentController().getFrame()
                dispatcher.executeDispatch(frame, command, "", 0, ())
                doc.storeToURL(uno.systemPathToFileUrl(os.path.abspath(target)),
                               (prop("FilterName", "Text (encoded)"), prop("FilterOptions", "UTF8,LF")))
                doc.close(True)
                print(f"OK {name}", flush=True)
            except Exception as e:  # noqa: BLE001 - report and continue with the next file
                print(f"FAIL {name} {type(e).__name__}", flush=True)
        try:
            desktop.terminate()
        except Exception:
            pass
    finally:
        try:
            proc.wait(timeout=10)
        except Exception:
            proc.kill()
    return 0


if __name__ == "__main__":
    sys.exit(main())
