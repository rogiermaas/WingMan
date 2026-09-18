"""Bounded, read-only comparison of actual 787 TCAS, raw traffic calls and their timestamps."""
import datetime as dt
import json
import pathlib
import sys
import time
root=pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0,str(root/'artifacts/python'))
import websocket
view=int(sys.argv[1]) if len(sys.argv)>1 else 21
seconds=min(120,max(10,int(sys.argv[2]))) if len(sys.argv)>2 else 40
sock=websocket.create_connection(f'ws://127.0.0.1:19999/devtools/page/{view}',timeout=8,suppress_origin=True)
ident=0
def evaluate(code):
    global ident
    ident+=1
    sock.send(json.dumps({'id':ident,'method':'Runtime.evaluate','params':{'expression':code,'returnByValue':True}}))
    while True:
        reply=json.loads(sock.recv())
        if reply.get('id')!=ident:continue
        if reply.get('error') or reply.get('result',{}).get('wasThrown'):raise RuntimeError(str(reply))
        return json.loads(reply['result']['result']['value'])
start=(root/'tools/coherent-airtraffic-research-start.js').read_text(encoding='utf-8')
snapshot=(root/'tools/coherent-787-tcas-read.js').read_text(encoding='utf-8')
query='''(function(){var s=window.__escortAirResearch;
Object.keys(s.listeners).forEach(function(k){var v=s.listeners[k];if(v.ready)s.query(k,function(){return v.listener.call('GET_AIR_TRAFFIC');});});
s.query('global',function(){return Coherent.call('GET_AIR_TRAFFIC');});
return JSON.stringify({queued:true});})();'''
read='''(function(){var s=window.__escortAirResearch;return JSON.stringify({readAt:Date.now(),reads:s.reads,alternatives:s.alternatives});})();'''
records=[]
path=root/'artifacts/acquisition-research'/('tcas-comparison-'+dt.datetime.now(dt.timezone.utc).strftime('%Y%m%d-%H%M%S')+'.jsonl')
try:
    evaluate(start)
    time.sleep(1)
    evaluate((root/'tools/coherent-alternative-traffic-research.js').read_text(encoding='utf-8'))
    with path.open('w',encoding='utf-8') as output:
        deadline=time.monotonic()+seconds
        while time.monotonic()<deadline:
            evaluate(query)
            time.sleep(.4)
            row={'instrument':evaluate(snapshot),'queries':evaluate(read)}
            records.append(row);output.write(json.dumps(row)+'\n');output.flush()
            if len(records)%10==0:
                print(json.dumps({'samples':len(records),'TCAS':row['instrument']['tcasCount'],'shared':row['instrument']['sharedCount'],
                    'raw':{k:len(v.get('data',[])) if isinstance(v.get('data'),list) else v.get('status') for k,v in row['queries']['reads'].items()}}),flush=True)
            time.sleep(.6)
finally:
    try:evaluate('JSON.stringify({cleared:delete window.__escortAirResearch})')
    finally:sock.close()
print(json.dumps({'file':str(path),'samples':len(records),'alternatives':records[-1]['queries']['alternatives'] if records else None},indent=2))
