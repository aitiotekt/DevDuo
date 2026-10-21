#!/usr/bin/env python3
"""Rebuild the 8 Safari scene references from canonical native captures, never physical units."""
from pathlib import Path
import argparse,json,hashlib,datetime
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--root',type=Path,default=Path(__file__).resolve().parents[3],help='Evidence root with captures/ and measurements/')
parser.add_argument('--output',type=Path,required=True)
args=parser.parse_args()
root=args.root.resolve()
if not (root/'measurements/session-2026-10-21/selected-browser-records.jsonl').is_file():
 parser.error('Private source evidence is unavailable. Use the checked-in config/safari-scenes.json, or provide --root with the original local evidence. See docs/en/007-REFERENCE-DATA.md.')
records=[json.loads(line) for line in (root/'measurements/session-2026-10-21/selected-browser-records.jsonl').read_text().splitlines()]
def tab_geometry(native):
 components=native['nativeComponents']
 containers=[c['frame'] for c in components if c['identifier']=='TabBarItemTitleContainer' and abs(c['frame']['height']-32)<.01]
 closes=[c['frame'] for c in components if c['identifier']=='CloseTabBarItemButton']
 tabs=[]
 for c in components:
  if not c['identifier'].startswith('TabBarTab?'):continue
  hit=c['frame'];active='isActive=true' in c['identifier']
  containing=[v for v in containers if abs(v['y']-hit['y'])<.01 and v['x']<=hit['x']+.01 and v['x']+v['width']>=hit['x']+hit['width']-.01]
  if not containing:raise RuntimeError('Missing native whole-tab title container')
  visual=min(containing,key=lambda v:abs(v['x']-hit['x'])+abs(v['width']-hit['width']))
  close=next((v for v in closes if abs(v['x']-visual['x'])<.01 and abs(v['y']-visual['y'])<.01),None) if active else None
  if active and (not close or abs(close['width']+hit['width']-visual['width'])>.01):raise RuntimeError('Selected tab does not reconcile close button and title hit region')
  tabs.append({'frame':hit,'visualFrame':visual,'active':active,'closeFrame':close,'visualFrameSource':'native TabBarItemTitleContainer; original TabBarTab hit frame retained separately'})
 return tabs
scenes=[]
for fold in ('closed','open'):
 for degrees,angle in zip((0,90,180,270),('portrait','landscapeRight','portraitUpsideDown','landscapeLeft')):
  pose=f'{fold}-{angle}';folder=root/'captures/safari/2026-10-21'/pose;states={}
  for state in ('initial','scrolled'):
   nativePath=folder/f'{pose}-{state}-components.json';capturePath=folder/f'{pose}-{state}-selected-display-capture.json'
   native=json.loads(nativePath.read_text());capture=json.loads(capturePath.read_text());record=next(r for r in records if r['labels']['pose']==pose and r['observedState']==state)
   width=capture['widthPx']/3;height=capture['heightPx']/3
   frames={c['identifier']:c['frame'] for c in native['nativeComponents'] if c['identifier'] in ('BackButton','SidebarButton','NewTabButton','TabOverviewButton')}
   search=next((c for c in native['nativeComponents'] if c['identifier'].startswith('SearchFieldItemView?')),None)
   if search:frames['address']=search['frame']
   elif state=='initial':
    menu=next(c for c in native['nativeComponents'] if c['identifier']=='MoreMenuButton');frames['address']={**menu['frame'],'width':334}
   tabs=tab_geometry(native)
   states[state]={'frames':frames,'tabs':tabs,'viewportWidth':record['viewport']['innerWidth'],'viewportHeight':record['viewport']['innerHeight'],'safeArea':record['safeAreaCssPx'],'nativeSource':str(nativePath.relative_to(root)),'nativeSha256':hashlib.sha256(nativePath.read_bytes()).hexdigest(),'image':str((folder/capture['image']).relative_to(root)),'imageSha256':capture['sha256']}
  rotation=({0:0,90:90,180:90,270:270} if fold=='closed' else {0:90,90:0,180:270,270:180})[degrees]
  scenes.append({'id':pose,'fold':fold,'bodyRotationDegrees':degrees,'sceneRotationDegrees':rotation,'width':width,'height':height,'mappingStatus':'Observed framebuffer scene. Closed upside-down retains the preceding landscapeRight scene in this clockwise capture sequence; orientation history matters. Symmetric open mask rotation signs are a representation, not hardware evidence.','states':states})
# Supplement the existing 8 scenes with independently captured one-tab open states.
local=root/'measurements/session-local/records.jsonl'
single_records=[json.loads(line) for line in local.read_text().splitlines()] if local.exists() else []
for scene in scenes:
 if scene['fold']!='open':continue
 pose=scene['id'].replace('open-','open-single-',1);folder=root/'captures/safari/2026-10-21'/pose/'verified'
 if not (folder/'session.json').exists():continue
 session=json.loads((folder/'session.json').read_text())
 if not session.get('captureStagesValid'):continue
 single={}
 for state in ('initial','scrolled'):
  path=folder/f'{pose}-{state}-components.json';native=json.loads(path.read_text());capture=json.loads((folder/f'{pose}-{state}-selected-display-capture.json').read_text())
  matching=[r for r in single_records if r['labels']['pose']==pose and r['observedState']==state and session['browserCaptureSessionId'] in r['environment']['url']]
  if not matching:raise RuntimeError('Missing browser record for '+pose+' '+state)
  captureTime=datetime.datetime.fromisoformat(capture['capturedAt']);record=min(matching,key=lambda r:abs((datetime.datetime.fromisoformat(r['clientTimestamp'].replace('Z','+00:00'))-captureTime).total_seconds()));frames={c['identifier']:c['frame'] for c in native['nativeComponents'] if c['identifier'] in ('BackButton','SidebarButton','NewTabButton','TabOverviewButton')};search=next(c for c in native['nativeComponents'] if c['identifier'].startswith('SearchFieldItemView?'));frames['address']=search['frame']
  single[state]={'frames':frames,'tabs':[],'viewportWidth':record['viewport']['innerWidth'],'viewportHeight':record['viewport']['innerHeight'],'safeArea':record['safeAreaCssPx'],'browserRecordId':record['id'],'nativeSource':str(path.relative_to(root)),'nativeSha256':hashlib.sha256(path.read_bytes()).hexdigest(),'image':str((folder/capture['image']).relative_to(root)),'imageSha256':capture['sha256']}
 scene['singleStates']=single
output=args.output;output.write_text(json.dumps(scenes,indent=2)+'\n');print(f'{len(scenes)} scenes / {sum(len(s["states"])+len(s.get("singleStates",{})) for s in scenes)} states -> {output}')
