using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LittleShelter;

internal static class OutdoorWindowTests
{
    [STAThread] private static void Main(string[] args)
    {
        string root=Path.GetFullPath(args.Length>0 ? args[0] : "output/outdoor-window-qa");
        string data=Path.Combine(root,"save-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(data);
        var storage=new Storage(data); var save=new SaveData(); save.Settings.Music=false;
        save.CompanionDates=Enumerable.Range(0,20).Select(i=>new DateTime(2026,1,1).AddDays(i).ToString("yyyy-MM-dd")).ToList();
        World.Initialize(save);
        var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
        var pet=new PetWindow(storage,save,true); app.MainWindow=pet; int checks=0;
        void Check(bool value,string label) { if (!value) throw new Exception("FAIL: "+label); checks++; Console.WriteLine("PASS: "+label); }
        void Field(string name,object value) => typeof(PetWindow).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(pet,value);
        void Visibility() => typeof(PetWindow).GetMethod("UpdateVisibility",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(pet,null);
        void Advance(double seconds) { while(seconds>1e-7) { double dt=Math.Min(.01,seconds); pet.Behavior.Tick(dt,save,false); seconds-=dt; } }
        void Until(string phase) { for(int i=0;i<12000 && pet.Behavior.Journey.Phase!=phase;i++) Advance(.01); Check(pet.Behavior.Journey.Phase==phase,"生产时间轴到达 "+phase); }
        void FinishJourney(string source,string request)
        {
            bool descent=pet.Behavior.Journey.Phase=="descend";
            for(int i=0;i<16000 && pet.Behavior.Journey.Active;i++) {
                Advance(.01);descent|=pet.Behavior.Journey.Phase=="descend";
            }
            Check(!pet.Behavior.Journey.Active,"软请求完成实际路线返回 "+source+"/"+request);
            if(save.SceneId=="mountain" && source!="footSettle")Check(descent,"软请求经过真实下山而非瞬移 "+source+"/"+request);
        }
        void Reset(string scene="mountain")
        {
            Field("sessionLocked",false); Field("fullscreen",false); Field("suspended",false); Visibility();
            if(pet.Paused) pet.TogglePause(); if(!pet.IsVisible) pet.ToggleHidden();
            if(pet.Scene.Editing) pet.ToggleEditing();
            save.Settings.ClickThrough=false; pet.ApplySettings();
            pet.Behavior.Journey.Cancel(save); pet.Behavior.Nurture.Cancel(save); pet.Behavior.Life.Cancel(); pet.Behavior.Tablet.Cancel(save);
            if(save.Settings.FreeMode) pet.SwitchMode();
            pet.SwitchScene(scene); pet.Behavior.Start("stand");
        }
        string Layouts() => JsonSerializer.Serialize(new{save.GrowthLayout,save.FreeLayout,save.GrowthSceneLayouts,save.FreeSceneLayouts,save.Created});
        void Begin(string phase)
        {
            Reset(); Check(pet.CanExplore("mountainClimb") && pet.Explore("mountainClimb"),"生产入口开始山脉攀登"); Until(phase);
        }
        try
        {
            pet.Show(); pet.SkipStartup();
            foreach(string phase in new[]{"ascend","summitSettle","look","descend","footSettle"}) foreach(string stop in new[]{"editing","scene","mode","tablet","messages","restore","hideObject","through","action","recover","end"})
            {
                Begin(phase); string before=Layouts();
                double x=pet.Behavior.X,y=pet.Behavior.Y,elapsed=pet.Behavior.Journey.Elapsed;
                int generation=pet.Behavior.Journey.Generation;
                switch(stop) {
                    case "editing": pet.ToggleEditing(); break;
                    case "scene": pet.SwitchScene("polar"); break;
                    case "mode": pet.SwitchMode(); break;
                    case "tablet": pet.OfferChoice(false); break;
                    case "messages": pet.OpenMessages(); break;
                    case "restore": pet.RestoreLayout(); break;
                    case "hideObject": pet.HideObject("cloud"); break;
                    case "through": save.Settings.ClickThrough=true; pet.ApplySettings(); break;
                    case "action": pet.PreviewAction("smile"); break;
                    case "recover": pet.Recover(); break;
                    case "end": pet.EndExploration(); break;
                }
                if(stop is "tablet" or "messages" or "action" or "recover" or "end") {
                    Check(pet.Behavior.Journey.Active && pet.Behavior.Journey.Recovering && pet.Behavior.X==x && pet.Behavior.Y==y && !pet.Behavior.Tablet.Active,"普通请求先保留当前落点并等待物理返回 "+phase+"/"+stop);
                    FinishJourney(phase,stop);
                } else if(stop=="hideObject") {
                    Check(pet.Behavior.Journey.Active && pet.Behavior.Journey.Generation==generation && pet.Behavior.Journey.Phase==phase && pet.Behavior.Journey.Elapsed==elapsed && pet.Behavior.X==x && pet.Behavior.Y==y,"收起无关云朵保留有效山径及原动作进度 "+phase);
                    Check(save.Layout.Single(p=>p.Id=="cloud").Hidden,"收起操作仍保存云朵的隐藏状态 "+phase);
                } else Check(!pet.Behavior.Journey.Active,"硬中断即时取消山脉时间轴 "+phase+"/"+stop);
                if(stop=="hideObject")continue;
                Check(pet.Behavior.Y==Scenes.Home(save).Y,"中断后恢复当前场景地面 "+phase+"/"+stop);
                if(stop is "tablet" or "messages") Check(pet.Behavior.Tablet.Active,"手动平板在实际返回后接管唯一状态机 "+stop);
                if(stop is not ("restore" or "hideObject")) Check(Layouts()==before,"中断不改物件布置 "+phase+"/"+stop);
            }
            foreach(string phase in new[]{"ascend","summitSettle","look","descend","footSettle"}) foreach(string stop in new[]{"pause","hide","lock","suspend","fullscreen"}) {
                Begin(phase); double time=pet.Behavior.Journey.Elapsed,x=pet.Behavior.X,y=pet.Behavior.Y; int generation=pet.Behavior.Journey.Generation;
                switch(stop) {
                    case "pause": pet.TogglePause(); break;
                    case "hide": pet.ToggleHidden(); break;
                    case "lock": Field("sessionLocked",true); Visibility(); break;
                    case "suspend": Field("suspended",true); Visibility(); break;
                    case "fullscreen": Field("fullscreen",true); Visibility(); break;
                }
                Check(pet.Behavior.Journey.Active && pet.Behavior.Journey.Elapsed==time && pet.Behavior.X==x && pet.Behavior.Y==y,"隐藏或暂停保留冻结中的时间轴 "+phase+"/"+stop);
                Check(!pet.Explore("mountainClimb") && !pet.OpenTablet(),"阻断状态不能创建新会话 "+stop);
                switch(stop) {
                    case "pause": pet.TogglePause(); break;
                    case "hide": pet.ToggleHidden(); break;
                    case "lock": Field("sessionLocked",false); Visibility(); break;
                    case "suspend": Field("suspended",false); Visibility(); break;
                    case "fullscreen": Field("fullscreen",false); Visibility(); break;
                }
                Check(pet.Behavior.Journey.Generation==generation && pet.Behavior.Journey.Elapsed==time && !pet.Behavior.Tablet.Active,"恢复沿用原阶段，不补播或自动打开平板 "+stop);
            }
            Reset(); Check(pet.Explore("mountainClimb"),"登山可再次打开"); Until("look"); int offers=save.ChoiceCounts.Values.Sum();
            pet.OfferChoice(true); Check(pet.Behavior.Journey.Active && !pet.Behavior.Tablet.Active && offers==save.ChoiceCounts.Values.Sum(),"主动邀请不会打断山顶观察或消耗邀请次数");
            pet.EndExploration();FinishJourney("look","end");
            foreach(string scene in new[]{"cloud","indoor","outdoor","meadow","swingTree","mountain","polar"}) {
                Reset(scene);
                foreach(string id in new[]{"mountainClimb","auroraWatch","snowTouch"}) Check(pet.CanExplore(id)==(scene=="mountain" ? id=="mountainClimb" : scene=="polar" && id!="mountainClimb"),"生产动作入口按场景开放 "+scene+"/"+id);
            }
            Reset("polar"); foreach(string id in new[]{"auroraWatch","snowTouch"}) { Check(pet.Explore(id),"极地生产入口 "+id); Until(id=="auroraWatch" ? "watch" : "snow"); pet.EndExploration();FinishJourney(id,"end"); }
            Reset(); var settings=new SettingsWindow(pet);
            static bool HasText(DependencyObject root,string text) {
                if(root is TextBlock block && block.Text.Contains(text)) return true;
                foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) if(HasText(child,text)) return true;
                return false;
            }
            string productVersion="v"+typeof(PetWindow).Assembly.GetName().Version!.ToString(3);
            Check(HasText(settings,productVersion) && HasText(settings,"正式维护版") && HasText(settings,"持续更新") && !HasText(settings,"公测预览版"),"设置页使用实际程序版本、正式维护版与持续更新说明"); settings.Close();
            Check(save.Days==20 && save.Version==1 && !save.GrowthStory.FatherLetterUnlocked,"场景入口与中断不改变成长、存档版本或故事门槛");
            Console.WriteLine("All "+checks+" production outdoor window checks passed. No native mouse, DPI, or hour-long stability result is implied. Isolated save: "+data);
        }
        finally { pet.Close(); }
    }
}
