import json, collections, sys
f = sys.argv[1]
files = ok = 0
fail = collections.Counter()
ctrl = 0; withc = 0
types = collections.Counter(); levels = collections.Counter(); bst = collections.Counter(); defects = collections.Counter()
bound = unbound = ph = custom = 0; withcustom = 0
failmsg = {}
for l in open(f):
    r = json.loads(l); files += 1
    if not r['ok']:
        fail[r['error']] += 1; failmsg.setdefault(r['error'], r['message'][:120]); continue
    ok += 1
    if r['controls']: withc += 1
    ctrl += r['controls']; bound += r['bound']; unbound += r['unbound']; ph += r['placeholders']
    custom += r['customXmlParts']; withcustom += 1 if r['customXmlParts'] else 0
    types.update(r['types']); levels.update(r['levels']); bst.update(r['bindingStatus']); defects.update(r['defects'])
print(json.dumps(dict(files=files, ok=ok, failed=dict(fail), failmsg=failmsg, filesWithControls=withc, controls=ctrl, bound=bound, unbound=unbound,
  placeholders=ph, filesWithCustomXml=withcustom, customXmlParts=custom, types=dict(types), levels=dict(levels), bindingStatus=dict(bst), defects=dict(defects)), indent=1))
