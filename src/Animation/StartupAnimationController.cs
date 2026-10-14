namespace LittleShelter;

public enum StartupPlayback { None, Full, Brief }

// Presentation time only. Never calls World.Create, Behavior or story events.
public sealed class StartupAnimationController
{
    public const double FullSeconds=14.4,BriefSeconds=3.1;
    public StartupPlayback Playback { get; private set; }
    public double Elapsed { get; private set; }
    public bool Active { get; private set; }
    public double Duration => Playback == StartupPlayback.Full ? FullSeconds : BriefSeconds;
    public event Action? Finished;
    public string Stage => !Active ? "complete" : Playback == StartupPlayback.Brief ? "welcome" : Elapsed switch { <2=>"starlight",<5=>"world",<8.6=>"awakening",<11.4=>"creation",_=>"home" };
    public static StartupPlayback Choose(SaveData save,bool diagnostic=false,string? requested=null)
    {
        if (requested is "full" or "brief" or "skip") return requested == "full" ? StartupPlayback.Full : requested == "brief" ? StartupPlayback.Brief : StartupPlayback.None;
        if (requested=="auto") diagnostic=false;
        if (diagnostic || !save.Settings.StartupAnimationEnabled || save.Settings.StartupAnimationPlayback == "off") return StartupPlayback.None;
        return save.Settings.StartupAnimationPlayback == "always" || !save.HasSeenStartup ? StartupPlayback.Full : StartupPlayback.Brief;
    }
    public bool Begin(StartupPlayback playback)
    {
        if (Active || playback == StartupPlayback.None) return false;
        Playback=playback; Elapsed=0; Active=true; return true;
    }
    public void Advance(double seconds)
    {
        if (!Active || !double.IsFinite(seconds) || seconds<=0) return;
        Elapsed=Math.Min(Duration,Elapsed+seconds);
        if (Elapsed>=Duration) Stop();
    }
    public void Stop()
    {
        if (!Active) return;
        Active=false; Finished?.Invoke();
    }
    public static double Ease(double time,double start,double end)
    {
        double p=Math.Clamp((time-start)/(end-start),0,1); return p*p*(3-2*p);
    }
}
