"""Read-only raster registration diagnostics, not anatomical contact inference.

Compare genuine alpha/bright upper-body centroids before and after the existing
ground-anchor registration. No source image or manifest pixels/values change.
The sole band is a measured candidate only: identifying a support leg requires
visual review, and a shared anchor/contact cannot establish foot locking.
"""
from pathlib import Path
import argparse
import json
import math
from PIL import Image


def centroid(points):
    total = sum(p[2] for p in points)
    if total == 0:
        return None
    return [sum(p[0] * p[2] for p in points) / total,
            sum(p[1] * p[2] for p in points) / total]


def vector_delta(a, b):
    return [round(b[0] - a[0], 3), round(b[1] - a[1], 3)]


def main():
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--atlas', type=Path, default=root / 'assets/rin/mountain-life.png')
    parser.add_argument('--manifest', type=Path, default=root / 'assets/manifest.json')
    parser.add_argument('--output', type=Path, default=root / 'output/climb-registration-diagnostic.json')
    args = parser.parse_args()
    manifest = json.loads(args.manifest.read_text(encoding='utf-8-sig'))
    atlas = Image.open(args.atlas).convert('RGBA')
    rows = []
    for i, (region, anchor, height) in enumerate(zip(manifest['ClimbRegions'], manifest['ClimbAnchors'], manifest['ClimbHeights'])):
        left = int(region[0] * atlas.width)
        top = int(region[1] * atlas.height)
        width = min(int(region[2] * atlas.width), atlas.width - left)
        cell_height = min(int(region[3] * atlas.height), atlas.height - top)
        cell = atlas.crop((left, top, left + width, top + cell_height))
        alpha = cell.getchannel('A')
        box = alpha.point(lambda a: 255 if a >= 90 else 0).getbbox()
        if box is None:
            raise ValueError(f'Empty cell {i}')
        l, t, r, b = box
        y0, y1 = t + int((b - t) * .23), t + int((b - t) * .65)
        opaque, bright, foot = [], [], []
        for y in range(t, b):
            for x in range(l, r):
                red, green, blue, a = cell.getpixel((x, y))
                if a < 180:
                    continue
                if y0 <= y < y1:
                    opaque.append((x + .5, y + .5, a))
                    if red > 175 and green > 150 and blue > 145 and max(red, green, blue) - min(red, green, blue) < 70:
                        bright.append((x + .5, y + .5, a))
                if y >= b - 5:
                    foot.append((x + .5, y + .5, a))
        upper, bright_body = centroid(opaque), centroid(bright)
        if upper is None:
            raise ValueError(f'No upper-body pixels in cell {i}')
        k = 150.0 / height
        def registered(point):
            return [round((point[0] - anchor[0]) * k, 3), round((point[1] - anchor[1]) * k, 3)] if point else None
        rows.append({
            'frame': i, 'opaque_box': list(box), 'cell_size': [width, cell_height],
            'edge_padding': [l, t, width-r, cell_height-b],
            'manifest_anchor_source_pixels': anchor, 'scale': k,
            'upper_alpha_centroid_source_pixels': upper,
            'bright_upper_proxy_source_pixels': bright_body,
            'registered_upper_alpha_centroid_logical': registered(upper),
            'registered_bright_upper_proxy_logical': registered(bright_body),
            'lowest_band_candidate_source_pixels': centroid(foot),
            'lowest_band_x_range': [min(p[0] for p in foot), max(p[0] for p in foot)] if foot else None,
            'same_anchor_and_contact': any(abs(c[0]-anchor[0]) < .01 and abs(c[1]-anchor[1]) < .01 for c in manifest['ClimbContacts'][i]),
        })
    transitions = []
    for group_start in (0, 12):
        for offset in range(12):
            a, b = rows[group_start + offset], rows[group_start + (offset + 1) % 12]
            raw = vector_delta(a['upper_alpha_centroid_source_pixels'], b['upper_alpha_centroid_source_pixels'])
            shift = vector_delta(a['registered_upper_alpha_centroid_logical'], b['registered_upper_alpha_centroid_logical'])
            transitions.append({'from': a['frame'], 'to': b['frame'],
                                'source_upper_delta': raw,
                                'registered_upper_delta_logical': shift,
                                'registered_upper_displacement_logical': round(math.hypot(*shift), 3)})
    ranked = sorted(transitions, key=lambda v: v['registered_upper_displacement_logical'], reverse=True)
    report = {'atlas': str(args.atlas), 'atlas_size': list(atlas.size),
              'scope': 'Measured raster centroids, not anatomical hips or independently verified foot contacts. Includes 11→0 and 23→12 cycle closure.',
              'frames': rows, 'transitions': transitions,
              'largest_registered_upper_displacements': ranked[:8],
              'touching_cell_edge': [r['frame'] for r in rows if min(r['edge_padding']) <= 1],
              'anchor_contact_identity_count': sum(r['same_anchor_and_contact'] for r in rows)}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({'report': str(args.output),
                      'largest_registered_upper_displacements': ranked[:5],
                      'touching_cell_edge': report['touching_cell_edge'],
                      'anchor_contact_identity_count': report['anchor_contact_identity_count']}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
