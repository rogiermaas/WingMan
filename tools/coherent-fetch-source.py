"""Read bounded simulator UI source files via its local SDK inspector, without executing them."""
import json
import pathlib
import sys
import time
root = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(root / 'artifacts/python'))
import websocket

view, url, filename = sys.argv[1:4]
sock = websocket.create_connection(f'ws://127.0.0.1:19999/devtools/page/{int(view)}', timeout=8, suppress_origin=True)
ident = 0
def evaluate(code):
    global ident
    ident += 1
    sock.send(json.dumps({'id':ident,'method':'Runtime.evaluate','params':{'expression':code,'returnByValue':True}}))
    while True:
        reply = json.loads(sock.recv())
        if reply.get('id') != ident: continue
        if reply.get('error') or reply.get('result',{}).get('wasThrown'): raise RuntimeError(str(reply))
        return json.loads(reply['result']['result']['value'])
try:
    evaluate('''(function(){var s=window.__escortSourceFetch={status:'pending'};var x=new XMLHttpRequest();
    x.open('GET',URL,true);x.timeout=10000;x.onload=function(){s.http=x.status;s.status='done';
    s.length=x.responseText.length;s.text=x.responseText.length<=20000000?x.responseText:null;};x.onerror=x.ontimeout=function(){s.status='error';};
    x.send();return JSON.stringify({started:true});})()'''.replace('URL',json.dumps(url)))
    for _ in range(15):
        time.sleep(1)
        data = evaluate('JSON.stringify(window.__escortSourceFetch)')
        if data.get('status') != 'pending': break
    if data.get('http') != 200 or data.get('text') is None: raise RuntimeError(str({k:v for k,v in data.items() if k!='text'}))
    path = root / 'artifacts/acquisition-research' / pathlib.Path(filename).name
    path.write_text(data['text'],encoding='utf-8')
    print(json.dumps({'file':str(path),'characters':len(data['text'])}))
finally:
    try: evaluate('JSON.stringify({cleared:delete window.__escortSourceFetch})')
    finally: sock.close()
