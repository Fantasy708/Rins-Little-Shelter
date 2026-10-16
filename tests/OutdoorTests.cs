using LittleShelter;
using System.Text.Json;

int checks=0;
string Snapshot<T>(T value) => JsonSerializer.Serialize(value,new JsonSerializerOptions { NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });
void Check(bool result,string label) { if (!result) throw new Exception(label); checks++; Console.WriteLine("PASS: "+label); }
SaveData State(int days=20,bool free=false,string scene="mountain") {
    var s=new SaveData(); s.CompanionDates=Enumerable.Range(0,days).Select(d=>new DateTime(2026,1,1).AddDays(d).ToString("yyyy-MM-dd")).ToList();
    World.Initialize(s); s.Settings.FreeMode=free; Scenes.Switch(s,scene); return s;
}
Behavior Actor(SaveData s) { var b=new Behavior(7); Scenes.PlaceActor(s,b); return b; }
void Advance(Behavior b,SaveData s,double seconds,double slice=.02) {
    for(double t=0;t<seconds;t+=slice) b.Tick(Math.Min(slice,seconds-t),s,false);
}
void Until(Behavior b,SaveData s,string phase) {
    for(int i=0;i<10000 && b.Journey.Phase != phase;i++) b.Tick(.01,s,false);
    Check(b.Journey.Phase == phase,"时间轴到达 "+phase);
}
foreach(var (scene,day) in new[]{("mountain",12),("polar",16)}) {
    var before=State(day-1,scene:"cloud");
    Check(!Scenes.Switch(before,scene),scene+"解锁前不可进入");
    var exact=State(day,scene:"cloud"); var original=Snapshot(exact.GrowthLayout);
    Check(exact.SceneId == "cloud" && Scenes.Unlocked(exact,scene),scene+"准确陪伴日解锁且不强制切换");
    Check(Scenes.Switch(exact,scene) && exact.Layout.Count>0,scene+"进入初始化独立场景布置");
    Check(Snapshot(exact.GrowthLayout) == original,scene+"不改既有院子布局");
    var p=exact.Layout.First(v=>!Scenes.Foundation(v.Id)); bool savedHidden=!p.Hidden; p.Hidden=savedHidden; p.X=412;
    var freeOriginal=exact.FreeSceneLayouts[scene].Single(v=>v.Id == p.Id).Clone();
    exact.Settings.FreeMode=true; Check(Scenes.Switch(exact,scene) && exact.Layout.Any(v=>v.Id == p.Id && v.X == freeOriginal.X && v.Y == freeOriginal.Y && v.Hidden == freeOriginal.Hidden),scene+"自由布局保留原有位置和可见性，与成长隔离");
    exact.Settings.FreeMode=false; Check(exact.Layout.Any(v=>v.Id == p.Id && v.X == 412 && v.Hidden == savedHidden),scene+"成长布局位置与隐藏状态恢复");
    var instant=State(0,true,scene); Check(instant.SceneId == scene,scene+"自由模式立即开放");
}
Check(!Scenes.MemoriesUnlocked(State(19)) && Scenes.MemoriesUnlocked(State(20)),"新增场景不提前开放第20日记忆");
Check(!Story.LetterAvailable(State(16)),"进入极地不会提前开放父亲来信");
var mountain=State(); var b=Actor(mountain); string snapshot=Snapshot(mountain);
Check(b.Journey.Begin(mountain,"mountainClimb"),"手动启动山径攀登");
Until(b,mountain,"ascend");
Check(b.Journey.DedicatedPose && b.Current.Id == "mountainClimb" && !b.FacingLeft,"登山使用专用姿势并先完成真实转身");
double x=b.X,y=b.Y; Advance(b,mountain,.1);
Check(b.X == x && b.Y == y,"每步抬膝准备时保留支撑脚接触点");
Advance(b,mountain,.22);
Check(b.X>x && b.Y<y && b.Journey.PoseFrame is >=0 and <12,"转移重心时沿坡前进并推进上山姿势");
Until(b,mountain,"look");
Check(b.Journey.AtSummit && b.Current.Id == "sky" && b.X == 320 && b.Y == 178,"到达真实峰顶并安静远望");
Advance(b,mountain,2);
Check(b.X == 320 && b.Y == 178 && b.Journey.Active,"峰顶观察不被普通回家规则拖走");
Until(b,mountain,"descend");
Check(b.FacingLeft && b.Journey.Direction == -1 && b.Journey.PoseFrame is >=12 and <24,"下山前真实转身并选专用下坡姿势");
Until(b,mountain,"settle"); Advance(b,mountain,.51);
Check(!b.Journey.Active && b.X == Scenes.Home(mountain).X && b.Y == Scenes.Home(mountain).Y,"完成下山在山脚站稳恢复日常");
Check(Snapshot(mountain) == snapshot,"攀登全程不修改布置进度和故事");
var coarse=Actor(mountain); coarse.Journey.Begin(mountain,"mountainClimb"); Until(coarse,mountain,"ascend");
coarse.Tick(2,mountain,false);
Check(Math.Abs(coarse.Journey.Elapsed-2)<1e-8 && coarse.Journey.RouteProgress>.4,"专用攀登按实际时间推进不按帧数延长");
coarse.Journey.Cancel(mountain);
b=Actor(mountain); b.Journey.Begin(mountain,"mountainClimb"); Until(b,mountain,"look");
Check(!b.Tablet.Open(mountain,true) && b.Journey.AtSummit && mountain.ChoiceCounts.Count == 0,"主动邀请不会打断登顶或消耗每日邀请次数");
Check(b.Tablet.Open(mountain,false,"messages") && !b.Journey.Active && b.Y == Scenes.Home(mountain).Y,"用户主动消息入口优先取消山顶动作并回到地面持板");
Check(!b.Journey.CanBegin(mountain,"mountainClimb"),"持板界面活动时不重叠启动攀登"); b.Tablet.Cancel(mountain);
b=Actor(mountain); b.Journey.Begin(mountain,"mountainClimb"); Until(b,mountain,"ascend");
mountain.Settings.FreeMode=true; b.Tick(.1,mountain,false);
Check(!b.Journey.Active && b.Y == Scenes.Home(mountain).Y,"模式切换取消旧山径且不恢复半段动作"); mountain.Settings.FreeMode=false;
foreach(var phase in new[]{"approach","ascend","look","turnDown","descend","settle"}) {
    b=Actor(mountain); b.Journey.Begin(mountain,"mountainClimb"); if (phase != "approach") Until(b,mountain,phase);
    b.Start("smile"); Advance(b,mountain,.2);
    Check(!b.Journey.Active && b.Current.Id == "smile" && b.X == Scenes.Home(mountain).X && b.Y == Scenes.Home(mountain).Y,"外部动作中断 "+phase+" 后回到当前场景落点");
}
foreach(var phase in new[]{"ascend","look","descend"}) {
    b=Actor(mountain); b.Journey.Begin(mountain,"mountainClimb"); Until(b,mountain,phase);
    b.Dragging=true; double dragX=b.X,dragY=b.Y; b.Tick(.1,mountain,false);
    Check(!b.Journey.Active && b.X == dragX && b.Y == dragY,"拖动中断 "+phase+"不覆盖用户手势位置");
}
b=Actor(mountain); b.Journey.Begin(mountain,"mountainClimb"); Until(b,mountain,"look"); Scenes.Switch(mountain,"polar"); b.Tick(.02,mountain,false);
Check(!b.Journey.Active && b.X == Scenes.Home(mountain).X && b.Y == Scenes.Home(mountain).Y,"切换场景中断山峰动作并使用新场景落点");
var polar=mountain;
foreach(string id in new[]{"auroraWatch","snowTouch"}) {
    b=Actor(polar); Check(b.Journey.Begin(polar,id),"极地专属动作可启动 "+id); Until(b,polar,id == "auroraWatch" ? "watch" : "snow");
    var home=Scenes.Home(polar); Advance(b,polar,3); Check(b.X == home.X && b.Y == home.Y,"极地观察/接雪不整人平移 "+id);
    b.Click(polar); Check(!b.Journey.Active && b.Current.Id == "stand","点击可结束极地动作 "+id);
}
foreach(string scene in new[]{"cloud","indoor","outdoor","meadow","swingTree"}) {
    var s=State(20,true,scene); b=Actor(s);
    foreach(string id in new[]{"mountainClimb","auroraWatch","snowTouch"}) Check(!b.Journey.CanBegin(s,id),scene+"禁止不合场景的专属动作 "+id);
}
mountain=State(); b=Actor(mountain); var foundation=mountain.Layout.First(p=>p.Id == "mountainBase");
foundation.Hidden=true; Check(!b.Journey.CanBegin(mountain,"mountainClimb"),"山基隐藏时不攀登"); foundation.Hidden=false;
foundation.X=440; Check(!b.Journey.CanBegin(mountain,"mountainClimb") && foundation.X == 440,"山径超出画布时拒绝攀登且不重置布置"); foundation.X=240;
foundation.Y=200; Check(!b.Journey.CanBegin(mountain,"mountainClimb"),"峰顶头部会裁切时不强行播放"); foundation.Y=310;
b=Actor(mountain); b.Journey.Begin(mountain,"mountainClimb"); Until(b,mountain,"ascend"); foundation.X+=10; b.Tick(.02,mountain,false);
Check(!b.Journey.Active && b.X == Scenes.Home(mountain).X,"基础场景移动使旧山径失效并清理时间轴");
foreach(string scene in new[]{"mountain","polar"}) {
    var s=State(20,true,scene); foreach(string id in new[]{"lie","nap","knees","swing","catch","hop","trot","nurtureHug","roomRest","swingLife"}) Check(!Scenes.ActionAvailable(s,Actions.Get(id)),scene+"动作页禁止不合地面的动作 "+id);
    b=Actor(s); var seen=new HashSet<string>();
    for(int i=0;i<24000;i++) { b.Tick(.1,s,i%2 == 0); seen.Add(b.Current.Id); if (!b.Journey.Active && !Scenes.ActionAvailable(s,b.Current)) throw new Exception(scene+"自动调度越界: "+b.Current.Id); }
    Check(!seen.Overlaps(new[]{"lie","nap","knees","swing","catch","hop","trot","nurtureHug"}),scene+"长时间逻辑调度不出现违和姿势");
    Check(scene == "mountain" ? seen.Contains("mountainClimb") : seen.Contains("snowTouch"),scene+"自动调度能够安静进入场景专属动作");
}
var directory=Path.Combine(Path.GetTempPath(),"ShelterOutdoor-"+Guid.NewGuid());
try {
    Directory.CreateDirectory(directory); var storage=new Storage(directory);
    File.WriteAllText(storage.FilePath,"{\"Version\":1,\"Settings\":{\"GrowthScene\":\"outdoor\"},\"GrowthLayout\":[{\"Id\":\"platform\",\"X\":154,\"Y\":300}],\"FreeLayout\":[{\"Id\":\"house\",\"X\":77,\"Y\":240,\"Hidden\":true}],\"CompanionDates\":[\"2026-01-01\"]}");
    var old=storage.Load(); Check(old.Days == 1 && old.SceneId == "outdoor" && old.GrowthLayout[0].X == 154 && old.FreeLayout[0].Hidden,"旧存档成长/院子访问和两种布置不变");
    Check(old.FreeSceneLayouts.ContainsKey("mountain") && old.FreeSceneLayouts.ContainsKey("polar") && !old.GrowthSceneLayouts.ContainsKey("mountain"),"旧存档补全自由新场景但不提前生成成长场景");
    var s=State(20,false,"mountain"); s.Layout.First(p=>p.Id == "cloud").Hidden=true; s.Layout.First(p=>p.Id == "cloud").X=361;
    s.Settings.FreeMode=true; Scenes.Switch(s,"polar"); s.Layout.First(p=>p.Id == "cloud").X=399; s.FreeStory.FatherLetterRead=true;
    storage.Write(s); var loaded=storage.Load(); Check(Snapshot(loaded) == Snapshot(s),"七场景/两模式布置与故事保存往返保持");
    loaded.Settings.FreeMode=false; Check(loaded.SceneId == "mountain" && loaded.Layout.Any(p=>p.Id == "cloud" && p.X == 361 && p.Hidden),"重启切回成长恢复独立山脉布置");
    storage.Write(s); File.WriteAllText(storage.FilePath,"{broken"); var backup=storage.Load(); Check(backup.Settings.FreeScene == "polar" && backup.GrowthSceneLayouts["mountain"].Any(p=>p.Id == "cloud" && p.X == 361),"新场景原子存档损坏后可由备份恢复");
} finally { if (Directory.Exists(directory)) Directory.Delete(directory,true); }
Console.WriteLine("All "+checks+" outdoor logic checks passed. This is simulated logic time, not an hour of runtime stability testing.");
