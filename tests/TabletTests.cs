using LittleShelter;
using System.Text.Json;

int checks=0;
void Check(bool condition,string name) { if (!condition) throw new Exception("FAIL: "+name); checks++; Console.WriteLine("PASS: "+name); }
SaveData Save(int days=20,bool free=false,string scene="outdoor") {
    var s=new SaveData { CompanionDates=Enumerable.Range(0,days).Select(i=>new DateTime(2026,1,1).AddDays(i).ToString("yyyy-MM-dd")).ToList() };
    s.Settings.FreeMode=free; World.Initialize(s);
    if (!Scenes.Switch(s,scene)) throw new Exception("invalid fixture scene");
    return s;
}
Behavior Actor(SaveData s,int seed=7) { var b=new Behavior(seed) { X=365 }; b.Y=Scenes.Home(s).Y; return b; }
void Advance(Behavior b,SaveData s,double seconds) { while(seconds>1e-7) { double dt=Math.Min(.01,seconds); b.Tick(dt,s,false); seconds-=dt; } }
void Until(Behavior b,SaveData s,string phase) {
    for(int i=0;i<16000 && b.Tablet.Phase!=phase;i++) b.Tick(.01,s,false);
    if (b.Tablet.Phase!=phase) throw new Exception("did not reach tablet "+phase+", current="+b.Tablet.Phase);
}
void LifeUntil(Behavior b,SaveData s,string phase) {
    for(int i=0;i<16000 && b.Life.Phase!=phase;i++) b.Tick(.01,s,false);
    if (b.Life.Phase!=phase) throw new Exception("did not reach memory "+phase);
}
string Layouts(SaveData s) => JsonSerializer.Serialize(new { s.GrowthLayout,s.FreeLayout,s.GrowthSceneLayouts,s.FreeSceneLayouts,s.Created });
void BeginAt(Behavior b,SaveData s,string phase) {
    if (phase=="approach") b.X=240;
    if (!b.Tablet.Open(s)) throw new Exception("cannot open fixture tablet");
    if (phase is "closing" or "tap" or "observe" or "stowing") {
        Until(b,s,"ready"); if (!b.Tablet.Select(s,"flower") || !b.Tablet.Confirm(s)) throw new Exception("cannot confirm fixture");
    }
    Until(b,s,phase);
}

foreach(int days in new[]{0,1,2,3,7,10,19,20}) foreach(bool free in new[]{false,true}) {
    var s=Save(days,free,"cloud");
    foreach(var scene in Scenes.All.Where(d=>Scenes.Unlocked(s,d.Id))) {
        Scenes.Switch(s,scene.Id); var b=Actor(s); string before=Layouts(s);
        Check(b.Tablet.Open(s),days+"日/"+free+"/"+scene.Id+"手动可打开平板");
        Check(b.Tablet.Candidates.ToHashSet().SetEquals(World.Available(s).Where(d=>!Scenes.Foundation(d.Id)).Select(d=>d.Id)),"候选仅含当前场景已解锁非基础景物");
        Check(Layouts(s)==before && s.ChoiceCounts.Count==0,"打开平板不改变布置与主动邀请次数");
        Check(!b.Tablet.Open(s),"已打开时拒绝第二会话"); b.Tablet.Cancel(s);
    }
}
{
    var s=Save(0,false,"cloud"); var b=Actor(s);
    Check(!b.Tablet.Open(s,true),"初始云台无候选时不主动邀请");
    Check(!b.Tablet.Open(s,preselect:"house") && s.ChoiceCounts.Count==0,"未解锁或其他场景预选不会打开或计数");
    Check(b.Tablet.Open(s,page:"messages"),"空消息也可手动打开"); Until(b,s,"ready");
    Check(!b.Tablet.OpenLetter(s) && FatherLetter.Empty=="No messages in 2593 days.","空消息固定2593并无可读占位信件");
    var page=b.Tablet.Page; Check(!b.Tablet.Confirm(s) && page=="messages","消息列表不会调度创造");
}
{
    var s=Save(); var b=Actor(s); var date=new DateTime(2026,10,13);
    for(int i=0;i<3;i++) { Check(b.Tablet.Open(s,true,date:date),"第"+(i+1)+"次当天主动邀请允许"); Check(b.Tablet.Candidates.Length==2,"主动邀请仅两个候选"); b.Tablet.Cancel(s); }
    Check(!b.Tablet.Open(s,true,date:date) && s.ChoiceCounts["2026-10-13"]==3,"主动邀请每天最多3次");
    Check(b.Tablet.Open(s,false,date:date) && s.ChoiceCounts["2026-10-13"]==3,"手动打开不消耗主动次数"); b.Tablet.Cancel(s);
    Check(b.Tablet.Open(s,true,date:date.AddDays(1)),"跨日可使用新的主动邀请额度");
}
{
    var s=Save(); var b=Actor(s); b.X=240; Check(b.Tablet.Open(s,true),"主动邀请从真实步行开始");
    Check(b.IsLocomotion && b.Tablet.Phase=="approach","先走到平板旁不平移人物");
    Until(b,s,"ready"); Check(b.Tablet.UnansweredSeconds==0 && b.Tablet.Expansion==1,"超时从界面可操作开始不计步行与展开");
    Advance(b,s,19.99); Check(b.Tablet.Interactive && b.Tablet.Automatic,"19.99秒仍可操作");
    Advance(b,s,.02); Check(!b.Tablet.Interactive && b.Tablet.Phase=="closing","满20秒收回且立即清除命中条件");
    Until(b,s,"none"); Check(!b.Tablet.Active && b.Current.Id=="stand","超时完整收起回到日常");
}
foreach(string response in new[]{"select","tab","candidatePage","letterPage","openLetter"}) {
    var s=Save(free:true); var b=Actor(s); Check(b.Tablet.Open(s,true),"主动邀请可开始回应检查 "+response); Until(b,s,"ready");
    Advance(b,s,19.8);
    if (response=="select") b.Tablet.Select(s,b.Tablet.Candidates[0]);
    if (response=="tab") b.Tablet.ChangePage(s,"messages");
    if (response=="candidatePage") b.Tablet.TurnPage(s,1);
    if (response is "letterPage" or "openLetter") { b.Tablet.ChangePage(s,"messages"); b.Tablet.OpenLetter(s); if (response=="letterPage") b.Tablet.TurnPage(s,1); }
    Advance(b,s,25);
    Check(b.Tablet.Interactive && !b.Tablet.Automatic && b.Tablet.UnansweredSeconds==0,"实际回应转手动并取消超时 "+response);
}
{
    var s=Save(); var b=Actor(s); int events=0; b.ObjectCreated+=id=>{events++;World.Create(s,id);};
    string before=Layouts(s); b.Tablet.Open(s); Until(b,s,"ready");
    Check(b.Tablet.Select(s,"flower") && Layouts(s)==before,"预选只预览不写布局");
    Check(b.Tablet.Confirm(s) && b.Tablet.ConfirmedTarget=="flower" && events==0,"确认只锁定目标不提前生成");
    Check(!b.Tablet.Confirm(s) && !b.Tablet.Select(s,"tree") && !b.Tablet.ChangePage(s,"messages"),"确认后双击与切页不能重复调度");
    Until(b,s,"tap"); Advance(b,s,.805);
    Check(events==0 && !s.Layout.Any(p=>p.Id=="flower"),"操作平板动作58%前不生成");
    Advance(b,s,.02); Check(events==1 && b.Tablet.HasEmitted && s.Layout.Any(p=>p.Id=="flower"),"点触动作达到58%仅生成一次");
    Until(b,s,"none"); Check(events==1 && b.Current.Id=="stand" && b.Tablet.ConfirmedTarget==null,"观察收起后不重复事件并释放目标");
    string shown=Layouts(s); b.Tablet.Open(s,preselect:"flower"); Until(b,s,"ready"); b.Tablet.Confirm(s); Until(b,s,"observe");
    Check(events==1 && Layouts(s)==shown,"已显示景物再次确认只观察不重复生成和保存"); b.Tablet.Cancel(s);
    var flower=s.Layout.First(p=>p.Id=="flower"); flower.X=411; flower.Y=299; flower.Hidden=true;
    b.Tablet.Open(s,preselect:"flower"); Until(b,s,"ready"); b.Tablet.Confirm(s); Until(b,s,"observe");
    Check(events==2 && !flower.Hidden && flower.X==411 && flower.Y==299,"隐藏景物在生成节点恢复原位置");
}
foreach(string phase in new[]{"approach","present","opening","ready","closing","tap","observe","stowing"}) foreach(string interrupt in new[]{"cancel","scene","mode","drag"}) {
    var s=Save(); var b=Actor(s); int events=0; b.ObjectCreated+=id=>{events++;World.Create(s,id);}; BeginAt(b,s,phase);
    int before=events, generation=b.Tablet.Generation; string layouts=Layouts(s);
    if (interrupt=="cancel") b.Tablet.Cancel(s);
    if (interrupt=="scene") { Scenes.Switch(s,"indoor"); b.Tick(.01,s,false); }
    if (interrupt=="mode") { s.Settings.FreeMode=true; b.Tick(.01,s,false); }
    if (interrupt=="drag") { b.Dragging=true; b.Tick(.01,s,false); }
    Check(!b.Tablet.Active && !b.Tablet.Interactive && b.Tablet.Candidates.Length==0 && b.Tablet.ConfirmedTarget==null && b.Tablet.Generation>generation,"阶段中断清除界面与过期目标 "+phase+"/"+interrupt);
    Advance(b,s,2);
    Check(events==before && Layouts(s)==layouts,"中断不补发生成且已出现物件保留 "+phase+"/"+interrupt);
}
foreach(string phase in new[]{"approach","present","opening","ready","closing","tap","observe","stowing"}) {
    var s=Save(); var b=Actor(s); int events=0; b.ObjectCreated+=id=>{events++;World.Create(s,id);}; BeginAt(b,s,phase);
    if (phase=="opening") Advance(b,s,.2);
    double expansion=b.Tablet.Expansion; int before=events; b.Tablet.Close(s);
    Check(b.Tablet.Expansion<=expansion+1e-9 && !b.Tablet.Interactive,"关闭阶段不弹出整屏或残留命中 "+phase);
    if (phase=="present") Check(!b.Tablet.ScreenVisible && b.Tablet.Phase=="present" && b.Tablet.Recovering,"拿板中关闭先完成当前拿板再收起");
    if (phase is "tap" or "observe") Check(!b.Tablet.ScreenVisible && b.Tablet.Phase=="stowing","手持阶段关闭直接收起 "+phase);
    Advance(b,s,.1); double elapsed=b.Tablet.Elapsed; string after=b.Tablet.Phase; b.Tablet.Close(s);
    Check(b.Tablet.Phase==after && b.Tablet.Elapsed==elapsed,"重复关闭不重置计时 "+phase);
    Until(b,s,"none"); Check(events==before,"关闭各阶段不会晚到生成 "+phase);
}
{
    var s=Save(); var b=Actor(s); b.Tablet.Open(s); Until(b,s,"ready"); b.Tablet.Select(s,"flower");
    s.CompanionDates.Clear(); s.Settings.LegacyOutdoorAccess=true;
    Check(!b.Tablet.Confirm(s),"确认时重新核对解锁状态"); b.Tablet.Cancel(s);
    s=Save(); b=Actor(s); int events=0; b.ObjectCreated+=_=>events++; b.Tablet.Open(s,preselect:"flower"); Until(b,s,"ready"); b.Tablet.Confirm(s); Until(b,s,"tap");
    s.CompanionDates.Clear(); s.Settings.LegacyOutdoorAccess=true; Advance(b,s,.9);
    Check(events==0 && !b.Tablet.Active,"生成时再次核对解锁并丢弃失效确认");
}
{
    var s=Save(); var b=Actor(s); b.Life.BeginMemory(s); // outdoor is not a memory scene; explicit exclusions below
    b.Dragging=true; Check(!b.Tablet.Open(s),"拖动拒绝展开平板"); b.Dragging=false; b.Start("createTap"); Check(!b.Tablet.Open(s),"自主创造保留且不强行展开");
    b.Start("stand"); Scenes.Switch(s,"swingTree"); b.Life.BeginSwing(s); Check(!b.Tablet.Open(s),"秋千日常期间拒绝展开"); b.Life.Cancel();
    Check(b.Life.BeginMemory(s) && !b.Tablet.Open(s),"记忆期间拒绝展开"); b.Life.Cancel();
    b.Tablet.Open(s); Until(b,s,"ready"); Check(b.Life.BeginMemory(s) && !b.Tablet.Active,"开始记忆关闭平板且唯一调度互斥");
}
foreach(string scene in new[]{"indoor","swingTree"}) {
    foreach(string phase in new[]{"approach","hold","fadeOut","memory","empty","fadeIn"}) {
        var s=Save(scene:scene); var b=Actor(s); int completed=0; b.Life.MemoryCompleted+=()=>completed++; b.Life.BeginMemory(s); LifeUntil(b,s,phase); b.Life.Cancel();
        Check(!s.GrowthStory.FatherLetterUnlocked && s.GrowthStory.CompletedMemories.Count==0 && completed==0,"提前结束不开放来信 "+scene+"/"+phase);
    }
    {
        var s=Save(scene:scene); var b=Actor(s); int completed=0; b.Life.MemoryCompleted+=()=>completed++; string before=Layouts(s); b.Life.BeginMemory(s); LifeUntil(b,s,"fadeIn"); Advance(b,s,1.49);
        Check(!s.GrowthStory.FatherLetterUnlocked,"现在Rin尚未完全淡入时不解锁 "+scene);
        LifeUntil(b,s,"watch"); Check(s.GrowthStory.FatherLetterUnlocked && s.GrowthStory.CompletedMemories.SetEquals(new[]{scene}) && completed==1 && !b.Tablet.Active,"淡入恢复观察节点开放同一来信而不弹出平板 "+scene);
        b.Life.Cancel(); Check(Story.LetterAvailable(s),"观察阶段退出不撤回来信 "+scene);
        b.Life.BeginMemory(s); LifeUntil(b,s,"watch"); Check(completed==1 && s.GrowthStory.CompletedMemories.Count==1,"重复完整记忆不重复生成记录 "+scene);
        Check(Layouts(s)==before,"记忆完成与重看不修改布局 "+scene);
    }
    foreach(bool modeChange in new[]{false,true}) {
        var s=Save(scene:scene); var b=Actor(s); b.Life.BeginMemory(s); LifeUntil(b,s,"fadeIn");
        if (modeChange) s.Settings.FreeMode=true; else Scenes.Switch(s,"meadow"); b.Tick(.02,s,false);
        Check(!s.GrowthStory.FatherLetterUnlocked && !s.FreeStory.FatherLetterUnlocked && !b.Life.Active,"过期记忆不能跨场景/模式解锁 "+scene+"/"+modeChange);
    }
}
{
    var s=Save(scene:"indoor"); var b=Actor(s); b.Life.BeginMemory(s); LifeUntil(b,s,"memory"); double before=b.Life.Elapsed;
    // A zero animation delta models a frozen clock without implying that the
    // native lock-screen/fullscreen hooks have been exercised by this test.
    for(int i=0;i<500;i++) b.Tick(0,s,false);
    Check(b.Life.Elapsed==before && !Story.LetterAvailable(s),"零动画时间供给不推进记忆或提前开放来信");
    LifeUntil(b,s,"watch"); b.Life.Cancel(); b.Tablet.Open(s,page:"messages"); Until(b,s,"ready");
    Check(!s.GrowthStory.FatherLetterRead,"仅打开消息列表仍未读"); int generation=b.Tablet.Generation, changes=0; b.Tablet.StoryChanged+=()=>changes++;
    Check(b.Tablet.OpenLetter(s) && !s.GrowthStory.FatherLetterRead,"主动点开正文但尚未绘制仍未读");
    b.Tablet.BodyPresented(s,generation-1); Check(!s.GrowthStory.FatherLetterRead,"过期绘图代次不能标记已读");
    b.Tablet.BodyPresented(s,generation); Check(s.GrowthStory.FatherLetterRead && changes==1,"当前可读正文呈现后只保存一次已读"); b.Tablet.BodyPresented(s,generation);
    Check(changes==1,"重复绘制正文不重复保存");
    b.Tablet.TurnPage(s,1); Check(b.Tablet.LetterPage==1,"正文仅手动下一页"); Advance(b,s,5); Check(b.Tablet.LetterPage==1,"等候不会自动滚动或翻页");
    b.Tablet.TurnPage(s,99); Check(b.Tablet.LetterPage==FatherLetter.Pages.Length-1,"最后一页不越界"); b.Tablet.TurnPage(s,-99); Check(b.Tablet.LetterPage==0,"第一页不越界");
    b.Tablet.BackToList(s); Check(!b.Tablet.Reading && s.GrowthStory.FatherLetterRead,"返回列表保留同一封已读信");
    b.Tablet.OpenLetter(s); Check(b.Tablet.LetterPage==0 && b.Tablet.Reading,"重读回到第一页");
}
{
    var s=Save(0,true,"cloud"); var b=Actor(s); string before=Layouts(s); var days=s.CompanionDates.ToArray();
    Check(Story.LetterAvailable(s),"自由模式立即开放来信"); b.Tablet.Open(s,page:"messages"); Until(b,s,"ready"); b.Tablet.OpenLetter(s); b.Tablet.BodyPresented(s,b.Tablet.Generation);
    Check(s.FreeStory.FatherLetterRead && !s.GrowthStory.FatherLetterRead && !s.GrowthStory.FatherLetterUnlocked,"自由模式阅读不提前开放成长来信");
    Check(Layouts(s)==before && s.ChoiceCounts.Count==0 && s.CompanionDates.SequenceEqual(days),"消息阅读不改布置/成长/创造次数");
    int generation=b.Tablet.Generation; s.Settings.FreeMode=false; b.Tablet.BodyPresented(s,generation); b.Tick(.01,s,false);
    Check(!b.Tablet.Active && !Story.LetterAvailable(s) && !s.GrowthStory.FatherLetterRead,"切回成长关屏并保留成长空消息");
    s.Settings.FreeMode=true; b.Tablet.Open(s,page:"messages"); Until(b,s,"ready"); Check(Story.LetterAvailable(s) && s.FreeStory.FatherLetterRead && !b.Tablet.Reading,"切回自由保留已读但不自动打开正文");
}
var directory=Path.Combine(Path.GetTempPath(),"ShelterTablet-"+Guid.NewGuid());
try {
    Directory.CreateDirectory(directory); var storage=new Storage(directory); var s=Save(); s.GrowthLayout[0].X=154; s.FreeLayout.First(p=>p.Id=="house").X=77; s.FreeLayout.First(p=>p.Id=="house").Hidden=true;
    s.GrowthStory.CompletedMemories.Add("indoor"); s.GrowthStory.FatherLetterUnlocked=true; s.GrowthStory.FatherLetterRead=true; s.FreeStory.FatherLetterRead=true;
    string before=Layouts(s); storage.Write(s); var reloaded=storage.Load();
    Check(reloaded.GrowthStory.FatherLetterRead && reloaded.FreeStory.FatherLetterRead && reloaded.GrowthStory.CompletedMemories.SetEquals(new[]{"indoor"}),"来信状态与两模式记忆记录重启恢复");
    Check(Layouts(reloaded)==before && reloaded.Days==20 && !Actor(reloaded).Tablet.Active,"重启不恢复半展开界面且保留旧布置与进度");
    s.GrowthStory.FatherLetterRead=false; storage.Write(s); File.WriteAllText(storage.FilePath,"{broken"); reloaded=storage.Load();
    Check(reloaded.GrowthStory.FatherLetterRead && reloaded.FreeStory.FatherLetterRead,"损坏主存档恢复备份中的故事状态");
    File.WriteAllText(storage.FilePath,"{\"Version\":1,\"Settings\":{},\"GrowthLayout\":[{\"Id\":\"platform\",\"X\":154,\"Y\":300}],\"FreeLayout\":[{\"Id\":\"house\",\"X\":77,\"Y\":240,\"Hidden\":true}],\"CompanionDates\":[\"2026-01-01\"]}");
    var legacy=storage.Load(); Check(legacy.Days==1 && legacy.GrowthLayout[0].X==154 && legacy.FreeLayout[0].X==77 && legacy.FreeLayout[0].Hidden && !legacy.GrowthStory.FatherLetterUnlocked && !legacy.GrowthStory.FatherLetterRead,"缺少故事字段的旧版存档安全迁移而不伪造完成");
    s.GrowthStory=null!; s.FreeStory=null!; storage.Write(s); var safe=storage.Load(); Check(safe.GrowthStory!=null && safe.FreeStory!=null,"null故事字段修复为安全默认");
    safe.GrowthStory.CompletedMemories=null!; safe.GrowthStory.FatherLetterRead=true; Story.Sanitize(safe); Check(safe.GrowthStory.CompletedMemories.Count==0 && !safe.GrowthStory.FatherLetterRead,"缺失记忆集合及无来信已读状态清理");
    safe.GrowthStory.CompletedMemories=new(){"indoor","unknown","swingTree"}; Story.Sanitize(safe); Check(safe.GrowthStory.CompletedMemories.SetEquals(new[]{"indoor","swingTree"}) && safe.GrowthStory.FatherLetterUnlocked,"故事恢复仅认可两个记忆ID");
} finally { Directory.Delete(directory,true); }
Console.WriteLine("All "+checks+" tablet/story checks passed.");
