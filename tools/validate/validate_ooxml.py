#!/usr/bin/env python3
"""Validate the XML parts of OOXML packages against pinned ECMA-376 schemas.

Usage:
  validate_ooxml.py [--schemas DIR]... [--understand NS]... [--json FILE] [--quiet] FILE...

DIR holds extracted .xsd files (see tools/setup/extract-schemas.sh); several
may be given (Transitional, Strict, packaging, extension schemas). The schema
for a part is chosen by its root element's namespace and name, so a Strict
file is checked against the Strict schemas and a Transitional file against the
Transitional ones. Parts whose root element no loaded schema declares are
reported as "no-schema" and do not fail the run.

Markup compatibility is applied first, as a consumer that understands only the
namespaces of the loaded schemas would (ECMA-376 Part 3 §10): content in
non-understood namespaces named by mc:Ignorable is dropped, mc:AlternateContent
resolves to the first mc:Choice whose Requires namespaces are all understood or
else to mc:Fallback, and the mc attributes are removed. Anything else the
schemas do not allow is an error. Errors carry the line and column of the
element's start tag in the original part.

Output: JSON lines (one per part, then a summary) to stdout or --json FILE.
Exit status: 0 all valid, 1 any invalid, 2 usage or unreadable package.
Needs lxml (libxml2).
"""
import argparse, glob, json, os, sys, zipfile
from xml.parsers import expat
from lxml import etree

MC = "http://schemas.openxmlformats.org/markup-compatibility/2006"
XML_NS = "http://www.w3.org/XML/1998/namespace"
XSD = "http://www.w3.org/2001/XMLSchema"
STUBS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "stubs")
STUB_FILES = ((XML_NS, "xml.xsd"), ("http://purl.org/dc/elements/1.1/", "dc.xsd"), ("http://purl.org/dc/terms/", "dcterms.xsd"))


class SchemaSet:
    def __init__(self, dirs):
        self.root_to_file = {}   # (ns, local) -> xsd path declaring that global element
        self.compiled = {}
        self.namespaces = {MC, XML_NS}
        for d in dirs:
            for f in sorted(glob.glob(os.path.join(d, "**", "*.xsd"), recursive=True)):
                try:
                    t = etree.parse(f).getroot()
                except etree.XMLSyntaxError:
                    continue
                tns = t.get("targetNamespace")
                if not tns:
                    continue
                self.namespaces.add(tns)
                for e in t.findall("{%s}element" % XSD):
                    self.root_to_file.setdefault((tns, e.get("name")), f)

    def schema_for(self, ns, local):
        f = self.root_to_file.get((ns, local))
        if f is None:
            return None, None
        if f not in self.compiled:
            try:
                # Wrap the schema so the namespace-only imports (xml:, Dublin Core) resolve to the local stand-ins.
                imports = "".join('<xs:import namespace="%s" schemaLocation="%s"/>' % (n, os.path.join(STUBS, s))
                                  for n, s in STUB_FILES)
                wrapper = ('<xs:schema xmlns:xs="%s">%s<xs:import namespace="%s" schemaLocation="%s"/></xs:schema>'
                           % (XSD, imports, etree.parse(f).getroot().get("targetNamespace"), f))
                self.compiled[f] = etree.XMLSchema(etree.fromstring(wrapper.encode()))
            except etree.XMLSchemaParseError as e:
                # e.g. opc-coreProperties imports Dublin Core schemas the package does not ship
                self.compiled[f] = str(e)
        c = self.compiled[f]
        return f, c


def positions(data):
    """Start-tag (line, column) of every element in document order."""
    out = []
    p = expat.ParserCreate(namespace_separator=None)
    p.StartElementHandler = lambda n, a: out.append((p.CurrentLineNumber, p.CurrentColumnNumber + 1))
    try:
        p.Parse(data, True)
    except expat.ExpatError:
        pass
    return out


def q(ns, local):
    return "{%s}%s" % (ns, local)


def preprocess(root, understood):
    """Apply markup compatibility in place (ECMA-376 Part 3 §10)."""
    def walk(el, ignorable):
        ign = set(ignorable)
        v = el.get(q(MC, "Ignorable"))
        if v:
            for pfx in v.split():
                ns = el.nsmap.get(pfx)
                if ns:
                    ign.add(ns)
        for a in list(el.attrib):
            if a.startswith("{"):
                ans = a[1:].split("}")[0]
                if ans in ign and ans not in understood:
                    del el.attrib[a]
        for a in (q(MC, "Ignorable"), q(MC, "ProcessContent"), q(MC, "MustUnderstand")):
            if a in el.attrib:
                del el.attrib[a]
        for child in list(el):
            if not isinstance(child.tag, str):
                continue
            ns = etree.QName(child).namespace
            if child.tag == q(MC, "AlternateContent"):
                chosen = None
                for br in child:
                    if br.tag == q(MC, "Choice"):
                        req = br.get("Requires", "").split()
                        if all(br.nsmap.get(p) in understood for p in req):
                            chosen = br
                            break
                if chosen is None:
                    chosen = next((b for b in child if b.tag == q(MC, "Fallback")), None)
                parent = child.getparent()
                idx = parent.index(child)
                kids = list(chosen) if chosen is not None else []
                tail = child.tail
                parent.remove(child)
                for k, kid in enumerate(kids):
                    parent.insert(idx + k, kid)
                if tail:
                    if kids:
                        kids[-1].tail = (kids[-1].tail or "") + tail
                    elif idx > 0:
                        parent[idx - 1].tail = (parent[idx - 1].tail or "") + tail
                    else:
                        parent.text = (parent.text or "") + tail
                for kid in kids:
                    walk(kid, ign)
            elif ns in ign and ns not in understood:
                tail = child.tail
                prev = child.getprevious()
                el.remove(child)
                if tail:
                    if prev is not None:
                        prev.tail = (prev.tail or "") + tail
                    else:
                        el.text = (el.text or "") + tail
            else:
                walk(child, ign)
    walk(root, set())


def validate_part(name, data, schemas, understood):
    row = {"kind": "part", "uri": name}
    try:
        root = etree.fromstring(data, etree.XMLParser(resolve_entities=False, no_network=True, load_dtd=False))
    except etree.XMLSyntaxError as e:
        row.update(status="invalid", errors=[{"line": e.lineno, "column": e.offset, "message": "not well-formed: " + str(e)}])
        return row
    qn = etree.QName(root)
    f, schema = schemas.schema_for(qn.namespace, qn.localname)
    if schema is None or isinstance(schema, str):
        row.update(status="no-schema", root=str(root.tag))
        if schema:
            row["schemaError"] = schema
        return row
    pos = positions(data)
    elements = [e for e in root.iter() if isinstance(e.tag, str)]   # keeps proxies alive for identity lookup
    where = {id(e): pos[i] for i, e in enumerate(elements) if i < len(pos)}
    keep = elements
    preprocess(root, understood)
    tree = etree.ElementTree(root)
    ok = schema.validate(tree)
    row.update(schema=os.path.basename(f), status="valid" if ok else "invalid")
    if not ok:
        errs = []
        by_path = {tree.getpath(e): e for e in root.iter() if isinstance(e.tag, str)}
        for er in schema.error_log:
            line, col = er.line, None
            hit = by_path.get(er.path)
            if hit is not None and id(hit) in where:
                line, col = where[id(hit)]
            errs.append({"line": line, "column": col, "path": er.path, "message": er.message})
        row["errors"] = errs[:50]
        row["errorCount"] = len(errs)
    del keep
    return row


def package_parts(path):
    with zipfile.ZipFile(path) as z:
        for info in z.infolist():
            n = info.filename
            if n.endswith("/") or not (n.endswith(".xml") or n.endswith(".rels") or n.endswith(".vml")):
                continue
            yield "/" + n, z.read(info)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--schemas", action="append", default=[])
    ap.add_argument("--understand", action="append", default=[])
    ap.add_argument("--json")
    ap.add_argument("--quiet", action="store_true")
    ap.add_argument("files", nargs="+")
    a = ap.parse_args()
    dirs = a.schemas or [d for d in os.environ.get("ONEOFFICE_XSD", "").split(os.pathsep) if d]
    if not dirs:
        print("no schema directories: pass --schemas or set ONEOFFICE_XSD", file=sys.stderr)
        return 2
    schemas = SchemaSet(dirs)
    understood = schemas.namespaces | set(a.understand)
    out, bad = [], 0
    for f in a.files:
        try:
            parts = list(package_parts(f))
        except (zipfile.BadZipFile, OSError) as e:
            out.append({"kind": "file", "file": f, "status": "unreadable", "message": str(e)})
            bad += 1
            continue
        counts = {"valid": 0, "invalid": 0, "no-schema": 0}
        for name, data in parts:
            r = validate_part(name, data, schemas, understood)
            r["file"] = f
            counts[r["status"]] += 1
            out.append(r)
        out.append({"kind": "summary", "file": f, **counts})
        if counts["invalid"]:
            bad += 1
    lines = [json.dumps(r) for r in out]
    if a.json:
        with open(a.json, "w") as fh:
            fh.write("\n".join(lines) + "\n")
    if not a.quiet:
        print("\n".join(lines))
    for r in out:
        if r["kind"] == "summary":
            print("%s: %d valid, %d invalid, %d no-schema" % (r["file"], r["valid"], r["invalid"], r["no-schema"]), file=sys.stderr)
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
