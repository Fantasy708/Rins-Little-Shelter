using System.Windows;
using System.Windows.Controls;
namespace LittleShelter;

public sealed partial class PetWindow
{
    private readonly Grid viewport=new() { Width=480,Height=340 };
    public StartupAnimationController Opening { get; }=new();
    public StartupAnimationCanvas OpeningCanvas { get; private set; }=null!;
    private bool captureOpeningFrames;
    private double lastOpeningCapture;
    private void InstallStartupPresentation()
    {
        OpeningCanvas=Scene.CreateOpeningCanvas(Opening); OpeningCanvas.Visibility=Visibility.Collapsed;
        OpeningCanvas.SkipRequested+=SkipStartup; Opening.Finished+=FinishStartup;
        viewport.Children.Add(Scene); viewport.Children.Add(OpeningCanvas); Content=viewport;
    }
    public bool BeginStartup(StartupPlayback playback)
    {
        if(Opening.Active || playback==StartupPlayback.None || !IsLoaded || !IsVisible || paused || manualHidden || locked || Save.Settings.ClickThrough || Behavior.Dragging) return false;
        if(fullscreen || Native.Fullscreen(this)) { fullscreen=true; UpdateVisibility(); return false; }
        Behavior.Nurture.Cancel(Save); CancelTablet(); Behavior.Life.Cancel(); Scene.Editing=false;
        Scenes.PlaceActor(Save,Behavior); Behavior.Start("stand"); Audio.Suspend(); clock.Reset();
        Scene.BubbleUntil=Scene.IdealUntil=0; OpeningCanvas.Reset(); OpeningCanvas.Destination=new Point(Behavior.X,Behavior.Y);
        Scene.SetStartupPresentation(true); OpeningCanvas.Visibility=Visibility.Visible;
        Opening.Begin(playback); lastFrame=watch.Elapsed.TotalSeconds;
        pointerThrough=false; Native.Configure(this,false); return true;
    }
    public void ReplayStartup() => BeginStartup(StartupPlayback.Full);
    public void SkipStartup() { Opening.Stop(); EndRecall(); }
    private void FinishStartup()
    {
        if(OpeningCanvas.DisplayedActor is { } actor) Scene.ContinueActorFrom(actor,OpeningCanvas.DisplayedActorRoot);
        OpeningCanvas.Visibility=Visibility.Collapsed; Scene.SetStartupPresentation(false); OpeningCanvas.Reset();
        Scenes.PlaceActor(Save,Behavior); Behavior.Welcome(); Save.HasSeenStartup=true;
        lastOffer=lastReminder=runtime; clock.Reset(); Persist();
        pointerThrough=Save.Settings.ClickThrough; Native.Configure(this,pointerThrough);
        if(!closed) { _=Audio.Poll(Save.Settings.Music && !paused && !locked, !manualHidden && !fullscreen && !Save.Settings.ClickThrough, resetPlaybackOnDisable:!Save.Settings.Music); CompanionChanged?.Invoke(); }
    }
    public void AdvanceStartup(double seconds)
    {
        if(!Opening.Active) return;
        Opening.Advance(seconds); Scene.InvalidateVisual(); OpeningCanvas.InvalidateVisual();
        if(!Opening.Active) return;
        if(Native.GetCursorPos(out var cursor)) {
            var point=OpeningCanvas.PointFromScreen(new Point(cursor.X,cursor.Y));
            bool through=Save.Settings.ClickThrough || OpeningCanvas.Hit(point)==null;
            if(through!=pointerThrough) { pointerThrough=through; Native.Configure(this,through); }
        }
        if(captureOpeningFrames && captureDirectory!=null && runtime-lastOpeningCapture>=.18) {
            lastOpeningCapture=runtime; Capture("startup-"+Opening.Stage+"-"+frames.ToString("D5")+".png");
        }
    }
    public void EnableStartupCapture() => captureOpeningFrames=true;
}
