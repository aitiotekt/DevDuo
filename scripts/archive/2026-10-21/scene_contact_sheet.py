#!/usr/bin/env python3
"""Presentation-only paired contact sheets. Original evidence remains unchanged."""
import argparse,json
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('captures',type=Path);p.add_argument('--root',type=Path,default=Path(__file__).resolve().parents[3]);p.add_argument('--font',type=Path,help='Optional TrueType/OpenType font for contact sheets');p.add_argument('--output',type=Path,required=True);a=p.parse_args();
 if not a.captures.is_dir():p.error('Private desktop captures unavailable; provide the original cohort directory. See scripts/archive/README.md.')
 scenes=json.loads((a.root/'config/safari-scenes.json').read_text());a.output.mkdir(parents=True,exist_ok=True)
 font=ImageFont.truetype(str(a.font),24) if a.font else ImageFont.load_default(size=24);small=ImageFont.truetype(str(a.font),17) if a.font else ImageFont.load_default(size=17)
 for fold in ('closed','open','open-single'):
  for stage in ('initial','scrolled'):
   page=Image.new('RGB',(1640,2380),'#e8edf4');draw=ImageDraw.Draw(page);draw.text((24,15),f'{fold.upper()} / {stage}   Device Hub screen  |  DevDuo native window crop',fill='#182332',font=font)
   for row,scene in enumerate(s for s in scenes if s['fold']==('open' if fold=='open-single' else fold)):
    ref=scene['singleStates' if fold=='open-single' else 'states'][stage];native=Image.open(a.root/ref['image']).convert('RGBA');back=Image.new('RGBA',native.size,'#e8edf4');back.alpha_composite(native);native=back.convert('RGB');sceneKey=scene['id'].replace('open-','open-single-',1) if fold=='open-single' else scene['id'];desktop=Image.open(a.captures/f'{sceneKey}-{stage}.jpg').convert('RGB');mask=desktop.convert('L').point(lambda v:255 if v<45 else 0);ImageDraw.Draw(mask).rectangle((0,0,desktop.width,420),fill=0);box=mask.getbbox();desktop=desktop.crop(box) if box else desktop
    y=65+row*575;draw.text((24,y),f'Body {scene["bodyRotationDegrees"]} deg / scene {scene["sceneRotationDegrees"]} deg / {scene["width"]:g} x {scene["height"]:g}',fill='#182332',font=font)
    for column,img in enumerate((native,desktop)):
     img.thumbnail((775,510),Image.Resampling.LANCZOS);page.paste(img,(20+column*820+(775-img.width)//2,y+40+(510-img.height)//2))
   draw.text((24,2350),'Cropped and resized for review; scroll offsets differ. No photometric fidelity score; preserve original PNG/JPEG files.',fill='#526172',font=small);page.save(a.output/f'DevDuo-{fold}-{stage}-4-angles.png')
if __name__=='__main__':main()
