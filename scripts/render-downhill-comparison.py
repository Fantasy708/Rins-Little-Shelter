"""Compare real ClimbContinuityTests exports; never generates character art.

Usage: python scripts/render-downhill-comparison.py <before> <after> <destination>
Requires Pillow for documentation only. The GIF keeps the supplied 60 Hz
clock at a 30 Hz export cadence; crops are enlarged solely for inspection.
"""
import json
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

before, after, destination = map(Path, sys.argv[1:4])
destination.mkdir(parents=True, exist_ok=True)
name = 'from-left-dpi-1.0-60fps'
cases = [next(c for c in json.loads((p / 'results.json').read_text(encoding='utf-8'))['Results']
              if c['Name'] == name) for p in (before, after)]
font_path = Path('C:/Windows/Fonts/msyh.ttc')
font = ImageFont.truetype(str(font_path), 18) if font_path.exists() else ImageFont.load_default()
small = ImageFont.truetype(str(font_path), 15) if font_path.exists() else font
background = (232, 237, 240)


def actor(root, sample):
    return Image.open(root / name / 'actor' / f"frame-{sample['Index']:04}.png").convert('RGBA')


def paste(canvas, im, xy):
    canvas.paste(im, xy, im.getchannel('A'))


sheet = Image.new('RGB', (1200, 440), background)
draw = ImageDraw.Draw(sheet)
for i, t in enumerate((.5533333333333335, 3.003333333333327)):
    for j, (root, case) in enumerate(zip((before, after), cases)):
        s = min((s for s in case['Samples'] if s['Phase'] == 'descend'),
                key=lambda s: abs(s['PhaseElapsed'] - t))
        x, y = s['Root']
        im = actor(root, s).crop((round(x - 70), round(y - 166), round(x + 80), round(y + 14)))
        paste(sheet, im.resize((300, 360)), ((i * 2 + j) * 300, 40))
        draw.text(((i * 2 + j) * 300 + 10, 10), f"{'修复前' if j == 0 else '修复后'} · 下山 {s['PhaseElapsed']:.3f}秒", font=font, fill=(35, 45, 55))
        draw.text(((i * 2 + j) * 300 + 10, 410), f"实际WPF角色绘图 · 第{s['Index']}帧", font=small, fill=(35, 45, 55))
sheet.save(destination / 'downhill-knee-before-after.png')

selected = [s for s in cases[0]['Samples'] if s['Phase'] in {'descendPrepare', 'descend', 'footSettle'}][::2]
frames = []
after_samples = {s['Index']: s for s in cases[1]['Samples']}
for s in selected:
    frame = Image.new('RGB', (960, 585), background)
    d = ImageDraw.Draw(frame)
    for j, root in enumerate((before, after)):
        sample = s if j == 0 else after_samples[s['Index']]
        scene = Image.open(root / name / 'scene' / f"frame-{sample['Index']:04}.png").convert('RGBA')
        paste(frame, scene, (j * 480, 30))
        x, y = sample['Root']
        legs = actor(root, sample).crop((round(x - 60), round(y - 82), round(x + 55), round(y + 18)))
        paste(frame, legs.resize((230, 200)), (j * 480 + 125, 380))
        d.text((j * 480 + 14, 4), f"{'修复前' if j == 0 else '修复后'} · {s['Phase']} {s['PhaseElapsed']:.2f}秒", font=font, fill=(35, 45, 55))
        d.text((j * 480 + 12, 390), '腿部放大', font=small, fill=(35, 45, 55))
    frames.append(frame.quantize(colors=192))
durations = [(round((i + 1) * 100 / 30) - round(i * 100 / 30)) * 10 for i in range(len(frames))]
frames[0].save(destination / 'downhill-knee-before-after.gif', save_all=True, append_images=frames[1:],
               duration=durations, loop=0, disposal=2, optimize=False)
print(f'{len(frames)} production frame pairs; {sum(durations) / 1000:.3f}s; {destination}')
