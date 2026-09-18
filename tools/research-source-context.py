"""Bounded context search in captured SDK/UI source."""
import pathlib
import re
import sys
text = pathlib.Path(sys.argv[1]).read_text(encoding='utf-8')
for needle in sys.argv[2:]:
    hits = list(re.finditer(re.escape(needle),text,re.I))
    print('\nTERM',needle,'COUNT',len(hits))
    for match in hits[:12]:
        print(text[max(0,match.start()-160):match.end()+280].replace('\n',' '))
