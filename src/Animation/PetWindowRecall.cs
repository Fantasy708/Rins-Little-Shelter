using System.Windows;
namespace LittleShelter;

public sealed partial class PetWindow
{
    public CreationRecall Recall { get; }=new();
    public CreationRecallCanvas RecallCanvas { get; private set; }=null!;
    private double lastRecallCapture;
    private void InstallRecallPresentation()
    {
        RecallCanvas=Scene.CreateRecallCanvas(Recall); RecallCanvas.Visibility=Visibility.Collapsed;
        RecallCanvas.EndRequested+=EndRecall; Recall.Finished+=FinishRecall;
        viewport.Children.Add(RecallCanvas);
    }
    private void ShelterPlaybackReady()
    {
        // Never queue a surprise recollection behind another interaction.
        // The pre-memory ideal-world behavior remains available as before.
        if(Scenes.MemoriesUnlocked(Save)) BeginRecall();
        else if(!paused && !manualHidden && !fullscreen && !locked && !Opening.Active && !Scene.Editing && !Save.Settings.ClickThrough && IsVisible) ShowIdeal();
    }
    public void ShowRecall() => BeginRecall();
    public bool CanRecall => !Recall.Active && Scenes.MemoriesUnlocked(Save) && !Opening.Active && !paused && !manualHidden && !fullscreen && !locked && !closed && IsLoaded && IsVisible && !Scene.Editing && !Save.Settings.ClickThrough && !Behavior.Dragging && !dragging && !Behavior.Bridge.Active && !Behavior.MotionExit.Active && !Behavior.Journey.Active && !Behavior.Nurture.Active && !Behavior.Tablet.Active && !Behavior.Life.Active && Behavior.Current.Kind is not ("create" or "move");
    public bool BeginRecall()
    {
        if(!CanRecall) return false;
        if(Native.Fullscreen(this)) return false;
        if(!Recall.Begin(Save)) return false;
        Scene.ClearCreationEffects(); Scene.ClearTabletHits(); Scene.BubbleUntil=Scene.IdealUntil=0;
        RecallCanvas.Reset(); Scene.SetStartupPresentation(true); Scene.Visibility=Visibility.Hidden; RecallCanvas.Visibility=Visibility.Visible;
        lastFrame=watch.Elapsed.TotalSeconds; lastOffer=lastReminder=runtime;
        CompanionChanged?.Invoke(); return true;
    }
    public void EndRecall() => Recall.Stop();
    private void FinishRecall()
    {
        if(RecallCanvas.DisplayedActor is { } actor) Scene.ContinueActorFrom(actor,RecallCanvas.DisplayedActorRoot);
        RecallCanvas.Visibility=Visibility.Collapsed; RecallCanvas.Reset(); Scene.SetStartupPresentation(false); Scene.Visibility=Visibility.Visible;
        lastFrame=watch.Elapsed.TotalSeconds; lastOffer=lastReminder=runtime;
        if(IsLoaded && !closed) { pointerThrough=Save.Settings.ClickThrough; Native.Configure(this,pointerThrough); }
        CompanionChanged?.Invoke();
    }
    public void AdvanceRecall(double seconds)
    {
        if(!Recall.Active) return; Recall.Advance(seconds); RecallCanvas.InvalidateVisual();
        if(!Recall.Active) return;
        if(Native.GetCursorPos(out var cursor)) {
            var point=RecallCanvas.PointFromScreen(new Point(cursor.X,cursor.Y));
            bool through=Save.Settings.ClickThrough || RecallCanvas.Hit(point)==null;
            if(through!=pointerThrough) { pointerThrough=through; Native.Configure(this,through); }
        }
        if(captureActionFrames && captureDirectory!=null && runtime-lastRecallCapture>=.2) {
            lastRecallCapture=runtime; Capture("recall-"+frames.ToString("D5")+".png");
        }
    }
}
