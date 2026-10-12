"""Register the observed atlas cells and attachment landmarks; no bitmap edits."""
import json
from pathlib import Path
root = Path(__file__).resolve().parents[1]
path = root / 'assets/manifest.json'
m = json.loads(path.read_text(encoding='utf-8-sig'))
cols=[0,320,596,872,1122]; rows=[0,319,606,880,1145,1402]
regions=[[cols[c]/1122,rows[r]/1402,(cols[c+1]-cols[c])/1122,(rows[r+1]-rows[r])/1402] for r in range(5) for c in range(4)]
hips=[(188,211),(448,213),(711,214),(987,211),(180,494),(447,509),(710,498),(1000,500),(178,783),(446,787),(715,781),(999,786),(179,1053),(447,1054),(715,1055),(994,1054),(180,1294),(445,1288),(714,1294),(1003,1300)]
hands=[(141,208,226,211),(362,51,500,122),(652,111,762,109),(927,115,1038,110),(119,409,229,421),(394,435,501,428),(664,425,776,423),(929,415,1037,420),(121,702,233,704),(402,704,507,703),(666,702,773,703),(937,704,1042,703),(130,965,223,974),(410,980,517,983),(666,975,775,975),(935,980,1043,990),(137,1265,236,1266),(403,1249,501,1257),(666,1236,771,1242),(970,1310,1045,1311)]
m.update(ScenesAtlas='world/scenes.png',SwingAtlas='rin/swing-life.png',FamilyAtlas='rin/family-memory.png',SwingRegions=regions,
         SwingHips=[[x-cols[i%4],y-rows[i//4]] for i,(x,y) in enumerate(hips)],
         SwingHands=[[x-cols[i%4],y-rows[i//4],z-cols[i%4],w-rows[i//4]] for i,(x,y,z,w) in enumerate(hands)])
m['SceneRegions']=[[c/3,0,1/3,540/1024] for c in range(3)]+[[c/3,550/1024,1/3,474/1024] for c in range(3)]
familyrows=[0,378,720,974,1254]
m['FamilyRegions']=[[c/4,familyrows[r]/1254,.25,(familyrows[r+1]-familyrows[r])/1254] for r in range(4) for c in range(4)]
m['ChildHips']=[[170,169],[170,169],[169,166],[170,169]]
m['ChildHands']=[[125.4,90.5,215.1,92.1],[126.4,88,220.3,94.8],[130.6,92.9,216.1,96.1],[115.7,94.3,215.2,93.5]]
loopcols=[0,268,532,790,1046,1302,1536]; looprows=[0,265,531,779,1024]
# The exaggerated first row is intentionally unused. Rows 2-4 have complete
# edges and small, quiet changes, played forward and backward without a jump.
m['LoopAtlas']='rin/swing-loop.png'
m['LoopRegions']=[[loopcols[c]/1536,looprows[r]/1024,(loopcols[c+1]-loopcols[c])/1536,(looprows[r+1]-looprows[r])/1024] for r in range(1,4) for c in range(6)]
m['LoopHips']=[[x-loopcols[c],y-looprows[r]] for r,y in enumerate([0,445,708,962]) if r>0 for c,x in enumerate([126,380,633,887,1142,1398])]
m['LoopHands']=[[x-loopcols[c],y-looprows[r],x+98-loopcols[c],y-looprows[r]] for r,y in enumerate([0,369,637,885]) if r>0 for c,x in enumerate([76,329,583,837,1093,1347])]
m['BedroomAtlas']='world/bedroom.png'
m['BedroomRegions']=[[x/1536,y/1024,w/1536,h/1024] for x,y,w,h in [[0,0,678,500],[678,0,511,507],[1190,0,346,520],[0,530,575,494],[575,508,365,516]]]
m['BedSeatAnchor']=[.48,.57]
m['BedroomWindowPane']=[.24,.035,.48,.91]
m['BedroomGlassRegions']=[[(x-1190)/325,(y-17)/499,w/325,h/499] for x,w in [(1300,10),(1312,42),(1360,43),(1408,7)] for y,h in [(54,54),(112,77),(192,92),(288,93),(385,68)]]
path.write_text(json.dumps(m,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
