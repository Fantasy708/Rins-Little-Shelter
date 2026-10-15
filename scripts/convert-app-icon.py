"""Pack the delivered imagegen PNG into standard Windows ICO sizes.

This only performs the required format conversion and resampling. It does not
redraw or alter the icon design. Runtime and normal .NET builds use the supplied
ICO and do not require Python or Pillow.
"""
from pathlib import Path
from PIL import Image

root = Path(__file__).resolve().parent.parent
source = root / 'assets/app/rin-shelter.png'
sizes = [(size, size) for size in (16, 24, 32, 48, 64, 128, 256)]
with Image.open(source) as image:
    if image.mode != 'RGBA' or image.width != image.height:
        raise ValueError('Expected the square RGBA imagegen source PNG')
    image.save(source.with_suffix('.ico'), format='ICO', sizes=sizes)
with Image.open(source.with_suffix('.ico')) as image:
    if image.ico.sizes() != set(sizes):
        raise ValueError('ICO is missing required Windows sizes')
print('Application icon packed at 16/24/32/48/64/128/256 pixels.')
