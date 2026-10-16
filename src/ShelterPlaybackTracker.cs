namespace LittleShelter;

// Media snapshots, not audio packets, determine completion. Missing/paused
// sessions and seeking must not be counted as time spent hearing the song.
public sealed class ShelterPlaybackTracker
{
    private string key="";
    private double heard,lastPosition;
    private bool previousPlaying,havePosition,fired;
    public bool Observe(string identity,bool shelter,bool playing,double position,double duration,double elapsed)
    {
        if(!shelter) { Reset(); return false; }
        if(!double.IsFinite(position) || !double.IsFinite(duration) || !double.IsFinite(elapsed) || elapsed<0) { Disconnect(); return false; }
        if(identity!=key) { Reset(); key=identity; }
        double delta=position-lastPosition;
        if(havePosition && delta < -1) { heard=0; fired=false; }
        if(havePosition && playing && previousPlaying && delta>0 && delta<=Math.Min(elapsed,4)+1)
            heard+=Math.Min(delta,Math.Min(elapsed,3.5));
        else if(havePosition && delta>Math.Min(elapsed,4)+1) heard=0;
        lastPosition=position; previousPlaying=playing; havePosition=true;
        if(!fired && playing && duration>30 && heard>=duration*.95 && position>=duration-4) { fired=true; return true; }
        return false;
    }
    public void Disconnect() { havePosition=false; previousPlaying=false; }
    public void Reset() { key="";heard=0;fired=false;Disconnect(); }
}
