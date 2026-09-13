using System.Collections.Generic;
using UnityEngine;
namespace MadeInArizona
{
    /// <summary>Pooled original effects, layered engine synthesis and explosion ducking.</summary>
    public sealed class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }
        readonly List<AudioClip> clips=new List<AudioClip>();
        AudioClip[] shots,blasts;AudioClip ui,radio,shift,release;
        AudioSource exhaust,engine,whine,road,wind,radioSource,uiSource;
        AudioSource[] pool;int cursor,previousGear;float duck=1,lastThrottle,blowoffAt;
        MusicManager music;
        void Awake()
        {
            Instance=this;
            exhaust=Loop(AudioSynthesis.Exhaust(),70);shift=Keep(AudioSynthesis.Mechanical(false));release=Keep(AudioSynthesis.Mechanical(true));
            engine=Loop(AudioSynthesis.Engine(),80);whine=Loop(AudioSynthesis.Turbo(),90);road=Loop(AudioSynthesis.Wind(),140);wind=Loop(AudioSynthesis.Wind(),160);
            shots=new AudioClip[3];blasts=new AudioClip[3];for(int i=0;i<3;i++){shots[i]=Keep(AudioSynthesis.Shot(i));blasts[i]=Keep(AudioSynthesis.Explosion(i));}
            ui=Keep(AudioSynthesis.Chirp(false));radio=Keep(AudioSynthesis.Chirp(true));
            uiSource=gameObject.AddComponent<AudioSource>();radioSource=gameObject.AddComponent<AudioSource>();uiSource.spatialBlend=0;radioSource.spatialBlend=0;
            pool=new AudioSource[28];
            for(int i=0;i<pool.Length;i++){var child=new GameObject("Pooled effect "+i);child.transform.SetParent(transform);pool[i]=child.AddComponent<AudioSource>();pool[i].playOnAwake=false;pool[i].spatialBlend=.5f;pool[i].minDistance=16;pool[i].maxDistance=160;pool[i].rolloffMode=AudioRolloffMode.Linear;pool[i].dopplerLevel=0;}
            music=gameObject.AddComponent<MusicManager>();
        }
        AudioClip Keep(AudioClip clip){clips.Add(clip);return clip;}
        AudioSource Loop(AudioClip clip,int priority)
        {var source=gameObject.AddComponent<AudioSource>();source.clip=Keep(clip);source.spatialBlend=0;source.priority=priority;source.loop=true;source.volume=0;source.Play();return source;}
        GameSettings Settings { get { return GameManager.Instance?.Save?.settings; } }
        void Update()
        {
            var game=GameManager.Instance;if(game==null||Settings==null)return;
            duck=Mathf.MoveTowards(duck,1,Time.unscaledDeltaTime*1.5f);if(music)music.Duck=duck;
            bool active=game.State==GameState.Playing;var player=game.Player;
            float gain=Settings.engines*duck;
            if(player&&player.Damage!=null)
            {
                float rpm=Mathf.Clamp01((player.RPM-850)/6200),load=player.Throttle;
                engine.pitch=.5f+rpm*2.2f;engine.volume=(player.Damage.IsDead?0:active?.09f+load*.20f:.035f)*gain;
                exhaust.pitch=Mathf.Lerp(exhaust.pitch,.55f+rpm*2.5f,Time.unscaledDeltaTime*14);
                exhaust.volume=active&&!player.Damage.IsDead?(.07f+load*.22f+rpm*.07f)*gain:0;
                if(active&&player.Gear!=previousGear){previousGear=player.Gear;PlayAt(shift,player.transform.position,.24f*gain,Random.Range(.9f,1.1f),70);}
                float boost=InputManager.Instance!=null&&InputManager.Instance.Boost?1:0;
                whine.pitch=.55f+rpm*1.6f;whine.volume=active?gain*(load*rpm*.027f+boost*.027f):0;
                road.pitch=.6f+player.SpeedKph/80;
                float rough=WorldBuilder.SurfaceAt(player.transform.position)==SurfaceKind.Asphalt?.12f:.36f;
                road.volume=active?Settings.environment*(Mathf.Clamp01(player.SpeedKph/80)*rough+player.DriftAmount*.13f)*duck:0;
                if(active&&lastThrottle>.7f&&load<.3f&&Time.time>blowoffAt){blowoffAt=Time.time+.5f;PlayAt(release,player.transform.position,.18f*gain,1+rpm*.3f,75);}
                lastThrottle=load;
            }
            else{exhaust.volume=0;engine.volume=0;whine.volume=0;road.volume=0;}
            wind.volume=Settings.environment*(active?.08f:.035f)*duck;
            AudioListener.volume=Settings.master;
        }
        void PlayAt(AudioClip clip,Vector3 position,float volume,float pitch,int priority)
        {
            if(pool==null)return;AudioSource source=pool[cursor++%pool.Length];source.Stop();source.transform.position=position;source.clip=clip;source.volume=Mathf.Clamp01(volume);source.pitch=pitch;source.priority=priority;source.Play();
        }
        public void PlayShot(Vector3 position,int weapon)
        {if(Settings==null)return;int kind=Mathf.Clamp(weapon,0,2);PlayAt(shots[kind],position,Settings.weapons*(kind==0?.21f:.45f)*duck,Random.Range(.93f,1.06f),kind==0?85:60);}
        public void PlayExplosion(Vector3 position,float strength)
        {
            if(Settings==null)return;int size=strength>=10?2:strength>=5?1:0;
            PlayAt(blasts[size],position,Settings.weapons*Mathf.Clamp(.3f+strength*.055f,.35f,.93f),Random.Range(.85f,1.04f),25);
            if(size>0)duck=Mathf.Min(duck,size==2?.3f:.55f);
        }
        public void PlayUI(){if(Settings==null||!uiSource)return;uiSource.PlayOneShot(ui,Settings.environment*.5f);}
        public void PlayRadio(){if(Settings==null||!radioSource)return;radioSource.PlayOneShot(radio,Settings.dialogue*.32f);}
        public void SetCombat(bool enabled){if(music)music.SetCombat(enabled);}
        void OnDestroy(){foreach(var clip in clips)if(clip)Destroy(clip);if(Instance==this)Instance=null;}
    }
}
