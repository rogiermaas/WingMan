"""Summarize observed multiplayer coordinates and derive motion; never sends controls."""
import datetime as dt
import json
import math
import pathlib
import statistics
import sys
root = pathlib.Path(__file__).resolve().parents[1]
name = sys.argv[1] if len(sys.argv)>1 else 'Lasterminater'
path = max((root/'dist/EscortPlane2024/logs').glob('*.jsonl'),key=lambda p:p.stat().st_mtime)
rows=[]; label=None; own=None; commands=0
for line in path.read_text(encoding='utf-8-sig').splitlines():
    try:r=json.loads(line)
    except json.JSONDecodeError:continue
    d=r['data']
    if r['kind']=='own_telemetry':own=d
    if r['kind']=='guidance_output':commands+=1
    if r['kind']=='nameplate_snapshot':
        label=next((x for x in d['Contacts'] if x['Name'].casefold()==name.casefold()),None)
    if r['kind']=='coherent_traffic' and d.get('Name','').casefold()==name.casefold():
        rows.append({'time':dt.datetime.fromisoformat(d['ReceivedAt']).timestamp(),'data':d,'label':label,'own':own})
if not rows:
    print(json.dumps({'name':name,'samples':0,'lastLabel':label}));sys.exit(0)
first,last=rows[0],rows[-1]
recent=[r for r in rows if r['time']>=last['time']-15]
lat0=math.radians(recent[0]['data']['Latitude']);lon0=recent[0]['data']['Longitude'];latdeg=recent[0]['data']['Latitude']
t=[r['time']-recent[0]['time'] for r in recent]
def slope(values):
    mt=statistics.mean(t);mv=statistics.mean(values);den=sum((x-mt)**2 for x in t)
    return sum((x-mt)*(y-mv) for x,y in zip(t,values))/den if den else None
east=slope([6371008.8*math.cos(lat0)*math.radians(r['data']['Longitude']-lon0) for r in recent])
north=slope([6371008.8*math.radians(r['data']['Latitude']-latdeg) for r in recent])
up=slope([r['data']['RawAltitude'] for r in recent])
gaps=[b['time']-a['time'] for a,b in zip(rows,rows[1:])]
segments=[];segment=[rows[0]]
for r in rows[1:]:
    if r['time']-segment[-1]['time']>5:
        segments.append(segment);segment=[]
    segment.append(r)
segments.append(segment)
last_move=rows[0]['time']
for a,b in zip(rows,rows[1:]):
    if any(abs(b['data'][k]-a['data'][k])>1e-8 for k in ('Latitude','Longitude','RawAltitude')):last_move=b['time']
result={'name':name,'log':str(path),'samples':len(rows),'spanSeconds':last['time']-first['time'],
    'first':first['data'],'last':last['data'],'ageSeconds':dt.datetime.now(dt.timezone.utc).timestamp()-last['time'],
    'firstLabel':first['label'],'lastLabel':last['label'],'currentLabel':label,
    'maxGapSeconds':max(gaps,default=0),'medianGapSeconds':statistics.median(gaps) if gaps else None,
    'motionWindowSeconds':t[-1], 'estimatedGroundSpeedKnots':math.hypot(east,north)/0.514444 if east is not None else None,
    'estimatedTrueTrack':math.degrees(math.atan2(east,north))%360 if east is not None else None,
    'estimatedVerticalSpeedFpm':up*60/.3048 if up is not None else None,'guidanceOutputs':commands,
    'unchangedPositionSeconds':last['time']-last_move,
    'segments':[{'firstAt':s[0]['data']['ReceivedAt'],'lastAt':s[-1]['data']['ReceivedAt'],
        'samples':len(s),'firstLabel':s[0]['label'],'lastLabel':s[-1]['label']} for s in segments]}
out=root/'artifacts/acquisition-research'/('live-track-'+name+'.json')
out.write_text(json.dumps(result,indent=2),encoding='utf-8')
print(json.dumps(result,indent=2))
