using LittleShelter;
using System.Text.Json;

int checks=0;
void Check(bool c,string name) { if (!c) throw new Exception(name); checks++; Console.WriteLine("PASS: "+name); }
SaveData WithDays(int days) { var s=new SaveData(); s.CompanionDates=Enumerable.Range(0,days).Select(i=>new DateTime(2026,1,1).AddDays(i).ToString("yyyy-MM-dd")).ToList(); World.Initialize(s); return s; }
foreach (var d in Scenes.All.Where(d=>d.Day > 0)) {
    var s=WithDays(d.Day-1); Check(!Scenes.Switch(s,d.Id),d.Name+"门槛前拒绝进入");
    s=WithDays(d.Day); Check(Scenes.Switch(s,d.Id) && s.Layout.Count > 0,d.Name+"准确解锁并初始化");
    Check(s.GrowthLayout.Count == 1 && s.Created.Count == 0,"解锁场景不覆盖屋外布局或强制创造");
}
var save=WithDays(20); var outdoor=save.GrowthLayout[0]; outdoor.X=154;
foreach (var d in Scenes.All.Where(d=>d.Id is not "outdoor" and not "cloud")) {
    Scenes.Switch(save,d.Id); var p=save.Layout.First(p=>!Scenes.Foundation(p.Id)); p.X=410; p.Hidden=true;
    save.Settings.FreeMode=true; Scenes.Switch(save,d.Id); Check(save.Layout.Any(p=>p.X != 410 && !p.Hidden),d.Name+"自由布局独立");
    save.Settings.FreeMode=false; Check(save.Layout.Any(p=>p.X == 410 && p.Hidden),d.Name+"成长布局原样恢复");
}
Scenes.Switch(save,"outdoor"); Check(save.Layout[0].X == 154,"切回屋外保留旧位置");
save.Settings.FreeMode=true; Scenes.Switch(save,"swingTree"); save.Settings.FreeMode=false; Check(save.SceneId == "outdoor","两模式当前场景独立保存");
Check(!Scenes.MemoriesUnlocked(WithDays(19)) && Scenes.MemoriesUnlocked(WithDays(20)),"父女记忆在秋千节点同时解锁");
var free=WithDays(0); free.Settings.FreeMode=true; foreach (var d in Scenes.All) Check(Scenes.Switch(free,d.Id),"自由模式开放"+d.Name);
foreach(int days in new[]{0,1,2,6,7,9,10,11,12,15,16,19,20}) {
    var fresh=WithDays(days);
    var ids=Scenes.All.Where(d=>Scenes.Unlocked(fresh,d.Id)).Select(d=>d.Id).ToArray();
    var expected=days>=20 ? new[]{"cloud","indoor","outdoor","meadow","mountain","polar","swingTree"} : days>=16 ? new[]{"cloud","indoor","outdoor","meadow","mountain","polar"} : days>=12 ? new[]{"cloud","indoor","outdoor","meadow","mountain"} : days>=10 ? new[]{"cloud","indoor","outdoor","meadow"} : days>=7 ? new[]{"cloud","indoor","outdoor"} : days>=2 ? new[]{"cloud","indoor"} : new[]{"cloud"};
    Check(ids.SequenceEqual(expected),days+"日依次开放云台/屋内/院子/草坡/山脉/极地/秋千");
    Check(fresh.SceneId == "cloud","解锁新场景不强制切换");
}
var early=WithDays(2); Check(Scenes.Switch(early,"indoor") && World.Available(early).Count() == 5,"第2日卧室家具全部可见");
Scenes.Switch(early,"cloud"); Check(World.Available(early).Single().Id == "platform","云台阶段不混入院子景物");
early.Layout[0].X=87; early.Settings.FreeMode=true; Scenes.Switch(early,"cloud"); Check(early.Layout[0].X == 240,"云台两模式布局独立"); early.Settings.FreeMode=false; Check(early.Layout[0].X == 87,"成长云台位置恢复");
void Advance(Behavior b,double seconds,SaveData s) { for(double t=0;t<seconds;t+=.01) b.Tick(Math.Min(.01,seconds-t),s,false); }
void Until(Behavior b,string phase,SaveData s) { for(int i=0;i<12000 && b.Life.Phase != phase;i++) b.Tick(.01,s,false); Check(b.Life.Phase == phase,"时间轴到达 "+phase); }
string SaveFingerprint(SaveData s) => JsonSerializer.Serialize(s,new JsonSerializerOptions { NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });
Scenes.Switch(save,"swingTree"); var layout=JsonSerializer.Serialize(save.Layout);
var actor=new Behavior(1); actor.Life.BeginSwing(save);
Check(actor.Current.Id == "visit" && actor.IsLocomotion,"秋千开始实际步行");
Until(actor,"hold",save); Until(actor,"mount",save); Until(actor,"swing",save);
Advance(actor,4,save); Check(Math.Abs(actor.Life.Angle)>1 && actor.Life.PresentOpacity == 1,"日常摆荡连续且人物不透明");
double initialAngle=actor.Life.Angle; actor.Life.RequestStop(); Check(actor.Life.Phase == "settle" && actor.Life.Angle == initialAngle,"停下从当前角度连续开始");
Advance(actor,1,save); Check(Math.Abs(actor.Life.Angle)<Math.Abs(initialAngle),"停稳幅度逐渐减少");
Until(actor,"dismount",save); Advance(actor,1.2,save); Check(!actor.Life.Active && actor.IsLocomotion,"起身后实际走离");
Check(JsonSerializer.Serialize(save.Layout) == layout,"秋千整条动作链不改布局");
// Equal wall-clock duration must produce the same swing and memory progress at
// the normal and energy-saving frame rates. Start from an identical hold pose,
// excluding the ordinary walking scheduler from this animation-clock check.
(Behavior Actor,SaveData Save) SampleLife(bool memory,int hz,double seconds) {
    var fixture=WithDays(20); Scenes.Switch(fixture,"swingTree");
    var b=new Behavior(71); Scenes.PlaceActor(fixture,b);
    if (memory) Check(b.Life.BeginMemory(fixture),"时间步检查可进入记忆"); else b.Life.BeginSwing(fixture);
    Until(b,"hold",fixture);
    int whole=(int)Math.Floor(seconds*hz);
    for(int i=0;i<whole;i++) b.Tick(1.0/hz,fixture,false);
    double remaining=seconds-whole/(double)hz; if (remaining>1e-10) b.Tick(remaining,fixture,false);
    return (b,fixture);
}
foreach(bool memory in new[]{false,true}) foreach(double seconds in memory ? new[]{.7,1.7,3.7,7.3,19.5,20.7,22.7,24.2} : new[]{.7,1.7,2.9,4.7,9.3,18.1,22.9,24.8}) {
    var slow=SampleLife(memory,30,seconds); var fast=SampleLife(memory,60,seconds);
    Check(slow.Actor.Life.Phase == fast.Actor.Life.Phase,"30/60Hz同真实时间到达相同阶段 "+memory+"/"+seconds);
    Check(Math.Abs(slow.Actor.Life.Elapsed-fast.Actor.Life.Elapsed)<.051,"30/60Hz阶段时钟误差不累计超过一帧半 "+memory+"/"+seconds+" ("+slow.Actor.Life.Elapsed.ToString("F6")+" / "+fast.Actor.Life.Elapsed.ToString("F6")+")");
    Check(Math.Abs(slow.Actor.Life.Angle-fast.Actor.Life.Angle)<.45,"30/60Hz摆角与减幅随真实时间一致 "+memory+"/"+seconds);
    Check(Math.Abs(slow.Actor.Life.PresentOpacity-fast.Actor.Life.PresentOpacity)<.06 && Math.Abs(slow.Actor.Life.MemoryOpacity-fast.Actor.Life.MemoryOpacity)<.06,"30/60Hz虚化淡入时序一致 "+memory+"/"+seconds);
    Check(slow.Save.GrowthStory.CompletedMemories.SetEquals(fast.Save.GrowthStory.CompletedMemories),"30/60Hz正常完成记忆的故事状态一致 "+memory+"/"+seconds);
}
// The attachment fixes must remain transient: cancellation in every phase must
// not persist half a seated pose or unlock a letter before Rin has returned.
foreach(bool freeMode in new[]{false,true}) foreach(string phase in new[]{"hold","mount","swing","settle","dismount"}) {
    var fixture=WithDays(20); fixture.Settings.FreeMode=freeMode; Scenes.Switch(fixture,"swingTree");
    fixture.Layout.First(p=>p.Id == "flower").X=411;
    string persistent=SaveFingerprint(fixture);
    var b=new Behavior(97); Scenes.PlaceActor(fixture,b); b.Life.BeginSwing(fixture); Until(b,phase,fixture);
    Advance(b,.19,fixture); b.Life.Cancel();
    Check(!b.Life.Active && b.Current.Id == "stand" && b.Life.Angle == 0,"秋千各阶段中断恢复站立与零摆角 "+freeMode+"/"+phase);
    Check(SaveFingerprint(fixture) == persistent,"秋千各阶段中断保留两模式布局/故事/成长 "+freeMode+"/"+phase);
}
foreach(bool freeMode in new[]{false,true}) {
    foreach(string phase in new[]{"hold","fadeOut","memory","empty","fadeIn"}) {
        var fixture=WithDays(20); fixture.Settings.FreeMode=freeMode; Scenes.Switch(fixture,"swingTree");
        var b=new Behavior(98); Scenes.PlaceActor(fixture,b); double x=b.X,y=b.Y;
        string persistent=SaveFingerprint(fixture); int completed=0; b.Life.MemoryCompleted+=()=>completed++;
        b.Life.BeginMemory(fixture); Until(b,phase,fixture); Advance(b,.2,fixture); b.Life.RequestStop();
        Check(!b.Life.Active && b.X == x && b.Y == y && b.Life.PresentOpacity == 1,"父女秋千提前结束恢复原角色落点 "+freeMode+"/"+phase);
        Check(completed == 0 && SaveFingerprint(fixture) == persistent,"提前结束父女记忆不提交来信或临时陈设 "+freeMode+"/"+phase);
    }
    var complete=WithDays(20); complete.Settings.FreeMode=freeMode; Scenes.Switch(complete,"swingTree");
    string layouts=JsonSerializer.Serialize(new[]{complete.GrowthLayout,complete.FreeLayout});
    string growthScenes=JsonSerializer.Serialize(complete.GrowthSceneLayouts), freeScenes=JsonSerializer.Serialize(complete.FreeSceneLayouts);
    var done=new Behavior(99); Scenes.PlaceActor(complete,done); int events=0; done.Life.MemoryCompleted+=()=>events++;
    done.Life.BeginMemory(complete); Until(done,"watch",complete);
    Check(events == 1 && Story.Current(complete).CompletedMemories.SetEquals(new[]{"swingTree"}) && Story.Current(complete).FatherLetterUnlocked,"父女记忆在现在Rin恢复后只提交一次完成 "+freeMode);
    Advance(done,3.1,complete); Check(events == 1 && !done.Life.Active,"退出观察不重复提交记忆 "+freeMode);
    Check(!(freeMode ? complete.GrowthStory : complete.FreeStory).FatherLetterUnlocked,"记忆完成不污染另一模式故事 "+freeMode);
    Check(JsonSerializer.Serialize(new[]{complete.GrowthLayout,complete.FreeLayout}) == layouts && JsonSerializer.Serialize(complete.GrowthSceneLayouts) == growthScenes && JsonSerializer.Serialize(complete.FreeSceneLayouts) == freeScenes,"父女秋千完整播放保留全部场景布局 "+freeMode);
}
foreach (var scene in new[]{"swingTree","indoor"}) {
    Scenes.Switch(save,scene); var before=JsonSerializer.Serialize(save.Layout); var b=new Behavior(4) { X=220,Y=252 };
    Check(b.Life.BeginMemory(save),scene+"可主动打开记忆"); Until(b,"fadeOut",save);
    Advance(b,.7,save); Check(b.Life.PresentOpacity>0 && b.Life.PresentOpacity<1 && b.Life.BlurRadius>0,"淡出与虚化连续");
    Until(b,"memory",save); Check(b.Life.PresentOpacity == 0,"记忆中现在Rin完全隐去");
    Advance(b,2,save); Check(b.Life.MemoryOpacity == 1,"父女稳定出现");
    Until(b,"empty",save); Until(b,"fadeIn",save); Until(b,"watch",save); Advance(b,3.1,save);
    Check(!b.Life.Active && b.X == 220 && b.Y == 252,"记忆结束恢复位置和日常");
    Check(JsonSerializer.Serialize(save.Layout) == before,"记忆临时陈设不写入布局");
    foreach (var phase in new[]{"approach","hold","fadeOut","memory","empty","fadeIn","watch"}) {
        b=new Behavior(2) { X=220,Y=252 }; b.Life.BeginMemory(save); if (phase != "approach") Until(b,phase,save);
        b.Life.RequestStop(); Check(!b.Life.Active && b.X == 220 && b.Life.PresentOpacity == 1,"提前结束 "+scene+"/"+phase);
    }
    b=new Behavior(2); b.Life.BeginMemory(save); Until(b,"memory",save); Scenes.Switch(save,"meadow"); b.Tick(.01,save,false);
    Check(!b.Life.Active && b.Current.Id == "stand","切换场景优先于记忆"); Scenes.Switch(save,scene);
}
Scenes.Switch(save,"swingTree"); var active=new Behavior(1); active.Life.BeginSwing(save); Until(active,"swing",save); Advance(active,3,save);
Check(active.Life.BeginMemory(save) && active.Life.Phase == "settle","荡秋千时先停稳再回忆"); Until(active,"fadeOut",save); Check(active.Life.Angle == 0,"回忆进入前秋千已停稳");
foreach(bool freeMode in new[]{false,true}) {
    var fixture=WithDays(20); fixture.Settings.FreeMode=freeMode; Scenes.Switch(fixture,"swingTree");
    string before=JsonSerializer.Serialize(fixture.Layout); var b=new Behavior(101); Scenes.PlaceActor(fixture,b);
    double homeX=b.X,homeY=b.Y; int completed=0; b.Life.MemoryCompleted+=()=>completed++;
    b.Life.BeginSwing(fixture); Until(b,"dismount",fixture); Advance(b,.2,fixture);
    Check(b.Life.BeginMemory(fixture),"起身离座期间可以主动请求记忆 "+freeMode);
    Until(b,"fadeOut",fixture);
    Check(b.Life.IsMemory && !b.Life.RecallSeated && b.Life.Angle == 0,"离座请求等待站稳再淡出，不把未坐角色移回座板 "+freeMode);
    Check(completed == 0 && !Story.Current(fixture).FatherLetterUnlocked,"离座请求本身不提前提交记忆完成 "+freeMode);
    Until(b,"watch",fixture); Advance(b,3.1,fixture);
    Check(!b.Life.Active && !b.Life.MemoryRequested && completed == 1,"离座请求完整进入并结束记忆，不遗留请求或重复完成 "+freeMode);
    Check(b.X == homeX && b.Y == homeY && JsonSerializer.Serialize(fixture.Layout) == before,"离座后记忆恢复原来落点和布置 "+freeMode);
}
var directory=Path.Combine(Path.GetTempPath(),"ShelterScenes-"+Guid.NewGuid());
try {
    var storage=new Storage(directory);
    // A real legacy JSON lacking all additive fields, including scene settings.
    string legacy="{\"Version\":1,\"Settings\":{},\"GrowthLayout\":[{\"Id\":\"platform\",\"X\":154,\"Y\":300}],\"FreeLayout\":[{\"Id\":\"house\",\"X\":77,\"Y\":240,\"Hidden\":true}],\"CompanionDates\":[\"2026-01-01\"]}";
    Directory.CreateDirectory(directory); File.WriteAllText(storage.FilePath,legacy); var migrated=storage.Load();
    Check(migrated.Days == 1 && migrated.GrowthLayout[0].X == 154 && migrated.FreeLayout[0].Hidden,"旧JSON完整保留成长与两布局");
    Check(migrated.SceneId == "outdoor" && Scenes.Unlocked(migrated,"outdoor"),"旧版提前院子访问权保留");
    var oldRoom=WithDays(2); oldRoom.GrowthSceneLayouts["indoor"]=new(){new(){Id="roomBase",X=240,Y=306},new(){Id="desk",X=411,Y=215,Hidden=true},new(){Id="window",X=275,Y=168},new(){Id="chair",X=313,Y=266},new(){Id="lamp",X=109,Y=248,Hidden=true}}; oldRoom.IndoorStyleVersion=0;
    Scenes.Switch(oldRoom,"indoor"); storage.Write(oldRoom); var upgrade=storage.Load(); var table=upgrade.Layout.First(p=>p.Id == "desk");
    Check(table.X == 411 && table.Y == 215 && table.Hidden,"重绘卧室保留自定义位置与隐藏状态");
    Check(upgrade.Layout.Any(p=>p.Id == "chair" && p.X == 166 && p.Y == 279),"旧默认木椅位置迁移为床铺布局");
    Check(upgrade.Layout.Any(p=>p.Id == "lamp" && p.Hidden) && !World.Available(upgrade).Any(d=>d.Id == "lamp"),"旧灯布置保留但不混入原片卧室");
    var roomSnapshot=JsonSerializer.Serialize(upgrade.Layout); World.Initialize(upgrade); Check(JsonSerializer.Serialize(upgrade.Layout) == roomSnapshot,"卧室迁移仅执行一次");
    storage.Write(save); var reloaded=storage.Load(); Check(reloaded.SceneId == save.SceneId && JsonSerializer.Serialize(reloaded.Layout) == JsonSerializer.Serialize(save.Layout),"新多场景存档往返");
    storage.Write(save); File.WriteAllText(storage.FilePath,"{broken"); Check(storage.Load().SceneId == save.SceneId,"多场景备份恢复");
} finally { Directory.Delete(directory,true); }
foreach(var id in new[]{"outdoor","meadow","indoor","swingTree"}) {
    Scenes.Switch(free,id); var b=new Behavior(9); Scenes.PlaceActor(free,b); var home=Scenes.Home(free);
    double floor=id switch { "meadow"=>free.Layout.First(p=>p.Id == "meadowBase").Y-28, "indoor"=>free.Layout.First(p=>p.Id == "roomBase").Y-12, "swingTree"=>free.Layout.First(p=>p.Id == "swingSeat").Y+44, _=>252 };
    Check(b.Y == floor,id+"启动落点跟随该场景地面");
    b.Start("walk",home.X+15,home.Y); Advance(b,2,free); Check(b.Y == floor,id+"散步不返回屋外高度");
    if (id is "indoor" or "swingTree") {
        b.Start("stand"); double x=b.X; b.Life.BeginMemory(free); Until(b,"watch",free); Advance(b,3.02,free);
        Check(!b.Life.Active && b.X == x && b.Y == floor,id+"记忆恢复正确地面落点");
    }
}
foreach(bool night in new[]{false,true}) foreach(bool hiddenBed in new[]{false,true}) foreach(bool freeMode in new[]{false,true}) {
    var room=WithDays(20); room.Settings.FreeMode=freeMode; Scenes.Switch(room,"indoor");
    room.Layout.First(p=>p.Id == "chair").Hidden=hiddenBed;
    var selected=new HashSet<string>();
    foreach(int seed in Enumerable.Range(0,12)) {
        var b=new Behavior(seed); Scenes.PlaceActor(room,b);
        for(int i=0;i<8000;i++) {
            b.Tick(.15,room,night); selected.Add(b.Current.Id);
            if (!Scenes.ActionAvailable(room,b.Current)) throw new Exception("屋内自动行为越界: "+b.Current.Id);
        }
    }
    Check(!selected.Overlaps(new[]{"lie","nap","knees","swing","catch","sky","trot","hop"}),"屋内昼夜/两模式不使用地板休息和户外动作 "+night+"/"+hiddenBed+"/"+freeMode);
    Check(hiddenBed ? !selected.Contains("roomRest") : selected.Contains("roomRest"),"床隐藏时禁止休息，可见时能够床沿休息");
}
Scenes.Switch(free,"indoor"); var bedPlacement=free.Layout.First(p=>p.Id == "chair"); bedPlacement.Hidden=false;
var interruptedRoom=new Behavior(6); Scenes.PlaceActor(free,interruptedRoom); interruptedRoom.PreviewVisit(free,"roomRest"); bedPlacement.Hidden=true; interruptedRoom.Tick(.1,free,false);
Check(interruptedRoom.Current.Id == "stand" && interruptedRoom.Y == Scenes.Home(free).Y,"走向床时床被收起，取消排队坐姿并回地面");
foreach(var id in new[]{"lie","nap","knees","swing","catch","sky","hop","trot","createRaise","createSwipe"}) {
    var b=new Behavior(); b.Start(id); b.Tick(.01,free,true);
    Check(b.Current.Id == "stand","屋内拒绝遗留/强制动作 "+id);
}
bedPlacement.Hidden=false; var rising=new Behavior(); rising.X=bedPlacement.X; rising.Y=bedPlacement.Y; rising.Start("roomRest"); rising.Click(free);
Check(rising.Y == Scenes.Home(free).Y && rising.Current.Kind == "interact","床沿点击回应先回到地板高度");
rising.Start("roomRest"); rising.Y=bedPlacement.Y; rising.PreviewVisit(free,"deskTablet");
Check(rising.Y == Scenes.Home(free).Y && rising.IsLocomotion,"从床去圆几沿家具前地板走动");
rising.Y=bedPlacement.Y; rising.Start("roomRest"); rising.Life.BeginMemory(free);
Check(rising.Y == Scenes.Home(free).Y,"床沿进入记忆先起身到地板");
rising.Life.RequestStop(); Check(rising.Y == Scenes.Home(free).Y && rising.Current.Id == "stand","床沿进入记忆后取消不悬空");
Console.WriteLine("All "+checks+" scene checks passed.");
