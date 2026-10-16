"""Measure new complete poses and register contacts; never modify PNG pixels."""
from pathlib import Path
import json
from collections import deque
from PIL import Image
root=Path(__file__).resolve().parents[1]
image=Image.open(root/'assets/rin/mountain-life.png').convert('RGBA')
width,height=image.size
regions=[];anchors=[];contacts=[];measurements=[]
alpha_image=image.getchannel('A')
row_counts=[sum(alpha_image.getpixel((x,y))>=90 for x in range(width)) for y in range(height)]
row_edges=[0]
for split in range(1,4):
 centre=round(split*height/4);candidates=range(centre-22,centre+23)
 minimum=min(row_counts[y] for y in candidates);quiet=[y for y in candidates if row_counts[y]==minimum]
 row_edges.append(quiet[len(quiet)//2])
row_edges.append(height)
for row in range(4):
 for col in range(6):
  left=round(col*width/6);top=row_edges[row];right=round((col+1)*width/6);bottom=row_edges[row+1]
  cell=image.crop((left,top,right,bottom));alpha=cell.getchannel('A')
  mask=alpha.point(lambda a:255 if a>=90 else 0);box=mask.getbbox()
  if box is None:raise ValueError('Empty climbing pose')
  l,t,r,b=box
  # Register the torso independently of the soles. Choosing whichever foot is
  # lowest as a shared root can move an otherwise steady upper body by a full
  # stride on each frame. Measure pale blouse pixels at the hip/waist band.
  torso=[];torso_y=[]
  for y in range(round(t+(b-t)*.42),round(t+(b-t)*.65)):
   for x in range(l,r):
    red,green,blue,a=cell.getpixel((x,y))
    if a>=180 and min(red,green,blue)>150 and abs(red-green)<45 and abs(green-blue)<45:torso.append(x);torso_y.append(y)
  if not torso:raise ValueError('Cannot measure independent blouse/hip origin')
  root_x=sum(torso)/len(torso)
  # Independently find the visible two lower-leg/sandal components. No pixel
  # editing; only real opaque source soles become contact coordinates.
  low=max(t,b-58);remaining={(x,y) for y in range(low,b) for x in range(l,r) if alpha.getpixel((x,y))>=180};groups=[]
  while remaining:
   start=remaining.pop();queue=deque([start]);component=[start]
   while queue:
    x,y=queue.popleft()
    for point in [(x-1,y),(x+1,y),(x,y-1),(x,y+1)]:
     if point in remaining:remaining.remove(point);queue.append(point);component.append(point)
   if len(component)>=30:groups.append(component)
  groups=sorted(groups,key=len,reverse=True)[:2];soles=[]
  for component in groups:
   floor_y=max(y for x,y in component);floor=[x for x,y in component if y>=floor_y-3]
   soles.append([round(sum(floor)/len(floor),2),floor_y+1])
  if not soles:raise ValueError('No independently measured opaque sandal')
  # Fixed hip-to-ground projection for this short-step character design. The
  # ground origin does not switch to a different lifted foot from one frame to
  # the next. Source sole coordinates remain independent and are verified
  # against the terrain by the production raster checks.
  root_y=sum(torso_y)/len(torso_y)+110
  regions.append([left/width,top/height,(right-left)/width,(bottom-top)/height])
  anchors.append([round(root_x,2),round(root_y,2)]);contacts.append(soles)
  measurements.append({'frame':row*6+col,'cell':[left,top,right,bottom],'opaque_box':[l,t,r,b],'torso_origin':[round(root_x,2),round(root_y,2)],'independent_source_soles':soles})
path=root/'assets/manifest.json';manifest=json.loads(path.read_text(encoding='utf-8-sig'))
manifest.update(MountainImage='world/mountain.png',PolarImage='world/polar.png',AuroraImage='world/polar-aurora.png',ClimbAtlas='rin/mountain-life.png',ClimbRegions=regions,ClimbAnchors=anchors,ClimbHeights=[245]*24,ClimbContacts=contacts)
snow=Image.open(root/'assets/rin/polar-life.png').convert('RGBA');sw,sh=snow.size
snow_regions=[];snow_anchors=[]
for row in range(2):
 for col in range(6):
  l=round(col*sw/6);t=round(row*sh/2);r=round((col+1)*sw/6);b=round((row+1)*sh/2)
  cell=snow.crop((l,t,r,b));a=cell.getchannel('A');box=a.point(lambda v:255 if v>=90 else 0).getbbox()
  if box is None:raise ValueError('Empty polar pose')
  points=[(x,y) for y in range(box[3]-5,box[3]) for x in range(box[0],box[2]) if a.getpixel((x,y))>=180]
  anchor=[round(sum(x for x,y in points)/len(points),2),max(y for x,y in points)+1]
  snow_regions.append([l/sw,t/sh,(r-l)/sw,(b-t)/sh]);snow_anchors.append(anchor)
manifest.update(PolarPoseAtlas='rin/polar-life.png',PolarPoseRegions=snow_regions,PolarPoseAnchors=snow_anchors,PolarPoseHeights=[360]*12)
path.write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
output=root/'output/outdoor-registration.json';output.parent.mkdir(exist_ok=True);output.write_text(json.dumps({'source_size':[width,height],'standing_reference_height':245,'poses':measurements},indent=2),encoding='utf-8')
print('Registered 24 full climbing poses, measured opaque sandal contacts, and two terrain canvases.')
