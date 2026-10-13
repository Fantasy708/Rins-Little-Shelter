using LittleShelter;
using System.Text.Json;

var checks = new List<string>();
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); checks.Add(name); Console.WriteLine("PASS: " + name); }
var save = new SaveData(); World.Initialize(save);
Check(save.GrowthLayout.Count == 1 && save.FreeLayout.Count == 12, "初始成长场景和自由场景隔离");
var clock = new CompanionClock(); var start = new DateTime(2026, 10, 10, 10, 0, 0);
clock.Tick(save, start, false, false);
for (int i = 1; i <= 899; i++) clock.Tick(save, start.AddSeconds(i), false, false);
Check(save.Days == 0, "满15分钟前不计陪伴日");
clock.Tick(save, start.AddSeconds(900), false, false);
Check(save.Days == 1, "满15分钟计一个陪伴日");
for (int i = 901; i <= 1000; i++) clock.Tick(save, start.AddSeconds(i), false, false);
Check(save.Days == 1, "同日不重复累计陪伴日");
double before = save.DailySeconds["2026-10-10"];
clock.Tick(save, start.AddSeconds(1001), true, false);
clock.Tick(save, start.AddSeconds(1002), false, true);
clock.Tick(save, start.AddHours(2), false, false);
Check(save.DailySeconds["2026-10-10"] == before, "锁屏、暂停与休眠时间均不计入");
clock.Reset(); clock.Tick(save, new DateTime(2026, 10, 10, 23, 59, 59), false, false);
clock.Tick(save, new DateTime(2026, 10, 11, 0, 0, 1), false, false);
Check(save.DailySeconds["2026-10-11"] == 1 && save.DailySeconds["2026-10-10"] == before + 1, "跨午夜按日期分割秒数");
foreach (int day in new[] { 2, 3, 5, 7, 10, 14, 20 })
{
    var s = new SaveData { CompanionDates = Enumerable.Range(0, day).Select(i => start.AddDays(i).ToString("yyyy-MM-dd")).ToList() }; World.Initialize(s);
    s.Settings.LegacyOutdoorAccess=true; Scenes.Switch(s,"outdoor"); // existing yard item schedule remains compatible
    Check(World.Available(s).All(d => d.Day <= day) && World.Available(s).Any(d => d.Day == day), "第" + day + "天的解锁边界");
}
save.Settings.FreeMode = true; World.Create(save, "house"); save.FreeLayout.First(p => p.Id == "house").X = 42;
save.Settings.FreeMode = false;
Check(save.GrowthLayout.All(p => p.Id != "house") && save.Days == 1 && !save.Created.Contains("house"), "自由创造不影响成长与收藏");
World.Create(save, "house"); Check(save.GrowthLayout.All(p => p.Id != "house"), "未解锁景物不能提前创建");
var behavior = new Behavior(1); int created = 0; string? objectId = null; behavior.ObjectCreated += id => { created++; objectId = id; };
behavior.Create("flower"); behavior.Click(); Check(behavior.Current.Kind == "create", "普通点击不打断创造");
for (int i = 0; i < 60; i++) behavior.Tick(.1, save, false);
Check(created == 1 && objectId == "flower", "创造时间轴恰好生成一次物件");
behavior.Dragging = true; var actionBefore = behavior.Current; double timeBefore = behavior.Time;
behavior.Start("smile"); behavior.Tick(1, save, false);
Check(behavior.Current == actionBefore && behavior.Time == timeBefore, "拖动优先且阻止其他动作");
behavior.Released(); Check(!behavior.Dragging && behavior.Current.Id == "gaze", "松开后观察环境");
behavior.Start("walk", 300, 252); double initial = behavior.X; double midpoint=behavior.ActiveDuration/2; for (int i = 0; i < 25; i++) behavior.Tick(midpoint/25, save, false);
Check(behavior.X > initial && behavior.X < 300, "步行位置连续插值");
var shortVisit=new Behavior(1) { X=250 }; shortVisit.Visit("benchSit",270,252);
var longVisit=new Behavior(1) { X=120 }; longVisit.Visit("benchSit",270,252);
Check(shortVisit.ActiveDuration < longVisit.ActiveDuration && shortVisit.ActiveDuration < 2, "短距离回长椅不再固定滑动五秒");
var walking=new Behavior(1) { X=120 }; walking.Start("walk",300,252);
var jogging=new Behavior(1) { X=120 }; jogging.Start("trot",300,252);
Check(jogging.ActiveDuration < walking.ActiveDuration, "小跑有独立的移动速度与步幅");
double phaseBefore=walking.StepPhase; walking.Tick(0,save,false);
Check(walking.StepPhase == phaseBefore, "没有位移时脚步不自行推进");
double peakSpeed=0, lateSpeed=0; bool phaseTracksTravel=true;
while (longVisit.Current.Id == "visit")
{
    longVisit.Tick(.02,save,false);
    if (longVisit.Current.Id != "visit") break;
    peakSpeed=Math.Max(peakSpeed,longVisit.MotionSpeed);
    if (longVisit.Time > longVisit.ActiveDuration*.9) lateSpeed=longVisit.MotionSpeed;
    phaseTracksTravel &= Math.Abs(longVisit.StepPhase-(longVisit.X-120)/36) < .00001;
}
Check(phaseTracksTravel, "回长椅时脚步与实际移动距离同步");
Check(lateSpeed < peakSpeed*.5 && Math.Abs(longVisit.X-270) < .001, "到长椅前减速且准确到达");
Check(longVisit.Current.Id == "stand" && longVisit.Settling, "到长椅先站稳再坐下");
longVisit.Tick(.15,save,false); longVisit.Tick(.15,save,false);
Check(longVisit.Current.Id == "stand", "站稳过渡保留短暂停顿");
longVisit.Tick(.1,save,false);
Check(longVisit.Current.Id == "benchSit", "站稳后进入长椅坐姿");
shortVisit.Tick(.1,save,false); shortVisit.Start("smile"); for(int i=0;i<6;i++) shortVisit.Tick(.1,save,false);
Check(shortVisit.Current.Id == "smile", "新交互取消待执行的坐下动作");
void Advance(Behavior actor, double seconds) { while(seconds > .000001) { double dt=Math.Min(.02,seconds); actor.Tick(dt,save,false); seconds-=dt; } }
var hopping=new Behavior(1) { X=240, Y=252 }; hopping.Start("hop",390,100);
Advance(hopping,MotionClips.Hop.Duration*.25);
Check(hopping.X == 240 && hopping.Y == 252 && MotionClips.Hop.Sample(hopping.Time).FootLift == 0, "轻跃蓄力时双脚不滑动且保持平台高度");
Advance(hopping,MotionClips.Hop.Duration*.2);
Check(hopping.X > 240 && hopping.X < 270 && MotionClips.Hop.Sample(hopping.Time).FootLift > 30, "轻跃只在腾空阶段前移");
Advance(hopping,MotionClips.Hop.Duration*.25);
Check(Math.Abs(hopping.X-270) < .001 && MotionClips.Hop.Sample(hopping.Time).FootLift == 0, "轻跃落点限制在30像素并结束腾空");
double landingX=hopping.X; Advance(hopping,MotionClips.Hop.Duration*.15);
Check(hopping.Current.Id == "hop" && hopping.X == landingX, "落地缓冲与恢复期间脚部不继续漂移");
hopping.Dragging=true; double frozenTime=hopping.Time; hopping.Tick(.1,save,false);
Check(hopping.Time == frozenTime, "拖动中轻跃动画保持冻结");
hopping.Released(); Check(hopping.Current.Id == "gaze", "拖动打断轻跃后进入观察而非补播跳跃");
var turning=new Behavior(1) { X=350, Y=252 }; turning.Start("turn",100,140);
Check(!turning.FacingLeft && !turning.SequenceMirror && turning.X == 350, "转身开始时不立即镜像或向目标滑动");
Advance(turning,MotionClips.Turn.Duration*.39); Check(!turning.FacingLeft && turning.X == 350 && turning.Y == 252, "视线与肩髋转动阶段保持原支撑位置");
Advance(turning,MotionClips.Turn.Duration*.22); Check(turning.FacingLeft && !turning.SequenceMirror, "转身后半程更新朝向而不再次镜像图集");
Advance(turning,MotionClips.Turn.Duration*.4); Check(turning.Current.Id == "stand" && turning.FacingLeft, "转身结束保持新朝向并自然站稳");
turning.Start("turn"); Check(turning.SequenceMirror, "从左朝向开始时完整反向播放转身姿势");
turning.Start("smile"); Advance(turning,.5); Check(turning.Current.Id == "smile" && turning.FacingLeft, "点击打断转身后不残留翻面或待执行动作");
foreach (var clip in new[]{MotionClips.Hop,MotionClips.Turn})
{
    Check(clip.Keys.Zip(clip.Keys.Skip(1),(a,b)=>(b-a)*clip.Duration).Max()<.14, "轻跃与转身每个姿势间隔均短于140毫秒");
    double a=clip.Keys[4]*clip.Duration, span=(clip.Keys[5]-clip.Keys[4])*clip.Duration;
    double early=clip.Sample(a+span*.1).Blend, middle=clip.Sample(a+span*.5).Blend, late=clip.Sample(a+span*.9).Blend;
    Check(Math.Abs((middle-early)-(late-middle))<.000001 && early>.09, "姿势衔接持续推进，不在每张关键图处重复缓停");
}
Check(Actions.All.Length == 35 && Actions.All.Select(a => a.Id).Distinct().Count() == 35, "35组动作具有唯一标识（保留原动作，新增独立手动草地彩蛋）");
Check(!WindowPolicy.IsFullscreen(0x01C40000, (0,0,1920,1080), (0,0,1920,1080)), "自动隐藏任务栏时普通最大化不隐藏桌宠");
Check(!WindowPolicy.IsFullscreen(0x01040000, (0,0,1920,1080), (0,0,1920,1080)), "无标题栏的普通最大化仍保持陪伴");
Check(WindowPolicy.IsFullscreen(0, (0,0,1920,1080), (0,0,1920,1080)), "无边框全屏隐藏桌宠");
Check(!WindowPolicy.IsFullscreen(0, (0,0,1920,1040), (0,0,1920,1080)), "未覆盖完整显示器的窗口不视为全屏");
Check(World.Theme(new Settings(), 6) == "清晨" && World.Theme(new Settings(), 23) == "夜晚" && World.Theme(new Settings(), 2) == "深夜", "自动天空时间段");
var folder = Path.Combine(Path.GetTempPath(), "ShelterCoreTests-" + Guid.NewGuid());
try
{
    var storage = new Storage(folder); storage.Write(save); save.Interactions = 17; storage.Write(save);
    var reloaded = storage.Load(); Check(reloaded.Interactions == 17 && reloaded.FreeLayout.First(p => p.Id == "house").X == 42, "重启保存设置、进度和独立布局");
    File.WriteAllText(storage.FilePath, "{broken"); var recovered = storage.Load();
    Check(recovered.Days == 1 && recovered.Interactions == 0, "损坏主存档恢复备份");
    File.WriteAllText(storage.FilePath, "{}"); Check(storage.Load().Days == 1, "缺少版本的存档恢复备份");
    File.WriteAllText(storage.FilePath, "{\"Version\":1,\"Settings\":null}"); Check(storage.Load().Days == 1, "空设置对象恢复备份");
    File.WriteAllText(storage.FilePath, "{\"Version\":2}"); Check(storage.Load().Days == 1, "未知存档版本恢复备份");
    File.WriteAllText(storage.FilePath + ".bak", "{broken"); Check(storage.Load().GrowthLayout.Count == 1, "两个存档损坏时安全恢复初始世界");
}
finally { Directory.Delete(folder, true); }
Console.WriteLine("All " + checks.Count + " checks passed.");

