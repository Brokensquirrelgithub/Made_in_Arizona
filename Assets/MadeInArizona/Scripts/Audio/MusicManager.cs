using UnityEngine;
namespace MadeInArizona
{
    public sealed class MusicManager : MonoBehaviour
    {
        AudioSource garage,combat;bool fighting;float blend;
        public float Duck { get; set; } = 1;
        void Awake()
        {
            garage=gameObject.AddComponent<AudioSource>();combat=gameObject.AddComponent<AudioSource>();
            garage.clip=AudioSynthesis.Music(false);combat.clip=AudioSynthesis.Music(true);
            foreach(var source in new[]{garage,combat}){source.loop=true;source.spatialBlend=0;source.volume=0;source.priority=128;source.Play();}
        }
        public void SetCombat(bool enabled){fighting=enabled;}
        void Update()
        {
            var game=GameManager.Instance;if(game==null||game.Save==null)return;
            blend=Mathf.MoveTowards(blend,fighting?1:0,Time.unscaledDeltaTime*.65f);
            float volume=game.Save.settings.music*.62f*Duck*(game.State==GameState.Paused?.5f:1);
            garage.volume=(1-blend)*volume;combat.volume=blend*volume;
        }
        void OnDestroy(){if(garage&&garage.clip)Destroy(garage.clip);if(combat&&combat.clip)Destroy(combat.clip);}
    }
}
