namespace LittleShelter;

public enum ShelterPlaybackTrigger { None, RecognizedPlayback }

// Recognition uses the player's metadata and consecutive Playing snapshots.
// This is a ten-second playback condition, not proof of hearing the whole song.
public sealed class ShelterPlaybackTracker
{
    public const double RecognizedPlaybackSeconds = 10;
    private string key="";
    private double recognizedPlayback,lastPosition,lastDuration;
    private bool previousPlaying,haveTimelinePosition,fired;
    public ShelterPlaybackTrigger LastTrigger { get; private set; }
    public bool Observe(string identity,bool shelter,bool playing,double position,double duration,double elapsed)
    {
        LastTrigger=ShelterPlaybackTrigger.None;
        if(!shelter) { Reset(); return false; }
        if(identity!=key) { Reset(); key=identity; }
        if(!double.IsFinite(elapsed) || elapsed<=0 || elapsed>6) { Disconnect(); return false; }

        // Only a real, bounded timeline can identify a new round of the same
        // song. A missing/all-zero timeline must never look like a rewind.
        bool timeline=double.IsFinite(position) && double.IsFinite(duration) && duration>0 && position>=0 && position<=duration+1;
        bool rewind=timeline && haveTimelinePosition && Math.Abs(duration-lastDuration)<=1 && position-lastPosition < -1;
        if(rewind) { recognizedPlayback=0; previousPlaying=false; fired=false; }
        haveTimelinePosition=timeline;
        if(timeline) { lastPosition=position; lastDuration=duration; }

        if(!playing) { recognizedPlayback=0; previousPlaying=false; return false; }
        // The first snapshot is only an anchor: its elapsed interval predates
        // our observation of Playing and therefore cannot be credited.
        if(previousPlaying) recognizedPlayback=Math.Min(RecognizedPlaybackSeconds,recognizedPlayback+elapsed);
        previousPlaying=true;
        if(!fired && recognizedPlayback>=RecognizedPlaybackSeconds)
        {
            fired=true; LastTrigger=ShelterPlaybackTrigger.RecognizedPlayback; return true;
        }
        return false;
    }
    public void Disconnect()
    {
        recognizedPlayback=0; previousPlaying=false; haveTimelinePosition=false;
        LastTrigger=ShelterPlaybackTrigger.None;
    }
    public void Reset() { key="";fired=false;Disconnect(); }
}
