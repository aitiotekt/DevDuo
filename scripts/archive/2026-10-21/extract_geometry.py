#!/usr/bin/env python3
"""Generate UI geometry from saved local Device Hub source paths, never millimeters."""
import argparse,json,pathlib,re
ROOT=pathlib.Path(__file__).resolve().parents[3]
def svg_tokens(svg):return re.findall(r'[MLCZ]|[-+]?(?:\d*\.)?\d+(?:[eE][-+]?\d+)?',svg)
def transform(svg,fn):
    tokens=svg_tokens(svg);out=[];i=0
    while i<len(tokens):
        op=tokens[i];i+=1;out.append(op)
        for _ in range({'M':1,'L':1,'C':3,'Z':0}[op]):
            x,y=fn(float(tokens[i]),float(tokens[i+1]));i+=2;out.extend([f'{x:.6f}',f'{y:.6f}'])
    return ' '.join(out)
def pdf_path(text):
    stack=[];out=[]
    for t in re.findall(r'[-+]?(?:\d*\.)?\d+(?:[eE][-+]?\d+)?|[A-Za-z*]+',text):
        if t in ('m','l','c'):
            n={'m':2,'l':2,'c':6}[t];out.append(t.upper()+' '+' '.join(stack[-n:]));stack=[]
        elif t=='h':out.append('Z');stack=[]
        else:stack.append(t)
    return ' '.join(out)
def generate(source):
    masks=json.loads((source/'mask-paths.json').read_text());closed=masks['1C896A2B-F0D7-405C-8D0F-66E4B80AD044.pdf']['svgPath'];opened=masks['BF0DC480-5EE2-4EC1-B02B-C75E94759832.pdf']['svgPath']
    closed=transform(closed,lambda x,y:(x,678-y));opened=transform(opened,lambda x,y:(x,1780-y))
    text=(source/'phone15-composite-path.txt').read_text();start=text.index('416.6473 706 m');end=text.index('h W',start)+1
    outer=transform(pdf_path(text[start:end]),lambda x,y:(x,706-y))
    hinge='M 9 6 L 16 6 L 16 699 L 9 699 C 5.134007 699 2 695.866 2 692 L 2 13 C 2 9.134007 5.134007 6 9 6 Z'
    common={'outerCornerRadiusDip':None,'shellInsetDip':None,'geometryStatus':'Inner mask and shell proportions from local Device Hub vector artwork; physical dimensions unknown','source':'measurements/devicehub-source/','units':'local vector canvas units; screen CSS aspect from browser/metadata, never mm'}
    return [{**common,'id':'closed','label':'Closed · primary screen 1','screenWidthCssPx':466,'screenHeightCssPx':678,'maskCanvasWidth':466,'maskCanvasHeight':678,'screenMaskPath':closed,'shellCanvasWidth':514,'shellCanvasHeight':706,'screenX':25,'screenY':14,'screenCanvasWidth':466,'screenCanvasHeight':678,'shellPath':outer,'shellStrokeWidth':0,'hingePath':hinge},
    {**common,'id':'open','label':'Open · secondary screen 3','screenWidthCssPx':669,'screenHeightCssPx':951,'maskCanvasWidth':1252,'maskCanvasHeight':1780,'screenMaskPath':opened,'shellCanvasWidth':660,'shellCanvasHeight':924,'screenX':17,'screenY':17,'screenCanvasWidth':626,'screenCanvasHeight':890,'shellPath':transform(opened,lambda x,y:(x/2+17,y/2+17)),'shellStrokeWidth':34,'hingePath':None}]
def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--source',type=pathlib.Path,default=ROOT/'measurements/devicehub-source');p.add_argument('--output',type=pathlib.Path,required=True);a=p.parse_args();
    if not (a.source/'mask-paths.json').is_file():p.error('Local Device Hub artwork extraction is unavailable. Use checked-in config/device-profiles.json, or provide --source. See docs/en/007-REFERENCE-DATA.md.')
    a.output.write_text(json.dumps(generate(a.source),indent=2)+'\n');print(a.output)
if __name__=='__main__':main()
