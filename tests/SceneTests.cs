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
foreach (var d in Scenes.All.Where(d=>d.Id != "outdoor")) {
    Scenes.Switch(save,d.Id); var p=save.Layout.First(p=>!Scenes.Foundation(p.Id)); p.X=410; p.Hidden=true;
    save.Settings.FreeMode=true; Scenes.Switch(save,d.Id); Check(save.Layout.Any(p=>p.X != 410 && !p.Hidden),d.Name+"自由布局独立");
    save.Settings.FreeMode=false; Check(save.Layout.Any(p=>p.X == 410 && p.Hidden),d.Name+"成长布局原样恢复");
}
Scenes.Switch(save,"outdoor"); Check(save.Layout[0].X == 154,"切回屋外保留旧位置");
save.Settings.FreeMode=true; Scenes.Switch(save,"swingTree"); save.Settings.FreeMode=false; Check(save.SceneId == "outdoor","两模式当前场景独立保存");
Check(!Scenes.MemoriesUnlocked(WithDays(19)) && Scenes.MemoriesUnlocked(WithDays(20)),"父女记忆在秋千节点同时解锁");
var free=WithDays(0); free.Settings.FreeMode=true; foreach (var d in Scenes.All) Check(Scenes.Switch(free,d.Id),"自由模式开放"+d.Name);
void Advance(Behavior b,double seconds,SaveData s) { for(double t=0;t<seconds;t+=.01) b.Tick(Math.Min(.01,seconds-t),s,false); }
void Until(Behavior b,string phase,SaveData s) { for(int i=0;i<12000 && b.Life.Phase != phase;i++) b.Tick(.01,s,false); Check(b.Life.Phase == phase,"时间轴到达 "+phase); }
Scenes.Switch(save,"swingTree"); var layout=JsonSerializer.Serialize(save.Layout);
var actor=new Behavior(1); actor.Life.BeginSwing(save);
Check(actor.Current.Id == "visit" && actor.IsLocomotion,"秋千开始实际步行");
Until(actor,"hold",save); Until(actor,"mount",save); Until(actor,"swing",save);
Advance(actor,4,save); Check(Math.Abs(actor.Life.Angle)>1 && actor.Life.PresentOpacity == 1,"日常摆荡连续且人物不透明");
double initialAngle=actor.Life.Angle; actor.Life.RequestStop(); Check(actor.Life.Phase == "settle" && actor.Life.Angle == initialAngle,"停下从当前角度连续开始");
Advance(actor,1,save); Check(Math.Abs(actor.Life.Angle)<Math.Abs(initialAngle),"停稳幅度逐渐减少");
Until(actor,"dismount",save); Advance(actor,1.2,save); Check(!actor.Life.Active && actor.IsLocomotion,"起身后实际走离");
Check(JsonSerializer.Serialize(save.Layout) == layout,"秋千整条动作链不改布局");
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
var directory=Path.Combine(Path.GetTempPath(),"ShelterScenes-"+Guid.NewGuid());
try {
    var storage=new Storage(directory);
    // A real legacy JSON lacking all additive fields, including scene settings.
    string legacy="{\"Version\":1,\"Settings\":{},\"GrowthLayout\":[{\"Id\":\"platform\",\"X\":154,\"Y\":300}],\"FreeLayout\":[{\"Id\":\"house\",\"X\":77,\"Y\":240,\"Hidden\":true}],\"CompanionDates\":[\"2026-01-01\"]}";
    Directory.CreateDirectory(directory); File.WriteAllText(storage.FilePath,legacy); var migrated=storage.Load();
    Check(migrated.Days == 1 && migrated.GrowthLayout[0].X == 154 && migrated.FreeLayout[0].Hidden,"旧JSON完整保留成长与两布局");
    storage.Write(save); var reloaded=storage.Load(); Check(reloaded.SceneId == save.SceneId && JsonSerializer.Serialize(reloaded.Layout) == JsonSerializer.Serialize(save.Layout),"新多场景存档往返");
    storage.Write(save); File.WriteAllText(storage.FilePath,"{broken"); Check(storage.Load().SceneId == save.SceneId,"多场景备份恢复");
} finally { Directory.Delete(directory,true); }
foreach(var id in new[]{"outdoor","meadow","indoor","swingTree"}) {
    Scenes.Switch(free,id); var b=new Behavior(9); Scenes.PlaceActor(free,b); var home=Scenes.Home(free);
    double floor=id switch { "meadow"=>free.Layout.First(p=>p.Id == "meadowBase").Y-28, "indoor"=>free.Layout.First(p=>p.Id == "roomBase").Y-36, "swingTree"=>free.Layout.First(p=>p.Id == "swingSeat").Y+44, _=>252 };
    Check(b.Y == floor,id+"启动落点跟随该场景地面");
    b.Start("walk",home.X+15,home.Y); Advance(b,2,free); Check(b.Y == floor,id+"散步不返回屋外高度");
    if (id is "indoor" or "swingTree") {
        b.Start("stand"); double x=b.X; b.Life.BeginMemory(free); Until(b,"watch",free); Advance(b,3.02,free);
        Check(!b.Life.Active && b.X == x && b.Y == floor,id+"记忆恢复正确地面落点");
    }
}
Console.WriteLine("All "+checks+" scene checks passed.");
