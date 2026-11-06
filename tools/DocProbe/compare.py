#!/usr/bin/env python3
"""Compares DocProbe's text files with Apache Tika's JSON output (information only).

Usage: compare.py <DocProbe out dir> <Tika out dir> [suffix: ours|hub]
Per file the score is the word-multiset overlap F-measure (2*common/(|a|+|b|)) over whitespace-split lower-case words. Prints the
distribution and the worst files.
"""
import collections, json, os, re, sys

ours_dir, tika_dir = sys.argv[1], sys.argv[2]
suffix = sys.argv[3] if len(sys.argv) > 3 else 'ours'


def words(s):
    return re.findall(r'\w+', s.lower())


rows = []
for fn in sorted(os.listdir(ours_dir)):
    if not fn.endswith('.' + suffix + '.txt'):
        continue
    name = fn.split('.')[0]
    tj = os.path.join(tika_dir, name + '.doc.json')
    if not os.path.exists(tj):
        continue
    try:
        t = json.load(open(tj))[0].get('X-TIKA:content', '') or ''
    except Exception:
        continue
    a = collections.Counter(words(open(os.path.join(ours_dir, fn), encoding='utf8').read()))
    b = collections.Counter(words(t))
    na, nb = sum(a.values()), sum(b.values())
    if na + nb == 0:
        rows.append((1.0, name, na, nb))
        continue
    common = sum((a & b).values())
    rows.append((2 * common / (na + nb), name, na, nb))

rows.sort()
n = len(rows)
buckets = [(1.0, 1.0), (0.99, 1.0), (0.95, 0.99), (0.9, 0.95), (0.7, 0.9), (0.0, 0.7)]
print('files compared:', n)
print('exactly equal word multisets:', sum(1 for r in rows if r[0] == 1.0))
for lo, hi in buckets[1:]:
    c = sum(1 for r in rows if lo <= r[0] < hi or (hi == 1.0 and r[0] == 1.0 and False))
    print(f'  [{lo:.2f}, {hi:.2f}): {c}')
if n:
    print('median', rows[n // 2][0], 'p10', rows[n // 10][0])
print('worst:')
for r in rows[:25]:
    print('  %.3f %s ours=%d tika=%d' % r)
