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
double Distance(OutdoorPoint a,OutdoorPoint c) => Math.Sqrt(Math.Pow(a.X-c.X,2)+Math.Pow(a.Y-c.Y,2));
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
Check(b.X>x && b.Y<y,"正常换重期间身体连续登高，不再每步停住整个人");
Advance(b,mountain,.22);
Check(b.X>x && b.Y<y && b.Journey.PoseFrame is >=0 and <16,"转移重心时沿坡前进并推进上山交替姿势");
Until(b,mountain,"look");
Check(b.Journey.AtSummit && b.Current.Id == "sky" && b.X == 320 && b.Y == 178,"到达真实峰顶并安静远望");
Advance(b,mountain,2);
Check(b.X == 320 && b.Y == 178 && b.Journey.Active,"峰顶观察不被普通回家规则拖走");
Until(b,mountain,"descend");
Check(b.FacingLeft && b.Journey.Direction == -1 && b.Journey.PoseFrame is >=16 and <32,"下山前真实转身并选专用下坡交替姿势");
Until(b,mountain,"settle"); Advance(b,mountain,.51);
Check(!b.Journey.Active && b.X == Scenes.Home(mountain).X && b.Y == Scenes.Home(mountain).Y,"完成下山在山脚站稳恢复日常");
Check(Snapshot(mountain) == snapshot,"攀登全程不修改布置进度和故事");
foreach(double slice in new[]{1.0/30,1.0/60}) {
    var chain=Actor(mountain);chain.Journey.Begin(mountain,"mountainClimb");
    var phases=new List<string>{chain.Journey.Phase};
    for(int i=0;i<4000 && chain.Journey.Active;i++) {
        chain.Tick(slice,mountain,false);
        if(phases[^1]!=chain.Journey.Phase)phases.Add(chain.Journey.Phase);
    }
    string[] expected={"approach","ascendPrepare","ascend","summitSettle","summitStand","look","summitReady","turnDown","descendPrepare","descend","footSettle","settle","none"};
    Check(phases.Where(p=>p!="turnUp").SequenceEqual(expected),"完整自然链显式经过收脚站立、观察、回正、转身和下山 "+slice);
    Check(!chain.MotionExit.Active && chain.Current.Id=="stand","登山使用同一Behavior完成转身且不被通用收尾抢占 "+slice);
}
foreach(string turn in new[]{"turnUp","turnDown"}) {
    var turning=Actor(mountain);if(turn=="turnUp")turning.X+=100;
    turning.Journey.Begin(mountain,"mountainClimb");Until(turning,mountain,turn);
    var ready=new List<string>();turning.RecoveryActionReady+=ready.Add;
    Check(turning.RecoverBeforeAction(mountain,"gaze") && turning.RecoverBeforeAction(mountain,"smile"),"山径转身期间保持最新软返回请求 "+turn);
    double turnX=turning.X,turnY=turning.Y;var exits=new HashSet<string>();
    for(int i=0;i<1000 && turning.Journey.Phase==turn;i++) {
        if(turning.MotionExit.Active) {
            exits.Add(turning.MotionExit.Phase);
            Check(turning.MotionExit.ActionId=="turn" && turning.Current.Id=="turn" && ready.Count==0 && turning.X==turnX && turning.Y==turnY,"专用转身收尾保持山径位置且不提前派发请求 "+turn);
        }
        turning.Tick(.01,mountain,false);
    }
    string prepare=turn=="turnUp" ? "ascendPrepare" : "descendPrepare";
    Check(exits.IsSupersetOf(new[]{"poses","neutral"}) && !turning.MotionExit.Active && turning.Journey.Phase==prepare && turning.Journey.StandingAmount==1 && turning.Journey.Elapsed==0,"转身经完整收尾及精确站姿后才开始攀登准备 "+turn);
    Check(turning.PendingRecoveryAction=="smile" && ready.Count==0 && turning.Journey.Recovering,"转身收尾结束仍保留Journey的最后请求 "+turn);
    for(int i=0;i<5000 && turning.Journey.Active;i++)turning.Tick(.01,mountain,false);
    Check(ready.SequenceEqual(new[]{"smile"}),"完成真实下山站稳后才派发转身期间的最后请求 "+turn);
}
foreach(string interruption in new[]{"drag","scene"}) {
    var interruptedTurn=State();var turning=Actor(interruptedTurn);turning.Journey.Begin(interruptedTurn,"mountainClimb");Until(turning,interruptedTurn,"turnDown");
    for(int i=0;i<1000 && !turning.MotionExit.Active;i++)turning.Tick(.01,interruptedTurn,false);
    Check(turning.MotionExit.DedicatedPose,"硬中断检查进入专用转身收尾 "+interruption);
    if(interruption=="drag")turning.Dragging=true;else Scenes.Switch(interruptedTurn,"polar");
    double atX=turning.X,atY=turning.Y;turning.Tick(.01,interruptedTurn,false);
    Check(!turning.Journey.Active && !turning.MotionExit.Active && !turning.Bridge.Active,"拖拽或场景变化立即取消山径专用转身收尾 "+interruption);
    Check(interruption=="drag" ? turning.X==atX && turning.Y==atY : turning.X==Scenes.Home(interruptedTurn).X && turning.Y==Scenes.Home(interruptedTurn).Y,"转身收尾硬中断遵循手势或新场景落点 "+interruption);
}
var coarseTurn=Actor(mountain);coarseTurn.Journey.Begin(mountain,"mountainClimb");Until(coarseTurn,mountain,"turnDown");
for(int i=0;i<1000 && !coarseTurn.MotionExit.Active;i++)coarseTurn.Tick(.01,mountain,false);
coarseTurn.Tick(.15,mountain,false);coarseTurn.Tick(.07,mountain,false);coarseTurn.Tick(.15,mountain,false);
Check(coarseTurn.Journey.Phase=="turnDown" && coarseTurn.MotionExit.Phase=="neutral" && coarseTurn.MotionExit.NeutralProgress==1,"粗步也保留转身收尾的精确站姿供实际呈现");
coarseTurn.Tick(0,mountain,false);
Check(coarseTurn.MotionExit.Phase=="neutral" && coarseTurn.MotionExit.NeutralProgress==1,"暂停时间不吞掉转身收尾站姿终点");
coarseTurn.Tick(.01,mountain,false);
Check(!coarseTurn.MotionExit.Active && coarseTurn.Journey.Phase=="descendPrepare" && coarseTurn.Journey.Elapsed==0,"精确站姿呈现机会之后才进入下山准备");
coarseTurn.Journey.Cancel(mountain);
foreach(string phase in new[]{"ascendPrepare","descendPrepare","summitSettle","footSettle"}) {
    var bridge=Actor(mountain);bridge.Journey.Begin(mountain,"mountainClimb");Until(bridge,mountain,phase);
    double duration=phase.EndsWith("Prepare") ? OutdoorJourney.PrepareSeconds : OutdoorJourney.SettleSeconds;
    double amount=bridge.Journey.StandingAmount;
    var initialFeet=bridge.Journey.FootContacts.ToArray();
    double startX=bridge.X,startY=bridge.Y;
    Advance(bridge,mountain,duration*.4,.005);
    Check(bridge.Journey.Phase==phase && bridge.Journey.DedicatedPose,"站立与攀登之间保留可观察的独立动作阶段 "+phase);
    Check(phase.EndsWith("Prepare") ? bridge.Journey.StandingAmount<amount : bridge.Journey.StandingAmount>amount,"连续骨架桥按真实时间连接站姿，不依靠80毫秒淡化 "+phase);
    Check(bridge.Journey.FootContacts.Select((v,i)=>Distance(v,initialFeet[i])).Max()>.5,"准备与收尾确实移动脚部目标 "+phase);
    if(phase.EndsWith("Prepare")) Check(bridge.X==startX && bridge.Y==startY,"起步先调整腿部并保留地面根 "+phase);
    else Check(Math.Abs(bridge.X-startX)>.1 || Math.Abs(bridge.Y-startY)>.1,"末步后身体随收脚站稳而回正 "+phase);
    while(bridge.Journey.Phase==phase && bridge.Journey.Elapsed<duration-.0051)bridge.Tick(.005,mountain,false);
    double priorX=bridge.X,priorY=bridge.Y;
    var priorFeet=bridge.Journey.FootContacts.ToArray();
    const double boundaryStep=.01;
    bridge.Tick(boundaryStep,mountain,false);
    // This sample can already contain the first moving gait interval. Bound
    // its local speed while still rejecting the former six-pixel root snap.
    double rootTravel=Math.Sqrt(Math.Pow(bridge.X-priorX,2)+Math.Pow(bridge.Y-priorY,2));
    Check(double.IsFinite(rootTravel) && rootTravel<=boundaryStep*80+.05,"准备或收尾边界按有限局部速度前进，不跳动身体根 "+phase);
    Check(bridge.Journey.Phase==phase && bridge.Journey.EndpointReady && bridge.Journey.Elapsed==duration && bridge.Journey.StepPhase==1,"四个桥接阶段均先保留可绘制的精确终点 "+phase);
    Check(bridge.Journey.StandingAmount==(phase.EndsWith("Prepare") ? 0 : 1),"精确呈现完整攀登或完整站姿后才切换阶段 "+phase);
    var endpointFeet=bridge.Journey.FootContacts.ToArray();
    bridge.Tick(boundaryStep,mountain,false);
    if(bridge.Journey.DedicatedPose) Check(bridge.Journey.FootContacts.Select((v,i)=>Distance(v,endpointFeet[i])).Max()<.3,"准备完成后双脚连续进入第一步 "+phase);
    else Check(bridge.Current.Id=="stand" && bridge.Journey.StandingAmount==1,"收尾先进入完整站姿后才交给下个动作 "+phase);
}
foreach(string phase in new[]{"ascendPrepare","descendPrepare","summitSettle","footSettle"}) {
    var endpoint=Actor(mountain);endpoint.Journey.Begin(mountain,"mountainClimb");Until(endpoint,mountain,phase);
    double duration=phase.EndsWith("Prepare") ? OutdoorJourney.PrepareSeconds : OutdoorJourney.SettleSeconds;
    endpoint.Tick(duration-endpoint.Journey.Elapsed+.075,mountain,false);
    Check(endpoint.Journey.Phase==phase && endpoint.Journey.EndpointReady && endpoint.Journey.Elapsed==duration,"粗时间步也不会截掉最终完整姿势 "+phase);
    endpoint.Tick(0,mountain,false);
    Check(endpoint.Journey.Phase==phase && endpoint.Journey.EndpointReady,"零时间或冻结期间继续保留终点画面 "+phase);
    Check(endpoint.Journey.RequestReturn(mountain) && endpoint.Journey.EndpointReady,"终点期间软返回保留实际完成姿势 "+phase);
    endpoint.Tick(.025,mountain,false);
    string next=phase switch { "ascendPrepare"=>"ascend","descendPrepare"=>"descend","summitSettle"=>"summitStand",_=>"settle" };
    Check(endpoint.Journey.Phase==next && !endpoint.Journey.EndpointReady && Math.Abs(endpoint.Journey.Elapsed-.1)<1e-8,"终点下一次推进续用粗步余量，不按帧数延长动作 "+phase);
    endpoint.Journey.Cancel(mountain);
    Check(!endpoint.Journey.EndpointReady && !endpoint.Journey.Recovering,"硬中断清理已呈现终点及其时间余量 "+phase);
    endpoint.Journey.Begin(mountain,"mountainClimb");Until(endpoint,mountain,"ascendPrepare");
    endpoint.Tick(.01,mountain,false);
    Check(Math.Abs(endpoint.Journey.Elapsed-.01)<1e-8 && !endpoint.Journey.EndpointReady,"重新开始不带入旧终点时间 "+phase);
}
var summitDown=Actor(mountain);summitDown.Journey.Begin(mountain,"mountainClimb");Until(summitDown,mountain,"look");
double summitX=summitDown.X,summitY=summitDown.Y;
Check(summitDown.Journey.RequestDescent(mountain) && summitDown.Journey.Phase=="summitReady" && summitDown.X==summitX && summitDown.Y==summitY,"山顶手动下山保留当前位置并先回正");
Until(summitDown,mountain,"descendPrepare");
Check(summitDown.FacingLeft && summitDown.Journey.Direction==-1 && summitDown.Journey.StandingAmount>.99,"下山先完成转身再从站姿调整到下降步态");
Until(summitDown,mountain,"settle");Advance(summitDown,mountain,.51);
Check(!summitDown.Journey.Active,"山顶请求完整执行下降和山脚收尾");
foreach(string phase in new[]{"approach","ascendPrepare","ascend","summitSettle","summitStand","look","summitReady","turnDown","descendPrepare","descend","footSettle","settle"}) {
    var returning=Actor(mountain);returning.Journey.Begin(mountain,"mountainClimb");if(phase!="approach")Until(returning,mountain,phase);
    double atX=returning.X,atY=returning.Y;var saved=Snapshot(mountain);
    Check(returning.Journey.CanRecover && returning.Journey.RequestReturn(mountain) && returning.Journey.Recovering,"显式结束请求沿原山径返回 "+phase);
    Check(returning.X==atX && returning.Y==atY && returning.Journey.Active,"返回请求保留当前山径位置，不立即移回山脚 "+phase);
    var seen=new HashSet<string>();
    for(int i=0;i<5000 && returning.Journey.Active;i++) {seen.Add(returning.Journey.Phase);returning.Tick(.01,mountain,false);}
    if(phase is not ("descendPrepare" or "descend" or "footSettle" or "settle")) Check(seen.Contains("descend") && !seen.Contains("look"),"上山或山顶软结束会省略远望并真实下山 "+phase);
    Check(!returning.Journey.Active && !returning.Journey.Recovering && returning.Current.Id=="stand" && returning.X==Scenes.Home(mountain).X && returning.Y==Scenes.Home(mountain).Y,"返回完成后才解除恢复状态并站稳 "+phase);
    Check(Snapshot(mountain)==saved,"软返回不写入成长或两模式布局 "+phase);
}
foreach(string id in new[]{"gaze","smile"}) {
    var queued=Actor(mountain);queued.Journey.Begin(mountain,"mountainClimb");Until(queued,mountain,"ascend");
    var dispatched=new List<string>();queued.RecoveryActionReady+=dispatched.Add;
    Check(queued.RecoverBeforeAction(mountain,id) && queued.Journey.Recovering,"普通动作等待山径返回 "+id);
    Check(dispatched.Count==0 && queued.Journey.Active,"恢复期间不提前派发普通动作 "+id);
    for(int i=0;i<5000 && queued.Journey.Active;i++)queued.Tick(.01,mountain,false);
    Check(dispatched.SequenceEqual(new[]{id}) && queued.X==Scenes.Home(mountain).X && queued.Y==Scenes.Home(mountain).Y,"回到山脚完整站稳后只派发一次最新请求 "+id);
}
var latest=Actor(mountain);latest.Journey.Begin(mountain,"mountainClimb");Until(latest,mountain,"look");
var latestDispatched=new List<string>();latest.RecoveryActionReady+=latestDispatched.Add;
Check(latest.RecoverBeforeAction(mountain,"gaze") && latest.RecoverBeforeAction(mountain,"smile") && latest.PendingRecoveryAction=="smile","返回期间新动作覆盖唯一待执行请求");
for(int i=0;i<5000 && latest.Journey.Active;i++)latest.Tick(.01,mountain,false);
Check(latestDispatched.SequenceEqual(new[]{"smile"}),"同一返回链只派发最终动作一次");
var tabletReturn=Actor(mountain);tabletReturn.Journey.Begin(mountain,"mountainClimb");Until(tabletReturn,mountain,"look");
var tabletDispatched=new List<string>();tabletReturn.RecoveryTabletReady+=(page,preselect)=>tabletDispatched.Add(page+":"+preselect);
Check(tabletReturn.RecoverBeforeTablet(mountain,"messages",null) && tabletReturn.Journey.Recovering && !tabletReturn.Tablet.Active,"山顶主动消息请求先回正下山，不提前打开平板");
for(int i=0;i<5000 && tabletReturn.Journey.Active;i++)tabletReturn.Tick(.01,mountain,false);
Check(tabletDispatched.SequenceEqual(new[]{"messages:"}),"完成山脚站稳后才派发原消息页请求");
var interrupted=Actor(mountain);interrupted.Journey.Begin(mountain,"mountainClimb");Until(interrupted,mountain,"ascend");
interrupted.RecoverBeforeAction(mountain,"gaze");var interruptedDispatched=new List<string>();interrupted.RecoveryActionReady+=interruptedDispatched.Add;
mountain.Layout.First(p=>p.Id=="mountainBase").X+=1;interrupted.Tick(.01,mountain,false);
Check(!interrupted.Journey.Active && interrupted.PendingRecoveryAction==null && interruptedDispatched.Count==0,"山基移动硬中断清除软返回和待执行请求");
mountain.Layout.First(p=>p.Id=="mountainBase").X-=1;
foreach(string phase in new[]{"ascend","descend"}) {
    var gait=Actor(mountain); gait.Journey.Begin(mountain,"mountainClimb"); Until(gait,mountain,phase);
    var path=gait.Journey.Route.ToArray(); int count=path.Length-1;
    for(int half=0;half<count;half++) {
        int expected=half%2; var stone=path[phase=="ascend" ? half : count-half];
        bool planted=true,moved=false; OutdoorPoint? lastSwing=null;
        for(int frame=0;frame<36;frame++) {
            if(gait.Journey.Phase!=phase)break;
            var feet=gait.Journey.FootContacts;
            planted &= gait.Journey.SupportFoot==expected && feet[expected]==stone;
            if(lastSwing is {} prior) moved |= Math.Abs(prior.X-feet[1-expected].X)>.01;
            lastSwing=feet[1-expected];gait.Tick(OutdoorJourney.StepSeconds/36,mountain,false);
        }
        Check(planted,"左右腿轮流支撑且整个半步保持同一石阶 "+phase+"/"+half);
        Check(moved,"另一条腿跨向下一阶，不是整个人平移 "+phase+"/"+half);
    }
    Check(gait.Journey.Phase==(phase=="ascend" ? "summitSettle" : "footSettle"),"末步进入收脚阶段 "+phase);
}
var coarse=Actor(mountain); coarse.Journey.Begin(mountain,"mountainClimb"); Until(coarse,mountain,"ascend");
double coarseBefore=coarse.Journey.Elapsed;
coarse.Tick(2,mountain,false);
Check(Math.Abs(coarse.Journey.Elapsed-coarseBefore-2)<1e-8 && coarse.Journey.RouteProgress>.4,"专用攀登按实际时间推进不按帧数延长");
coarse.Journey.Cancel(mountain);
b=Actor(mountain); b.Journey.Begin(mountain,"mountainClimb"); Until(b,mountain,"look");
Check(!b.Tablet.Open(mountain,true) && b.Journey.AtSummit && mountain.ChoiceCounts.Count == 0,"主动邀请不会打断登顶或消耗每日邀请次数");
Check(b.Tablet.Open(mountain,false,"messages") && !b.Journey.Active && b.Y == Scenes.Home(mountain).Y,"用户主动消息入口优先取消山顶动作并回到地面持板");
Check(!b.Journey.CanBegin(mountain,"mountainClimb"),"持板界面活动时不重叠启动攀登"); b.Tablet.Cancel(mountain);
b=Actor(mountain); b.Journey.Begin(mountain,"mountainClimb"); Until(b,mountain,"ascend");
mountain.Settings.FreeMode=true; b.Tick(.1,mountain,false);
Check(!b.Journey.Active && b.Y == Scenes.Home(mountain).Y,"模式切换取消旧山径且不恢复半段动作"); mountain.Settings.FreeMode=false;
foreach(var phase in new[]{"approach","ascendPrepare","ascend","summitSettle","summitStand","look","summitReady","turnDown","descendPrepare","descend","footSettle","settle"}) {
    b=Actor(mountain); b.Journey.Begin(mountain,"mountainClimb"); if (phase != "approach") Until(b,mountain,phase);
    b.Start("smile"); Advance(b,mountain,.2);
    Check(!b.Journey.Active && b.Current.Id == "smile" && b.X == Scenes.Home(mountain).X && b.Y == Scenes.Home(mountain).Y,"外部动作中断 "+phase+" 后回到当前场景落点");
}
foreach(var phase in new[]{"ascendPrepare","ascend","summitSettle","summitStand","look","summitReady","descendPrepare","descend","footSettle"}) {
    b=Actor(mountain); b.Journey.Begin(mountain,"mountainClimb"); Until(b,mountain,phase);
    b.Dragging=true; double dragX=b.X,dragY=b.Y; b.Tick(.1,mountain,false);
    Check(!b.Journey.Active && b.X == dragX && b.Y == dragY,"拖动中断 "+phase+"不覆盖用户手势位置");
}
b=Actor(mountain); b.Journey.Begin(mountain,"mountainClimb"); Until(b,mountain,"look"); Scenes.Switch(mountain,"polar"); b.Tick(.02,mountain,false);
Check(!b.Journey.Active && b.X == Scenes.Home(mountain).X && b.Y == Scenes.Home(mountain).Y,"切换场景中断山峰动作并使用新场景落点");
foreach(bool bridgePrimed in new[]{false,true}) {
    var switched=State();var changing=Actor(switched);changing.Journey.Begin(switched,"mountainClimb");Until(changing,switched,"look");
    Check(changing.Bridge.Active,"山顶抬头桥正在等待呈现 "+bridgePrimed);
    if(bridgePrimed)changing.Tick(.01,switched,false);
    Scenes.Switch(switched,"polar");changing.Tick(.02,switched,false);
    var home=Scenes.Home(switched);
    Check(!changing.Journey.Active && !changing.Bridge.Active && changing.Current.Id=="stand" && changing.X==home.X && changing.Y==home.Y,"场景变化先硬取消已初始化或未初始化的山顶桥并使用新落点 "+bridgePrimed);
    changing=Actor(switched);changing.Journey.Begin(switched,"auroraWatch");Until(changing,switched,"watch");
    if(bridgePrimed)changing.Tick(.01,switched,false);
    switched.Settings.FreeMode=true;changing.Tick(.02,switched,false);home=Scenes.Home(switched);
    Check(!changing.Journey.Active && !changing.Bridge.Active && changing.Current.Id=="stand" && changing.X==home.X && changing.Y==home.Y,"模式变化先取消极地抬头桥并恢复当前模式落点 "+bridgePrimed);
}
var bridgeInvalid=State();var invalidActor=Actor(bridgeInvalid);invalidActor.Journey.Begin(bridgeInvalid,"mountainClimb");Until(invalidActor,bridgeInvalid,"look");
invalidActor.RecoverBeforeAction(bridgeInvalid,"gaze");
Check(invalidActor.Journey.Recovering && invalidActor.Bridge.Active,"返回的回正桥保持待执行请求");
bridgeInvalid.Layout.First(p=>p.Id=="mountainBase").X+=1;invalidActor.Tick(.02,bridgeInvalid,false);
Check(!invalidActor.Journey.Active && !invalidActor.Bridge.Active && invalidActor.PendingRecoveryAction==null && invalidActor.Current.Id=="stand" && invalidActor.X==Scenes.Home(bridgeInvalid).X && invalidActor.Y==Scenes.Home(bridgeInvalid).Y,"山径变化在回正桥前清除旧动作与排队请求");
var polar=mountain;
foreach(string id in new[]{"auroraWatch","snowTouch"}) {
    b=Actor(polar); Check(b.Journey.Begin(polar,id),"极地专属动作可启动 "+id); Until(b,polar,id == "auroraWatch" ? "watch" : "snow");
    var home=Scenes.Home(polar); Advance(b,polar,3); Check(b.X == home.X && b.Y == home.Y,"极地观察/接雪不整人平移 "+id);
    b.Click(polar); Check(b.Journey.Active && b.Journey.Recovering && b.X==home.X && b.Y==home.Y,"点击极地动作先执行自然收尾，不硬切角色 "+id);
    for(int i=0;i<1500 && b.Journey.Active;i++)b.Tick(.01,polar,false);
    Check(!b.Journey.Active && b.Current.Id=="stand" && !b.Bridge.Active,"极地动作完成手部或抬头回正后才结束 "+id);
}
foreach(string id in new[]{"auroraWatch","snowTouch"}) {
    var ending=Actor(polar);ending.Journey.Begin(polar,id);Until(ending,polar,id=="auroraWatch" ? "watch" : "snow");
    while(ending.Bridge.Active)ending.Tick(.01,polar,false);
    var before=Snapshot(polar);var ready=new List<string>();ending.RecoveryActionReady+=ready.Add;
    Check(ending.RecoverBeforeAction(polar,"gaze") && ending.RecoverBeforeAction(polar,"smile") && ending.Journey.Recovering,"极地软结束保留最新单个动作请求 "+id);
    bool sawStandBridge=false;
    for(int i=0;i<1800 && ending.Journey.Active;i++) {
        if(ending.Journey.Phase=="polarSettle" && ending.Bridge.Active) {
            sawStandBridge=true;double elapsed=ending.Journey.Elapsed;
            Check(ready.Count==0,"极地回正桥期间不派发排队请求 "+id);
            ending.Tick(.01,polar,false);
            Check(ending.Journey.Elapsed==elapsed,"极地回正桥冻结原动作时钟 "+id);
        } else ending.Tick(.01,polar,false);
    }
    Check(sawStandBridge && ready.SequenceEqual(new[]{"smile"}) && !ending.Journey.Active,"极地回正站稳后只执行最终请求 "+id);
    Check(Snapshot(polar)==before,"极地软结束不改变存档 "+id);
}
foreach(double requestAt in new[]{.1,.75,1.1,2.0,6.2}) {
    var snow=Actor(polar);snow.Journey.Begin(polar,"snowTouch");Until(snow,polar,"snow");
    Advance(snow,polar,requestAt,.005);double before=snow.Journey.Elapsed;
    Check(snow.Journey.RequestReturn(polar) && snow.Journey.Phase=="snow","接雪各阶段均以专用收手结束 "+requestAt);
    if(before<OutdoorJourney.SnowRaiseSeconds) {
        Check(snow.Journey.Elapsed==before,"初始抬手未完成时不跳过姿势 "+requestAt);
        while(snow.Journey.Elapsed<OutdoorJourney.SnowRaiseSeconds-.011)snow.Tick(.005,polar,false);
        Check(snow.Journey.Phase=="snow" && snow.Journey.Elapsed<OutdoorJourney.SnowRaiseSeconds,"抬手按原真实时间继续至完整高度 "+requestAt);
        snow.Tick(.02,polar,false);
        Check(snow.Journey.Elapsed>=OutdoorJourney.SnowRetractionStart && snow.Journey.Elapsed<OutdoorJourney.SnowRetractionStart+.03,"达到抬手端点后只省略静止停留 "+requestAt);
    } else if(before<OutdoorJourney.SnowRetractionStart) Check(snow.Journey.Elapsed==OutdoorJourney.SnowRetractionStart,"完整持掌期间可直接开始专用收手 "+requestAt);
    else Check(snow.Journey.Elapsed==before,"已经收手时不会重放早期动作 "+requestAt);
    bool sawSettle=false;
    for(int i=0;i<1200 && snow.Journey.Active;i++) {sawSettle|=snow.Journey.Phase=="polarSettle";snow.Tick(.01,polar,false);}
    Check(sawSettle && !snow.Journey.Active && snow.Current.Id=="stand","接雪先收手再完整站稳结束 "+requestAt);
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
