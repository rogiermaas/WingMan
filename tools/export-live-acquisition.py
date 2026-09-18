"""Export raw observed traffic and latest label text separately for review."""
import csv
import json
import pathlib
import sys
root=pathlib.Path(__file__).resolve().parents[1]
source=pathlib.Path(sys.argv[1]) if len(sys.argv)>1 else max((root/'dist/EscortPlane2024/logs').glob('*.jsonl'),key=lambda p:p.stat().st_mtime)
dest=root/'artifacts/acquisition-research'
traffic=[];labels=[];exceptions={};controls=[]
for line in source.read_text(encoding='utf-8-sig').splitlines():
    try:r=json.loads(line)
    except json.JSONDecodeError:continue
    d=r['data']
    if r['kind']=='coherent_traffic':
        traffic.append({'receivedAt':d['ReceivedAt'],'sourceTime':d['SourceTime'],'trafficId':d['TrafficId'],
            'name':d['Name'],'model':d['Model'],'latitude':d['Latitude'],'longitude':d['Longitude'],
            'altitudeMeters':d['RawAltitude'],'altitudeFeet':d['RawAltitude']/.3048,
            'reportedHeadingDegrees':d['Heading'],'reportedOnGroundUnreliable':d['IsOnGround']})
    elif r['kind']=='nameplate_snapshot':
        labels=[dict(receivedAt=r['timestamp'],**{k:v for k,v in x.items() if k!='LabelId'}) for x in d['Contacts']]
    elif r['kind']=='simconnect_exception':
        key=str(d.get('code'))+': '+str(d.get('operation'));exceptions[key]=exceptions.get(key,0)+1
    elif r['kind'] in ('follower_engaged','follower_stopped','guidance_output','autopilot_command'):controls.append(r)
for suffix,records in [('traffic',traffic),('labels',labels)]:
    path=dest/(source.stem+'-'+suffix+'.csv')
    if records:
        with path.open('w',encoding='utf-8-sig',newline='') as f:
            writer=csv.DictWriter(f,fieldnames=list(records[0]));writer.writeheader();writer.writerows(records)
        print(str(path))
summary={'source':str(source),'trafficSamples':len(traffic),'positionedNames':sorted({r['name'] for r in traffic}),
    'labels':len(labels),'exceptionsByOperation':exceptions,
    'guidanceOutputs':sum(r['kind']=='guidance_output' for r in controls),
    'engagementEvents':[r for r in controls if r['kind'] in ('follower_engaged','follower_stopped')]}
(dest/(source.stem+'-summary.json')).write_text(json.dumps(summary,indent=2),encoding='utf-8')
print(json.dumps(summary,indent=2))
