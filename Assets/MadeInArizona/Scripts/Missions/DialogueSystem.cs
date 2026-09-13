using UnityEngine;
namespace MadeInArizona
{
    /// <summary>Brief interruptible radio subtitles. Character portrait is drawn by GameUI.</summary>
    public sealed class DialogueSystem : MonoBehaviour
    {
        public static DialogueSystem Instance { get; private set; }
        public string Speaker { get; private set; } = "";
        public string Text { get; private set; } = "";
        public bool Active { get { return remaining>0&&!string.IsNullOrEmpty(Text); } }
        float remaining;
        void Awake() { Instance=this; }
        public void Say(string speaker,string text,float duration=7)
        { Speaker=speaker; Text=text; remaining=Mathf.Clamp(duration,1,24); AudioManager.Instance?.PlayRadio(); }
        public void SayLine(string line,float duration=9)
        {
            int split=line.IndexOf(':');
            if(split>0&&split<26)Say(line.Substring(0,split).ToUpperInvariant(),line.Substring(split+1).Trim(),duration);
            else Say("117° AUTO CARE",line,duration);
        }
        public void Skip() { remaining=0;Text=""; }
        void Update() { if(GameManager.Instance!=null&&GameManager.Instance.State==GameState.Paused)return; remaining=Mathf.Max(0,remaining-Time.unscaledDeltaTime); }
        void OnDestroy() { if(Instance==this)Instance=null; }
    }
}
