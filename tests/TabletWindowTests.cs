using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LittleShelter;

internal static class TabletWindowTests
{
    [STAThread] private static void Main(string[] args)
    {
        string root=Path.GetFullPath(args.Length>0 ? args[0] : "output/tablet-window-qa");
        Directory.CreateDirectory(root); string data=Path.Combine(root,"save-"+Guid.NewGuid().ToString("N"));
        var storage=new Storage(data); var save=new SaveData(); save.Settings.Music=false; save.Settings.FreeMode=false;
        save.CompanionDates=Enumerable.Range(0,20).Select(i=>new DateTime(2026,1,1).AddDays(i).ToString("yyyy-MM-dd")).ToList();
        World.Initialize(save); Scenes.Switch(save,"outdoor");
        var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
        var pet=new PetWindow(storage,save,true); app.MainWindow=pet; int checks=0; double clock=0;
        void Check(bool value,string name) { if (!value) throw new Exception("FAIL: "+name); checks++; Console.WriteLine("PASS: "+name); }
        void Step(double seconds) { while(seconds>1e-7) { double dt=Math.Min(.01,seconds); pet.Behavior.Tick(dt,save,false); clock+=dt; seconds-=dt; } }
        void Until(string phase) { for(int i=0;i<16000 && pet.Behavior.Tablet.Phase!=phase;i++) Step(.01); Check(pet.Behavior.Tablet.Phase==phase,"控制器到达 "+phase); }
        void Render(string? name=null)
        {
            pet.Scene.Time=clock; pet.Scene.InvalidateVisual(); pet.Scene.UpdateLayout();
            var bitmap=new RenderTargetBitmap(480,340,96,96,PixelFormats.Pbgra32); bitmap.Render(pet.Scene);
            if (name!=null) { using var f=File.Create(Path.Combine(root,name+".png")); var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); png.Save(f); }
        }
        Point Center(Rect r) => new(r.X+r.Width/2,r.Y+r.Height/2);
        void Reset()
        {
            if (pet.Paused) pet.TogglePause(); if (!pet.IsVisible) pet.ToggleHidden();
            if (pet.Scene.Editing) pet.ToggleEditing();
            save.Settings.ClickThrough=false; pet.ApplySettings();
            pet.Behavior.Life.Cancel(); if (save.Settings.FreeMode) pet.SwitchMode();
            pet.SwitchScene("outdoor"); pet.Behavior.X=365; pet.Behavior.Y=Scenes.Home(save).Y; pet.Behavior.Start("stand");
            pet.Scene.Measure(new Size(480,340)); pet.Scene.Arrange(new Rect(0,0,480,340)); Render(); Step(.3); Render();
        }
        void OpenAt(string phase)
        {
            Reset(); Check(pet.OpenTablet("create","flower"),"生产入口打开平板");
            if (phase is "closing" or "tap" or "observe") { Until("ready"); Render(); pet.HandleTabletInput("confirm"); }
            Until(phase); Render();
        }
        string Layouts() => JsonSerializer.Serialize(new { save.GrowthLayout,save.FreeLayout,save.GrowthSceneLayouts,save.FreeSceneLayouts,save.Created });
        try
        {
            pet.Show(); Check(pet.IsVisible && !pet.ShowActivated,"窗口可见且保持不激活");
            Check(pet.Icon is BitmapSource iconImage && iconImage.PixelWidth>=16,"生产窗口加载内嵌应用图标");
            using (var icon = AppIcon.CreateTrayIcon()) Check(icon.Width==32 && icon.Height==32,"托盘从同一内嵌资源读取32像素图标");
            var iconSettings = new SettingsWindow(pet);
            Check(ReferenceEquals(iconSettings.Icon,pet.Icon),"设置与陪伴窗口共用应用图标");
            iconSettings.Close();
            foreach(string phase in new[]{"opening","ready","tap"}) foreach(string stop in new[]{"pause","hidden","editing","scene","mode","memory","through","restore","hideObject"}) {
                OpenAt(phase); var buttons=pet.Scene.TabletButtons.ToArray(); int events=0; void Count(string id) => events++; pet.Behavior.ObjectCreated+=Count;
                if (stop=="pause") pet.TogglePause();
                if (stop=="hidden") pet.ToggleHidden();
                if (stop=="editing") pet.ToggleEditing();
                if (stop=="scene") pet.SwitchScene("indoor");
                if (stop=="mode") pet.SwitchMode();
                if (stop=="memory") { pet.SwitchScene("indoor"); pet.ViewMemory(); }
                if (stop=="through") { save.Settings.ClickThrough=true; pet.ApplySettings(); }
                if (stop=="restore") pet.RestoreLayout();
                if (stop=="hideObject") pet.HideObject("flower");
                Check(!pet.Behavior.Tablet.Active && !pet.Behavior.Tablet.Interactive && pet.Scene.TabletButtons.Count==0,"生产操作即时关闭平板 "+phase+"/"+stop);
                foreach(var button in buttons) Check(pet.Scene.Hit(Center(button.Value))!="tablet:"+button.Key,"生产操作清除旧按钮命中 "+stop);
                if (stop=="pause") { Check(!pet.OpenTablet(),"暂停时不允许重新打开"); pet.TogglePause(); }
                if (stop=="hidden") { Check(!pet.OpenTablet(),"隐藏时不允许重新打开"); pet.ToggleHidden(); }
                if (stop=="editing") { Check(!pet.OpenTablet(),"整理时不允许重新打开"); pet.ToggleEditing(); }
                if (stop=="through") { Check(!pet.OpenTablet(),"完全穿透时不允许重新打开"); save.Settings.ClickThrough=false; pet.ApplySettings(); }
                Step(.3); Render(); Check(!pet.Behavior.Tablet.Active && events==0,"恢复不重开、不补发取消的生成 "+phase+"/"+stop);
                pet.Behavior.ObjectCreated-=Count;
            }
            foreach(string phase in new[]{"opening","ready","tap"}) {
                Reset(); pet.SwitchScene("indoor"); pet.Behavior.X=365;
                Check(pet.OpenTablet("create","desk"),"室内直接打开平板");
                if (phase=="tap") { Until("ready"); Render(); pet.HandleTabletInput("confirm"); }
                Until(phase); Render(); var buttons=pet.Scene.TabletButtons.ToArray();
                pet.ViewMemory();
                Check(pet.Behavior.Tablet.Active && pet.Behavior.Tablet.Recovering && !pet.Behavior.Tablet.Interactive && !pet.Behavior.Life.Active,"直接记忆入口先收起室内平板，旧输入立即失效 "+phase);
                foreach(var button in buttons) Check(pet.Scene.Hit(Center(button.Value))!="tablet:"+button.Key,"直接记忆入口清除旧命中");
                for(int i=0;i<16000 && pet.Behavior.Tablet.Active;i++)Step(.01);
                Render();
                Check(!pet.Behavior.Tablet.Active && pet.Scene.TabletButtons.Count==0,"直接记忆入口完成实际收纳后关闭室内平板 "+phase);
                Check(pet.Behavior.Life.Active,"平板收纳完成才启动原记忆状态机 "+phase);
                pet.Behavior.Life.Cancel();
            }
            OpenAt("ready"); var confirm=pet.Scene.TabletButtons["confirm"]; pet.HandleTabletInput("confirm");
            Check(!pet.Behavior.Tablet.Interactive,"确认后立即锁定输入");
            // This exercises the gap before the next composition frame. The
            // visible opaque screen must absorb a second click, while stale
            // button actions are already gone.
            Check(pet.Scene.Hit(Center(confirm))=="tablet:surface","确认后下一绘图前保留可见平板遮挡，避免双击落到景物取消创建");
            pet.HandleTabletInput("confirm"); Until("observe");
            Check(save.GrowthLayout.Count(p=>p.Id=="flower")==1 && !save.GrowthLayout.First(p=>p.Id=="flower").Hidden,"生产ObjectCreated订阅在点触节点创建且仅一份");
            var persisted=storage.Load(); Check(persisted.GrowthLayout.Count(p=>p.Id=="flower")==1,"生成节点立即通过生产存档事件持久化");
            Reset(); pet.SwitchScene("indoor"); pet.ViewMemory();
            for(int i=0;i<16000 && pet.Behavior.Life.Phase!="watch";i++) Step(.01);
            Check(pet.Behavior.Life.Phase=="watch" && pet.Behavior.Life.PresentOpacity==1,"完整记忆到达现在Rin恢复节点");
            Check(save.GrowthStory.FatherLetterUnlocked && storage.Load().GrowthStory.CompletedMemories.Contains("indoor"),"生产MemoryCompleted事件立即保存成长来信");
            pet.EndMemory(); pet.Behavior.Life.Cancel();
            // The next fixture isolates free-mode reading from an unearned
            // growth letter, including the actual persistence event.
            save.GrowthStory=new StoryState();
            Reset(); pet.SwitchMode(); string before=Layouts(); var counts=JsonSerializer.Serialize(save.ChoiceCounts);
            pet.OpenMessages(); Until("ready"); Render(); Check(!save.FreeStory.FatherLetterRead,"生产消息列表不会标已读");
            pet.HandleTabletInput("letter"); Check(!save.FreeStory.FatherLetterRead,"打开正文但未呈现时仍未读"); Render("letter-window-render");
            Check(save.FreeStory.FatherLetterRead && storage.Load().FreeStory.FatherLetterRead,"正文呈现的生产StoryChanged事件立即保存已读");
            Check(Layouts()==before && JsonSerializer.Serialize(save.ChoiceCounts)==counts && !save.GrowthStory.FatherLetterUnlocked,"阅读不修改布局、次数或成长来信门槛");
            pet.HandleTabletInput("back"); Render(); Check(pet.Scene.TabletButtons.ContainsKey("letter") && !pet.Scene.TabletButtons.ContainsKey("back"),"生产返回入口移除正文命中");
            pet.HandleTabletInput("tab:create"); Render(); Check(!pet.Scene.TabletButtons.ContainsKey("letter") && pet.Scene.TabletButtons.Keys.Any(k=>k.StartsWith("select:")),"生产切页移除消息并恢复创造选项");
            pet.TogglePause(); pet.TogglePause(); Check(!pet.Behavior.Tablet.Active && storage.Load().FreeStory.FatherLetterRead,"暂停恢复保留已读且不自动重开");
            Check(save.Days==20 && save.Version==1,"集成测试保留成长与存档版本");
            Console.WriteLine("All "+checks+" production PetWindow wrapper checks passed. Isolated save: "+data);
        }
        finally { pet.Close(); }
    }
}
