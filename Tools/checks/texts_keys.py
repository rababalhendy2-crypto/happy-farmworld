"""Fails when a language table in Core/Texts.cs has the same key twice (that crashes the game at start)."""
import re, sys, collections
s = open(sys.argv[1] if len(sys.argv) > 1 else 'Assets/Scripts/Core/Texts.cs', encoding='utf-8').read()
blocks = re.split(r'new\s*(?:Dictionary<string,\s*string>)?\s*\(\)\s*\{|new Dictionary<string, string>\s*\{', s)[1:]
bad = 0
for i, b in enumerate(blocks):
    b = b.split('};')[0]
    keys = re.findall(r'\{\s*"([A-Za-z0-9_]+)"\s*,\s*"', b)
    dup = [k for k, c in collections.Counter(keys).items() if c > 1]
    print('table', i, len(keys), 'keys', 'DUPLICATES: ' + ', '.join(dup) if dup else 'ok')
    bad += len(dup)
sys.exit(1 if bad else 0)
