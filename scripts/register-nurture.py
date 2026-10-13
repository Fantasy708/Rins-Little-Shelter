"""Measured source regions and grounded pose origins; does not edit PNG pixels."""
import json
from pathlib import Path
root=Path(__file__).resolve().parents[1]
boxes=[
[110,9,268,298],[388,14,564,296],[665,58,854,291],[954,96,1161,284],[1244,119,1492,278],
[36,310,288,498],[329,324,570,502],[609,345,886,504],[907,339,1225,509],[1252,304,1520,515],
[44,519,284,742],[318,518,544,749],[618,507,875,750],[944,504,1202,731],[1293,505,1462,724],
[77,755,233,1005],[378,756,568,994],[678,753,872,999],[981,721,1174,1009],[1278,716,1438,1013]
]
origins_x=[222,506,775,1050,1352,175,477,755,1070,1368,175,440,765,1070,1370,168,495,778,1080,1380]
# Grounded palms/knees/sandals, measured from newly authored RIGHT-facing poses.
contacts=[[[212, 291], [238, 284]], [[491, 283], [522, 279]], [[760, 285], [789, 275]], [[1000, 269], [1110, 277], [1130, 257]], [[1350, 260], [1301, 266], [1441, 268]], [[60, 479], [242, 475], [160, 469]], [[352, 482], [548, 442]], [[630, 474], [864, 414]], [[938, 487], [1209, 451], [973, 393]], [[1282, 490], [1328, 351], [1506, 439]], [[65, 720], [262, 647]], [[342, 727], [521, 664]], [[666, 731], [839, 677]], [[966, 705], [1178, 680], [1066, 697]], [[1310, 704], [1440, 686]], [[117, 989], [178, 991]], [[422, 975], [532, 977]], [[728, 978], [784, 953]], [[1036, 996], [1100, 998]], [[1365, 1000], [1400, 1001]]]
manifest_path=root/'assets/manifest.json'
m=json.loads(manifest_path.read_text(encoding='utf-8-sig'))
m['NurtureGrassAtlas']='world/nurture-grass.png'
m['NurtureGrassRegions']=[[0,0,1,640/1024],[0,640/1024,1,384/1024]]
m['NurtureAtlas']='rin/nurture-life.png'
m['NurtureRegions']=[[(l-2)/1536,(t-2)/1024,(r-l+4)/1536,(b-t+4)/1024] for l,t,r,b in boxes]
m['NurtureAnchors']=[[x-l+2,b-t] for x,(l,t,r,b) in zip(origins_x,boxes)]
m['NurtureStandingHeights']=[289]*15+[289,289,289,288,297]
m['NurtureContacts']=[[[x-l+2,y-t+2] for x,y in points] for points,(l,t,r,b) in zip(contacts,boxes)]
manifest_path.write_text(json.dumps(m,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('Registered two grass textures and twenty complete authored poses.')
