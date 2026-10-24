"""Encode production test frames as review previews; never edits runtime art."""
import argparse
import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

p = argparse.ArgumentParser()
p.add_argument('--actors', type=Path, required=True)
p.add_argument('--mountain', type=Path, required=True)
p.add_argument('--out', type=Path, default=Path('docs/screenshots'))
a = p.parse_args()
a.out.mkdir(parents=True, exist_ok=True)
font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 16)

def actions(filename, entries):
    streams=[]
    for action,title in entries:
        folder=a.actors / (action+'-right-1.0')
        files=[]
        for phase in ('entry','hold','exit','end'):
            files.extend(sorted(folder.glob(phase+'-*.png')))
        if not files:
            raise RuntimeError('Missing actual rendered frames: '+str(folder))
        streams.append((title,files))
    # A fixed union crop keeps the true ground and scale while making the
    # actual production actor readable in the review animation.
    bounds=None
    for _,files in streams:
        for source in files:
            box=Image.open(source).getbbox()
            if box:
                bounds=box if bounds is None else (min(bounds[0],box[0]),min(bounds[1],box[1]),max(bounds[2],box[2]),max(bounds[3],box[3]))
    if bounds is None: raise RuntimeError('No rendered actor in preview stream')
    bounds=(bounds[0]-18,bounds[1]-18,bounds[2]+18,bounds[3]+18)
    frames=[]
    for i in range(max(len(files) for _,files in streams)+12):
        frame=Image.new('RGB',(300*len(streams),320),'#e8f0ed')
        d=ImageDraw.Draw(frame)
        for col,(title,files) in enumerate(streams):
            image=Image.open(files[min(i,len(files)-1)]).convert('RGBA').crop(bounds)
            image.thumbnail((290,284),Image.Resampling.LANCZOS)
            frame.paste(image,(col*300+(300-image.width)//2,30),image)
            d.text((col*300+12,2),title,font=font,fill='#354a4c')
        frames.append(frame)
    frames[0].save(a.out/filename,save_all=True,append_images=frames[1:],duration=33,loop=0,disposal=2,optimize=False)

actors_report=json.loads((a.actors/'results.json').read_text(encoding='utf-8-sig'))
if not actors_report['Passed']:
    raise RuntimeError('Actor render suite must finish and pass before encoding')
actions('action-bridges-ground.gif',[('knees','屈膝 · 抱膝 · 起身'),('lie','支撑 · 侧卧 · 回站'),('benchSit','坐下 · 起身')])
actions('action-bridges-upper.gif',[('sky','抬头 · 回正'),('tablet','拿板 · 持板 · 收手'),('catch','伸手 · 收回')])

report=json.loads((a.mountain/'results.json').read_text(encoding='utf-8-sig'))
if not report['Passed']:
    raise RuntimeError('Mountain render suite must finish and pass before encoding')
case=next(c for c in report['Results'] if c['Name']=='from-left-dpi-1.0-60fps')
labels={'approach':'沿山径走近','turnUp':'转向 · 收脚','ascendPrepare':'换重起步','ascend':'交替迈步登山',
        'summitSettle':'登顶 · 收脚回站','summitStand':'站稳','look':'山顶看天空','summitReady':'收回视线',
        'turnDown':'转身 · 回正站稳','descendPrepare':'下山起步','descend':'沿山径下山',
        'footSettle':'山脚 · 收脚回站','settle':'站稳','none':'恢复日常'}
frames=[]
for sample in case['Samples'][::3]:
    source=a.mountain/case['Name']/'scene'/('frame-%04d.png'%sample['Index'])
    image=Image.open(source).convert('RGBA')
    frame=Image.new('RGB',image.size,'#e8f0ed');frame.paste(image,(0,0),image)
    d=ImageDraw.Draw(frame)
    d.text((12,8),labels.get(sample['Phase'],sample['Phase']),font=font,fill='#293e47')
    frames.append(frame)
frames[0].save(a.out/'mountain-roundtrip.gif',save_all=True,append_images=frames[1:],duration=50,loop=0,disposal=2,optimize=False)
print('Encoded three previews from actual production Scene frames.')
