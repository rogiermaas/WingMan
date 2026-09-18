"""Read-only SDK/source research helpers; saves bounded public source snapshots."""
import json
import pathlib
import sys
import urllib.request

DEST = pathlib.Path('artifacts/acquisition-research')
DEST.mkdir(parents=True, exist_ok=True)

def get(url):
    req = urllib.request.Request(url, headers={'User-Agent': 'EscortPlane2024 SDK research'})
    with urllib.request.urlopen(req, timeout=25) as response:
        return response.read(12 * 1024 * 1024).decode('utf-8-sig')

if sys.argv[1] == 'pages':
    data = json.loads(get('http://127.0.0.1:19999/pagelist.json'))
    print(json.dumps(data, indent=2))
elif sys.argv[1] == 'fetch':
    url, filename = sys.argv[2:4]
    text = get(url)
    path = DEST / pathlib.Path(filename).name
    path.write_text(text, encoding='utf-8')
    print(f'Saved {len(text)} characters to {path}')
elif sys.argv[1] == 'github-tree':
    repo = sys.argv[2]
    data = json.loads(get(f'https://api.github.com/repos/{repo}/git/trees/main?recursive=1'))
    (DEST / (repo.split('/')[-1] + '-tree.json')).write_text(json.dumps(data), encoding='utf-8')
    for item in data.get('tree', []):
        if any(term.lower() in item['path'].lower() for term in sys.argv[3:]):
            print(item['path'])
