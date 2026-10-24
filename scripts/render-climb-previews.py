"""Encode production ClimbContinuityTests PNGs; does not generate actor art.

Usage: python scripts/render-climb-previews.py output/climb-fix/continuous-delivery
Requires Pillow only for this optional documentation export, not the app.
"""
from pathlib import Path
import json
import sys
from PIL import Image

root = Path(sys.argv[1])
report = json.loads((root / 'results.json').read_text(encoding='utf-8'))
case = next(r for r in report['Results'] if r['Name'] == 'from-left-dpi-1.0-60fps')
source = root / case['Name'] / 'scene'
destination = Path(__file__).resolve().parents[1] / 'docs' / 'screenshots'
destination.mkdir(parents=True, exist_ok=True)

for filename, phases in [('climb-continuity.gif', None),
                         ('climb-gait-up.gif', {'ascend', 'summitSettle'}),
                         ('climb-gait-down.gif', {'descend', 'footSettle'})]:
    samples = [s for s in case['Samples'] if phases is None or s['Phase'] in phases]
    selected = samples[::2]
    frames = []
    # GIF centiseconds: 30/30/40ms distribution preserves 30Hz mean speed.
    durations = [(round((i + 1) * 100 / 30) - round(i * 100 / 30)) * 10
                 for i in range(len(selected))]
    for sample in selected:
        rgba = Image.open(source / f"frame-{sample['Index']:04}.png").convert('RGBA')
        background = Image.new('RGB', rgba.size, (235, 241, 244))
        background.paste(rgba, mask=rgba.getchannel('A'))
        frames.append(background.quantize(colors=160))
    durations[-1] += 300
    frames[0].save(destination / filename, save_all=True,
                   append_images=frames[1:], duration=durations,
                   loop=0, disposal=2, optimize=False)
    print(filename, len(frames), 'production Scene frames', sum(durations) / 1000, 'seconds')
