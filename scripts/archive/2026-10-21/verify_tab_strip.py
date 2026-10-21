#!/usr/bin/env python3
"""Validate actual native tab-strip telemetry against independent XCTest whole-tab containers."""
import argparse,hashlib,json
from pathlib import Path

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('captures',type=Path);p.add_argument('--root',type=Path,default=Path(__file__).resolve().parents[3]);p.add_argument('--output',type=Path,required=True);a=p.parse_args();
 if not a.captures.is_dir():p.error('Private desktop captures unavailable; provide the original cohort directory. See scripts/archive/README.md.')
 root=a.root.resolve();
 if not (root/'captures/safari/2026-10-21').is_dir():p.error('Private canonical Safari evidence unavailable under --root; see scripts/archive/README.md.')
 results=[]
 for angle in (0,90,180,270):
  saved=json.loads((a.captures/f'body-{angle}.state.json').read_text());live=saved['nativeContainer'];scene=live['materialParameters']['referenceScene'];nativePath=root/scene['states']['initial']['nativeSource'];native=json.loads(nativePath.read_text());components=native['nativeComponents'];containers=[c['frame'] for c in components if c['identifier']=='TabBarItemTitleContainer' and abs(c['frame']['height']-32)<.01];actual=live['tabStripGeometryScenePoints'];errors=[];maxError=0
  if not live['tabStripVisible'] or live['chromeState']!='expanded':errors.append('Strip not visible in expanded mode')
  if len(actual)!=len(containers):errors.append('Tab count differs from original native title containers')
  active=[]
  for i,tab in enumerate(actual):
   visual=tab['visualFrame'];hit=tab['hitFrame'];exposed=tab['exposedFrame'];delta=min(max(abs(visual[k]-ref[k]) for k in ('x','y','width','height')) for ref in containers);maxError=max(maxError,delta)
   if delta>.01:errors.append('Whole-tab container differs')
   if i+1<len(actual) and exposed['x']+exposed['width']>actual[i+1]['visualFrame']['x']-3+.01:errors.append('Visible tab surfaces overlap')
   if tab['active']:
    active.append(tab);close=tab['closeFrame']
    if abs(visual['width']-(hit['width']+close['width']))>.01 or abs(visual['x']-close['x'])>.01 or abs(hit['x']-close['x']-close['width'])>.01:errors.append('Close button omitted from selected whole-tab bounds')
  if len(active)!=1:errors.append('Expected one selected tab')
  selected=active[0];results.append({'bodyRotationDegrees':angle,'passed':not errors,'source':str(nativePath.relative_to(root)),'sourceSha256':hashlib.sha256(nativePath.read_bytes()).hexdigest(),'selectedTitleHitWidth':selected['hitFrame']['width'],'selectedWholeTabWidth':selected['visualFrame']['width'],'closeWidth':selected['closeFrame']['width'],'maxVisualFrameErrorScenePoints':maxError,'errors':errors})
 report={'passed':all(r['passed'] for r in results),'scope':'Actual macOS tab-strip telemetry vs original XCTest TabBarItemTitleContainer and CloseTabBarItemButton; visual screenshot review covers shared track and selected styling. Optical colors/strokes remain approximations.','results':results};a.output.parent.mkdir(parents=True,exist_ok=True);a.output.write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2));return 0 if report['passed'] else 1
if __name__=='__main__':raise SystemExit(main())
