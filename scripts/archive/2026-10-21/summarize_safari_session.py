#!/usr/bin/env python3
"""Join validated XCTest sessions with actual browser POST records by pose and time."""
import argparse,csv,datetime,hashlib,itertools,json,pathlib,struct
from devduo_tools.png_compare import read_png
from urllib.parse import parse_qs,urlsplit
from zoneinfo import ZoneInfo,ZoneInfoNotFoundError
ROOT=pathlib.Path(__file__).resolve().parents[3]
def instant(value):return datetime.datetime.fromisoformat(value.replace('Z','+00:00'))
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--local-date',type=datetime.date.fromisoformat,help='Explicit date when an IANA timezone database is unavailable');p.add_argument('--captures',type=pathlib.Path,required=True);p.add_argument('--records',type=pathlib.Path,required=True);p.add_argument('--output',type=pathlib.Path,required=True);args=p.parse_args();
 if not args.captures.is_dir() or not args.records.is_file():p.error('Private capture/session records unavailable; supply original --captures and --records. See scripts/archive/README.md.')
 args.output.mkdir(parents=True,exist_ok=True)
 records=[json.loads(line) for line in args.records.read_text().splitlines() if line.strip()]
 def path(f):
  try:return str(f.resolve().relative_to(ROOT.resolve()))
  except ValueError:return str(f.resolve())
 selected=[];nativeRows=[];missing=[]
 def session(file,appearance=False):
  m=json.loads(file.read_text());pose=m['pose'];base=file.parent
  if m.get('testExitCode')!=0 or m.get('orientationBefore')!=m.get('orientationAfter') or not m.get('captureStagesValid'):
   missing.append(path(file)+': failed test or changed orientation');return None
  if m.get('captureMethod')!='simctl-explicit-display-at-xctest-milestone':
   missing.append(path(file)+': no validated explicit-display milestone screenshots');return None
  milestoneFile=base/'capture-milestones.jsonl'
  if not milestoneFile.exists():missing.append(path(file)+': no screenshot milestones');return None
  milestones=[json.loads(line) for line in milestoneFile.read_text().splitlines() if line.strip()]
  sessionId=m.get('browserCaptureSessionId')
  if not sessionId:missing.append(path(file)+': no unique browser capture session');return None
  candidates=[r for r in records if r.get('labels',{}).get('browser')=='Safari' and r['labels'].get('pose')==pose and parse_qs(urlsplit(r.get('environment',{}).get('url','')).query).get('captureSession')==[sessionId] and instant(m['startedAt'])<=instant(r['receivedAt'])<=instant(m['finishedAt'])]
  entry={'pose':pose,'foldMode':m['foldMode'],'physicalOrientation':m['orientationBefore'],'webAssetSha256':m['webAssetSha256'],'probeSourceSha256':m.get('probeSourceSha256'),'testExitCode':m['testExitCode'],'captureMethod':m['captureMethod'],'browserCaptureSessionId':sessionId,'session':path(file),'states':[],'video':path(base/'transition.mov') if (base/'transition.mov').exists() else None}
  for state in ('initial','scrolled'):
   matches=[r for r in candidates if (r['viewport']['scrollY']==0 if state=='initial' else r['viewport']['scrollY']>0)]
   if not matches:missing.append(pose+'/'+state);continue
   native=json.loads((base/f'{pose}-{state}-components.json').read_text());png=base/f'{pose}-{state}.png'
   markers=[v for v in milestones if v.get('pose')==pose and v.get('state')==state]
   if not markers or not png.exists():missing.append(pose+'/'+state+': no explicit screenshot');continue
   marker=markers[-1]
   if marker.get('outcome')!='valid' or marker.get('browserCaptureSessionId')!=sessionId or marker.get('displayId')!=m['displayId'] or marker.get('image')!=png.name:
    missing.append(pose+'/'+state+': invalid screenshot receipt or display/session mismatch');continue
   if native.get('captureSessionId')!=sessionId or native.get('labels',{}).get('pose')!=pose or native.get('labels',{}).get('state')!=state:
    missing.append(pose+'/'+state+': native components belong to another session or stage');continue
   r=min(matches,key=lambda x:abs((instant(x['receivedAt'])-instant(marker['capturedAt'])).total_seconds()));actualHash=hashlib.sha256(png.read_bytes()).hexdigest()
   if marker.get('sha256')!=actualHash:missing.append(pose+'/'+state+': screenshot hash mismatch');continue
   w,h,pixels=read_png(png)
   if (w,h)!=(marker.get('widthPx'),marker.get('heightPx')):
    missing.append(pose+'/'+state+': screenshot dimensions disagree with receipt');continue
   nonblack=sum(1 for i in range(0,len(pixels),4) if pixels[i+3]>0 and max(pixels[i:i+3])>8)
   if nonblack < w*h*.01:missing.append(pose+'/'+state+': black screenshot rejected');continue
   selected.append(r)
   allowed={'Back','Show Bookmarks','Bookmarks','New tab','Tabs','Page Menu','Address','refresh','Reload Page'}
   identifiers={'BackButton','SidebarButton','NewTabButton','TabOverviewButton','MoreMenuButton','ReloadButton','TabBarItemTitle','TabBarItemTitleContainer','SearchFieldItemView'}
   components=[c for c in native['nativeComponents'] if c['label'] in allowed or c['identifier'].split('?')[0] in identifiers]
   entry['states'].append({'state':state,'measurementRecordId':r['id'],'receivedAt':r['receivedAt'],'actualScrollYCssPx':r['viewport']['scrollY'],'screenReportedCssPx':r['screen'],'viewport':r['viewport'],'safeAreaCssPx':r['safeAreaCssPx'],'colorScheme':r['environment']['colorScheme'],'applicationFramePoints':native['applicationFramePoints'],'nativeDeviceOrientationRaw':native['deviceOrientationRaw'],'imagePixels':{'width':w,'height':h},'screenshotSha256':actualHash,'framebufferDisplayId':marker.get('displayId'),'framebufferCapturedAt':marker.get('capturedAt'),'telemetryDeltaSeconds':(instant(r['receivedAt'])-instant(marker['capturedAt'])).total_seconds(),'nonblackPixelCount':nonblack,'image':path(png),'nativeComponentsJson':path(base/f'{pose}-{state}-components.json'),'safariAccessibilityComponents':components})
   for c in components:nativeRows.append({'pose':pose,'appearance':r['environment']['colorScheme'],'state':state,'label':c['label'],'identifier':c['identifier'],'elementType':c['elementType'],**c['frame'],'hittable':c['hittable'],'units':'XCTest screen points; accessibility/hit frame, not optical glass contour'})
  return entry
 entries=[e for f in sorted(args.captures.glob('*/session.json')) if (e:=session(f))]
 appearances=[e for f in sorted((args.captures/'appearance').glob('*/session.json')) if (e:=session(f,True))]
 expected={m+'-'+o for m,o in itertools.product(['closed','partial','open'],['portrait','landscapeRight','portraitUpsideDown','landscapeLeft'])}
 absent=sorted(expected-{e['pose'] for e in entries});timezone=selected[0]['environment'].get('timezone','UTC') if selected else 'UTC'
 date=None;dateStatus='no browser records'
 if args.local_date:date=args.local_date.isoformat();dateStatus='explicit --local-date'
 elif selected:
  try:date=instant(selected[0]['clientTimestamp']).astimezone(ZoneInfo(timezone)).date().isoformat();dateStatus='browser timezone with local IANA database'
  except ZoneInfoNotFoundError:dateStatus='timezone database unavailable; local date unknown; use --local-date'
 report={'schemaVersion':1,'device':'iPhone Duo','dateLocal':date,'dateLocalStatus':dateStatus,'timezone':timezone,'capturePoses':len(entries),'captureStatePairs':sum(len(e['states'])==2 for e in entries),'entries':entries,'appearanceComparisons':appearances,'missingSamples':missing,'missingMainPoses':absent,'mainMatrixComplete':not missing and not absent,'selectedBrowserRecordCount':len(selected),'nativeComponentRowCount':len(nativeRows),'observationCaveats':['Body orientation, native sensor orientation, XCTest applicationFramePoints, browser screen.orientation and image pixels are separate observations. Native fields can lag; never force equality.','Initial scrollY0 can retain compact chrome from earlier gestures. The current probe expands the page menu; older session history and source hashes remain visible.','Accessibility rectangles may describe interaction frames, text fields, or title contents, not the optical glass surface; labels can change after app restart, and compact controls may be absent from accessibility. DOM rects describe page elements only.','Browser POST and XCTest JSON/screenshot are sequential independent samples, not atomic compositor frames.','PNG differences are observations of changing content/chrome; they do not certify desktop-to-Safari visual equality.'],'unmeasured':['keyboard for the main posture matrix','reverse-scroll for the main posture matrix','other Safari toolbar preferences','dark appearance beyond explicitly listed sessions','physical body/screen/bezel dimensions and hardware radii in mm','physical hinge angle and partial-fold perspective model','Liquid Glass blur/refraction/material constants','Windows WebView2 runtime behavior'],'physical':{'bodyWidthMm':None,'bodyHeightMm':None,'screenWidthMm':None,'screenHeightMm':None,'bezelWidthMm':None,'hardwareInsideCornerRadiusMm':None,'hardwareOutsideCornerRadiusMm':None,'hingeAngleDegrees':None}}
 (args.output/'coverage.json').write_text(json.dumps(report,indent=2,ensure_ascii=False)+'\n');(args.output/'selected-browser-records.jsonl').write_text(''.join(json.dumps(r,ensure_ascii=False)+'\n' for r in selected))
 with (args.output/'native-components.csv').open('w') as f:
  w=csv.DictWriter(f,fieldnames=['pose','appearance','state','label','identifier','elementType','x','y','width','height','hittable','units']);w.writeheader();w.writerows(nativeRows)
 lines=['| Fold / physical orientation | Initial viewport CSS px | Scrolled viewport CSS px | scrollY CSS px | Safe area T/R/B/L CSS px |','|---|---|---|---:|---|']
 for e in entries:
  if len(e['states'])!=2:continue
  a,b=e['states'];v=a['viewport'];z=b['viewport'];s=a['safeAreaCssPx'];lines.append(f"| {e['pose']} | {v['innerWidth']}×{v['innerHeight']} | {z['innerWidth']}×{z['innerHeight']} | {b['actualScrollYCssPx']} | {'/'.join(str(s[k]) for k in ['top','right','bottom','left'])} |")
 (args.output/'viewport-table.md').write_text('\n'.join(lines)+'\n');print(json.dumps({'mainMatrixComplete':report['mainMatrixComplete'],'poses':len(entries),'statePairs':report['captureStatePairs'],'selectedBrowserRecords':len(selected),'nativeComponentRows':len(nativeRows),'missing':missing,'missingMainPoses':absent},ensure_ascii=False))
if __name__=='__main__':main()
