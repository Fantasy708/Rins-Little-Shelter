"""Register measured complete poses and device grips; never edit PNG pixels."""
import json
from pathlib import Path

root=Path(__file__).resolve().parents[1]
path=root/'assets/manifest.json'
m=json.loads(path.read_text(encoding='utf-8-sig'))
cols=[0,414,776,1131,1536]; rows=[0,355,691,1024]
feet=[[245.66,350],[172.85,350],[169.48,350],[162.66,350],
      [244.39,333],[172.39,332],[171.63,332],[165.01,333],
      [244.86,325],[175.22,326],[174.04,326],[167.86,326]]
heights=[340,340,340,340,328,327,327,328,320,322,321,322]
hands=[(186,231,295,231),(558,180,608,178),(908,172,965,175),(1258,161,1316,160),
       (209,501,265,502),(555,500,611,503),(910,506,972,507),(1267,499,1319,500),
       (209,848,265,849),(562,868,614,866),(919,890,966,889),(1233,899,1341,899)]
m.update(TabletAtlas='rin/tablet-life.png',
         TabletRegions=[[cols[i%4]/1536,rows[i//4]/1024,(cols[i%4+1]-cols[i%4])/1536,(rows[i//4+1]-rows[i//4])/1024] for i in range(12)],
         TabletFeet=feet,TabletHeights=heights,
         TabletHands=[[x-cols[i%4],y-rows[i//4],z-cols[i%4],w-rows[i//4]] for i,(x,y,z,w) in enumerate(hands)])
path.write_text(json.dumps(m,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
