using System.Collections.Generic;
using UnityEngine;
namespace MadeInArizona
{
    /// <summary>Brief interruptible radio subtitles with pipe-delimited exchanges.</summary>
    public sealed class DialogueSystem : MonoBehaviour
    {
        struct QueuedLine { public string speaker, text; public float duration; }
        public static DialogueSystem Instance { get; private set; }
        public string Speaker { get; private set; } = "";
        public string Text { get; private set; } = "";
        public bool Active { get { return remaining>0&&!string.IsNullOrEmpty(Text); } }
        readonly Queue<QueuedLine> queue = new Queue<QueuedLine>();
        float remaining;
        void Awake() { Instance=this; }
        public void Say(string speaker,string text,float duration=7)
        { queue.Clear(); Show(speaker,text,duration); }
        public void SayLine(string line,float duration=9)
        {
            queue.Clear();
            string[] lines=(line??"").Split('|');
            float each=Mathf.Clamp(duration/Mathf.Max(1,lines.Length),3.5f,24);
            foreach(string raw in lines)
            {
                string part=raw.Trim(); if(string.IsNullOrEmpty(part))continue;
                int split=part.IndexOf(':');
                queue.Enqueue(split>0&&split<26
                    ? new QueuedLine{speaker=part.Substring(0,split).ToUpperInvariant(),text=part.Substring(split+1).Trim(),duration=each}
                    : new QueuedLine{speaker="117° AUTO CARE",text=part,duration=each});
            }
            Advance();
        }
        void Show(string speaker,string text,float duration)
        { Speaker=speaker; Text=text; remaining=Mathf.Clamp(duration,1,24); AudioManager.Instance?.PlayRadio(); }
        void Advance()
        {
            if(queue.Count>0){var next=queue.Dequeue();Show(next.speaker,next.text,next.duration);}
            else{remaining=0;Text="";Speaker="";}
        }
        public void Skip() { queue.Clear();remaining=0;Text="";Speaker=""; }
        void Update()
        {
            if(GameManager.Instance!=null&&GameManager.Instance.State==GameState.Paused)return;
            if(remaining<=0)return;
            remaining=Mathf.Max(0,remaining-Time.unscaledDeltaTime);
            if(remaining<=0)Advance();
        }
        void OnDestroy() { if(Instance==this)Instance=null; }
    }
}
