#!/usr/bin/env python3
"""Compare captured desktop telemetry with independent canonical Device Hub records."""
import argparse,hashlib,itertools,json
from pathlib import Path

def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def overlap(a,b):return max(0,min(a['x']+a['width'],b['x']+b['width'])-max(a['x'],b['x']))*max(0,min(a['y']+a['height'],b['y']+b['height'])-max(a['y'],b['y']))
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('captures',type=Path);p.add_argument('--root',type=Path,default=Path(__file__).resolve().parents[3]);p.add_argument('--include-single',action='store_true');p.add_argument('--output',type=Path,required=True);a=p.parse_args();
 if not a.captures.is_dir():p.error('Private desktop captures unavailable; provide the original cohort directory. See scripts/archive/README.md.')
 root=a.root.resolve();
 if not (root/'captures/safari/2026-10-21').is_dir():p.error('Private canonical Safari evidence unavailable under --root; see scripts/archive/README.md.')
 scenes=json.loads((root/'config/safari-scenes.json').read_text());results=[];failures=[]
 cases=[(scene,stage,False) for scene in scenes for stage in ('initial','scrolled')]
 if a.include_single:cases += [(scene,stage,True) for scene in scenes if scene['fold']=='open' for stage in ('initial','scrolled')]
 for scene,stage,single in cases:
  sceneKey=scene['id'].replace('open-','open-single-',1) if single else scene['id'];key=f'{sceneKey}-{stage}';file=a.captures/f'{key}.state.json';image=a.captures/f'{key}.jpg';reference=scene['singleStates' if single else 'states'][stage];nativePath=root/reference['nativeSource'];native=json.loads(nativePath.read_text());result={'key':key,'reference':reference['nativeSource'],'referenceSha256':digest(nativePath),'bodyRotationDegrees':scene['bodyRotationDegrees'],'sceneRotationDegrees':scene['sceneRotationDegrees']};errors=[]
  if digest(nativePath)!=reference['nativeSha256'] or digest(root/reference['image'])!=reference['imageSha256']:errors.append('Canonical source hash mismatch')
  if not file.exists():errors.append('Missing desktop state');result['errors']=errors;results.append(result);failures+=errors;continue
  saved=json.loads(file.read_text());live=saved['nativeContainer'];rects=live.get('widgetFramesScenePoints',live.get('widgetFramesDip',{}));result.update(stateSha256=digest(file),screenshotSha256=digest(image) if image.exists() else None,actualViewport=live['actualPageViewport'],referenceViewport={'width':reference['viewportWidth'],'height':reference['viewportHeight']},renderScale=live.get('renderScale'),maxButtonFrameErrorScenePoints=0)
  if not image.exists():errors.append('Missing original window screenshot')
  if saved['requestedRotation']!=scene['bodyRotationDegrees'] or saved.get('referenceScene')!=scene['id']:errors.append('Body/reference mapping differs')
  if bool(live.get('tabStripVisible'))!=(scene['fold']=='open' and stage=='initial' and not single):errors.append('Tab strip visibility differs')
  if live['chromeState']!=('expanded' if stage=='initial' else 'compact'):errors.append('Wrong chrome state')
  for dimension in ('width','height'):
   if abs(live['actualPageViewport'][dimension]-reference['viewport'+dimension.title()])>1:errors.append('Actual DOM viewport '+dimension+' differs')
   if abs(live['requestedSceneCssPx'][dimension]-scene[dimension])>1:errors.append('Scene dimensions differ')
  for component in native['nativeComponents']:
   ident=component['identifier']
   if ident not in ('BackButton','SidebarButton','NewTabButton','TabOverviewButton'):continue
   if ident not in rects:errors.append('Missing '+ident);continue
   delta=max(abs(rects[ident][k]-component['frame'][k]) for k in ('x','y','width','height'));result['maxButtonFrameErrorScenePoints']=max(result['maxButtonFrameErrorScenePoints'],delta)
   if delta>1:errors.append(ident+' frame differs by '+str(delta))
  result['overlappingWidgetPairs']=[(x,y) for (x,rx),(y,ry) in itertools.combinations(rects.items(),2) if overlap(rx,ry)>.1]
  if result['overlappingWidgetPairs']:errors.append('Visible widgets overlap')
  expectedAddress=reference['frames'].get('address')
  if expectedAddress:
   actual=live.get('addressHitFrameScenePoints',live['addressHitFrameDip']);result['addressFrameErrorScenePoints']=max(abs(actual[k]-expectedAddress[k]) for k in expectedAddress)
   if result['addressFrameErrorScenePoints']>1:errors.append('Address frame differs')
  result['errors']=errors;result['passed']=not errors;results.append(result);failures += [key+': '+e for e in errors]
 report={'schemaVersion':1,'passed':not failures and len(results)==(24 if a.include_single else 16),'sceneCount':len(scenes),'stateCount':len(results),'passedStateCount':sum(r.get('passed',False) for r in results),'scope':'Real macOS desktop DOM viewport, native widget frames, body/scene mapping, screenshot presence and canonical hashes. Not a pixel/optical or physical hardware equivalence test.','limits':['Desktop DPR and safe-area env() remain native; open Safari safe bottom 34 is recorded, not spoofed.','Closed 180-degree sample retains the preceding 90-degree scene; capture history matters.','Glass, Wi-Fi/status artwork and tab content are approximate; tab slot geometry is capture-specific.','Windows cross compilation does not validate Windows runtime rendering.'],'results':results,'failures':failures};a.output.parent.mkdir(parents=True,exist_ok=True);a.output.write_text(json.dumps(report,indent=2)+'\n');print(json.dumps({k:report[k] for k in ('passed','sceneCount','stateCount','passedStateCount','failures')},indent=2));return 0 if report['passed'] else 1
if __name__=='__main__':raise SystemExit(main())
