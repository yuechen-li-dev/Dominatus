#!/usr/bin/env python3
from __future__ import annotations
import re, sys
from pathlib import Path

root = Path(__file__).resolve().parents[2]
link_re = re.compile(r'(?<!!)\[[^\]]*\]\(([^)]+)\)')
errors=[]
for md in sorted(root.rglob('*.md')):
    if any(part in {'.git','bin','obj'} for part in md.parts):
        continue
    text=md.read_text(encoding='utf-8', errors='ignore')
    text = re.sub(r'`[^`]*`', '', text)
    for m in link_re.finditer(text):
        target=m.group(1).strip()
        if not target or target.startswith(('#','http://','https://','mailto:')):
            continue
        target=target.split('#',1)[0]
        if not target:
            continue
        if target.startswith('<') and target.endswith('>'):
            target=target[1:-1]
        p=(md.parent / target).resolve()
        try:
            p.relative_to(root)
        except ValueError:
            errors.append((md, target, 'escapes repo'))
            continue
        if not p.exists():
            errors.append((md, target, 'missing'))
if errors:
    for md,target,why in errors:
        print(f'{md.relative_to(root)}: {target} -> {why}')
    sys.exit(1)
print('All internal Markdown links resolve.')
