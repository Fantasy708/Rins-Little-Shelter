using LittleShelter;
using System.Text.Json;

int checks=0;
void Check(bool value,string name) { if (!value) throw new Exception("FAIL: "+name); checks++; }
SaveData Save(string scene="meadow",bool free=true,int days=20) {
    var s=new SaveData(); s.Settings.FreeMode=free; s.Settings.Music=false; s.Settings.Left=0; s.Settings.Top=0;
    s.CompanionDates=Enumerable.Range(0,days).Select(i=>new DateTime(2026,1,1).AddDays(i).ToString("yyyy-MM-dd")).ToList();
    World.Initialize(s); Scenes.Switch(s,scene); return s;
}
Behavior Actor(SaveData s) { var b=new Behavior(19); Scenes.PlaceActor(s,b); return b; }
string Persistent(SaveData s) => JsonSerializer.Serialize(s);
void Step(Behavior b,SaveData s,double duration) { while(duration>1e-8) { double d=Math.Min(.01,duration); b.Tick(d,s,false); duration-=d; } }
void Until(Behavior b,SaveData s,string phase) { for(int i=0;i<12000 && b.Nurture.Phase!=phase;i++) Step(b,s,.01); Check(b.Nurture.Phase==phase,"到达 "+phase); }
foreach(bool free in new[]{false,true}) foreach(string id in Scenes.All.Select(d=>d.Id)) {
    var s=Save(id,free); var before=Persistent(s);
    bool available=Scenes.ActionAvailable(s,Actions.Get("nurtureHug"));
    Check(available==(id=="meadow"),"默认五场景限制/满院子无空位 "+id+"/"+free);
    Check(Persistent(s)==before,"候选检查不动布置");
}
foreach(int day in new[]{0,1,2,6,7,9,10,20}) {
    var s=Save("meadow",false,day);
    Check(Scenes.ActionAvailable(s,Actions.Get("nurtureHug"))==(day>=10),"草坡成长门槛 "+day);
}
foreach(string id in new[]{"meadow"}) foreach(double x in new[]{210.0,240.0,285.0}) foreach(double y in new[]{280.0,305.0}) {
    var s=Save(id); foreach(var p in s.Layout) if (!Scenes.Foundation(p.Id) && p.Id!="grass") p.Hidden=true;
    var g=s.Layout.First(p=>p.Id==(id=="meadow" ? "meadowBase" : "grass")); g.X=x; g.Y=y;
    var before=Persistent(s); var b=Actor(s);
    Check(b.Nurture.Begin(s),"不同可见草地位置可播放 "+id+"/"+x+"/"+y); Until(b,s,"lower");
    var lower=new HashSet<int>(); while(b.Nurture.Phase=="lower") { lower.Add(b.Nurture.PoseFrame); Step(b,s,.02); }
    Check(lower.SetEquals(Enumerable.Range(0,10)),"实际下降时间访问十个专用姿势");
    Until(b,s,"rest"); Check(b.Nurture.PoseFrame==9,"停留使用俯卧姿势"); Until(b,s,"rise");
    var rise=new HashSet<int>(); while(b.Nurture.Phase=="rise") { rise.Add(b.Nurture.PoseFrame); Step(b,s,.02); }
    Check(rise.SetEquals(Enumerable.Range(10,10)),"实际起身时间访问十个专用姿势"); Until(b,s,"none");
    var home=Scenes.Home(s); Check(Math.Abs(b.X-home.X)<.01 && Math.Abs(b.Y-home.Y)<.01,"完成恢复当前场景落点");
    Check(Persistent(s)==before,"完整动作不改变存档/布置/故事/创造次数");
}
foreach(string phase in new[]{"approach","turning","stowing","lower","rest","rise","returning"}) foreach(string stop in new[]{"drag","create","tablet","scene","mode","grassHidden","space"}) {
    var s=Save(); var b=Actor(s); b.Start("walk",b.X-1,b.Y); b.Start("stand"); Check(b.Nurture.Begin(s),"中断前启动"); Until(b,s,phase);
    var dates=s.CompanionDates.ToArray(); string layouts=JsonSerializer.Serialize(new{s.GrowthLayout,s.FreeLayout,s.GrowthSceneLayouts,s.FreeSceneLayouts});
    if (stop=="drag") { b.Dragging=true; b.X=88; b.Y=155; b.Tick(.1,s,false); Check(b.X==88 && b.Y==155,"拖动优先、不覆盖手势落点"); }
    if (stop=="create") b.Create("flower");
    if (stop=="tablet") { if (b.Tablet.Active) b.Tablet.Cancel(s); b.Tablet.Open(s); }
    if (stop=="scene") { Scenes.Switch(s,"indoor"); b.Tick(.1,s,false); }
    if (stop=="mode") { s.Settings.FreeMode=!s.Settings.FreeMode; b.Tick(.1,s,false); }
    if (stop=="grassHidden") { s.Layout.First(p=>p.Id=="meadowBase").Hidden=true; b.Tick(.1,s,false); }
    if (stop=="space") { var g=s.Layout.First(p=>p.Id=="meadowBase"); g.X=25; b.Tick(.1,s,false); }
    Check(!b.Nurture.Active,"所有阶段正确取消 "+phase+"/"+stop);
    if (stop!="drag") Check(Math.Abs(b.Y-Scenes.Home(s).Y)<.01,"取消恢复当前场景地面");
    Check(s.CompanionDates.SequenceEqual(dates),"中断不改成长");
    if (stop is not ("grassHidden" or "space")) Check(JsonSerializer.Serialize(new{s.GrowthLayout,s.FreeLayout,s.GrowthSceneLayouts,s.FreeSceneLayouts})==layouts,"中断不改布局");
}
{
    var s=Save(); var b=Actor(s); b.Start("walk",b.X-1,b.Y); b.Start("stand");
    Check(b.Nurture.Begin(s),"左向进入"); Until(b,s,"turning");
    double x=b.X,y=b.Y; Check(b.Current.Id=="turn" && b.FacingLeft,"左向使用真实转身序列");
    Step(b,s,.2); Check(b.Nurture.Phase=="turning" && Math.Abs(b.X-x)<.01 && Math.Abs(b.Y-y)<.01,"转身留在落点，不能整张翻面/平移");
    Until(b,s,"lower"); Check(!b.FacingLeft,"转身完成后固定朝右");
    s=Save("outdoor"); foreach(var p in s.Layout) if(p.Id!="grass") p.Hidden=true;
    Check(!Actor(s).Nurture.Begin(s),"空院子草地也需容纳远侧展开的手，深度不足禁用");
}
{
    var s=Save(); var b=Actor(s); b.Start("benchSit"); Check(!b.Nurture.Begin(s),"家具上拒绝彩蛋");
    b.Start("stand"); b.Create("flower"); Check(!b.Nurture.Begin(s),"自主创造优先");
    b.Start("stand"); b.Tablet.Open(s,preselect:"flower"); for(int i=0;i<2000 && !b.Tablet.Interactive;i++) Step(b,s,.01);
    b.Tablet.Confirm(s); Check(!b.Nurture.Begin(s),"已经确认的平板创造优先"); b.Tablet.Cancel(s);
    b.Tablet.Open(s,page:"messages"); for(int i=0;i<2000 && !b.Tablet.Interactive;i++) Step(b,s,.01);
    Check(b.Nurture.Begin(s),"手动消息会话可以先收起再前往草地"); Until(b,s,"rest"); Check(!b.Tablet.Active,"俯卧时无平板重叠");
}
{
    var s=Save(); var b=Actor(s); b.Nurture.Begin(s); Until(b,s,"lower");
    b.Nurture.Tick(.95,s); Check(b.Nurture.PoseFrame==7,"慢帧按实际时间推进，不按帧计数");
    b.Nurture.Tick(1.0,s); Check(b.Nurture.Phase=="rest" && Math.Abs(b.Nurture.Elapsed-.75)<.011,"跨阶段时间余量保留");
    b.Nurture.Cancel(s); Step(b,s,300); Check(!b.Nurture.Active && b.Current.Id!="nurtureHug","五分钟自主行为不启动手动彩蛋");
}
{
    var s=Save(); s.Layout.First(p=>p.Id=="meadowBase").Hidden=true; Check(!Actor(s).Nurture.Begin(s),"隐藏草坡拒绝");
    s=Save("outdoor"); foreach(var p in s.Layout) if (p.Id!="grass") p.Hidden=true; s.Layout.First(p=>p.Id=="grass").Hidden=true; Check(!Actor(s).Nurture.Begin(s),"隐藏院子草地拒绝");
    s=Save(); var b=Actor(s); b.Nurture.Begin(s); Until(b,s,"rest");
    string data=Path.Combine("output/nurture-logic-qa",Guid.NewGuid().ToString("N")); var storage=new Storage(data); storage.Write(s); var loaded=storage.Load();
    Check(Persistent(loaded)==Persistent(s) && !Actor(loaded).Nurture.Active,"兼容版本1重启不恢复半段动作");
}
Console.WriteLine("All "+checks+" Nurture logic/placement/interruption checks passed.");
