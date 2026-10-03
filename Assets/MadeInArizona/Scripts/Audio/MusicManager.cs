using System.Collections.Generic;
using UnityEngine;

namespace MadeInArizona
{
    /// <summary>Streamed soundtrack with shuffled driving tracks and two-source crossfades.</summary>
    public sealed class MusicManager : MonoBehaviour
    {
        enum Context { Menu, Garage, Combat, GameOver }
        const float FadeSeconds = 3f;
        readonly Playlist[] playlists = { new Playlist(), new Playlist(), new Playlist(), new Playlist() };
        readonly AudioSource[] sources = new AudioSource[2];
        Context context;
        int current;
        float fade = 1f, startedAt;
        public float Duck { get; set; } = 1;
        public string CurrentTrack => sources[current] && sources[current].clip ? sources[current].clip.name : "";

        void Awake()
        {
            foreach (var clip in Resources.LoadAll<AudioClip>("Audio/Music"))
            {
                if (!clip || clip.length <= 0) continue;
                var group = clip.name == "Arizona Nation" ? Context.Menu : clip.name == "Arizonaland" ? Context.Garage :
                    clip.name == "Arizona Highlands" || clip.name == "Arizona Lowlands" ? Context.GameOver : Context.Combat;
                // Streaming keeps decoded memory small; preload makes the next source audible as soon as its fade starts.
                if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
                playlists[(int)group].Add(clip);
            }
            for (int i = 0; i < sources.Length; i++)
            {
                sources[i] = gameObject.AddComponent<AudioSource>();
                sources[i].playOnAwake = false;
                sources[i].spatialBlend = 0;
                sources[i].volume = 0;
                // Combat can exhaust the 32 real voices. Never let gunshots virtualize the soundtrack.
                sources[i].priority = 0;
            }
            foreach (var playlist in playlists)
                if (playlist.Count == 0) Debug.LogWarning("MIA_MUSIC: a soundtrack playlist is missing imported tracks.");
            Begin(Context.Menu);
        }

        bool fighting;
        public void SetCombat(bool enabled) { fighting = enabled; }

        void Update()
        {
            var game = GameManager.Instance;
            if (!game || game.Save == null) return;
            var desired = game.Dying || game.State == GameState.Lost ? Context.GameOver :
                game.State == GameState.MainMenu || game.State == GameState.Generating ? Context.Menu :
                fighting && (game.State == GameState.Playing || game.State == GameState.Paused) ? Context.Combat : Context.Garage;
            // A death or menu transition should not wait for the previous fade to finish.
            if (desired != context) Begin(desired);
            else fade = Mathf.MoveTowards(fade, 1f, Time.unscaledDeltaTime / FadeSeconds);
            if (fade >= 1f)
            {
                var outgoing = sources[1 - current];
                if (outgoing.clip) { outgoing.Stop(); outgoing.clip = null; }
                var playing = sources[current];
                if (playing.clip && !playing.loop &&
                    (playing.time >= playing.clip.length - FadeSeconds ||
                    (!playing.isPlaying && Time.unscaledTime - startedAt > 1f && playing.clip.loadState != AudioDataLoadState.Loading)))
                    Begin(context);
            }
            float gain = game.Save.settings.music * .62f * Mathf.Clamp01(Duck) * (game.State == GameState.Paused ? .5f : 1f);
            float incoming = Mathf.SmoothStep(0, 1, fade);
            sources[current].volume = incoming * gain;
            sources[1 - current].volume = (1 - incoming) * gain;
        }

        void Begin(Context nextContext)
        {
            var playlist = playlists[(int)nextContext];
            var clip = playlist.Next();
            if (!clip)
            {
                // An unavailable death playlist must not leave combat music running over the death screen.
                if (nextContext == Context.GameOver)
                {
                    foreach (var source in sources) { source.Stop(); source.clip = null; }
                    context = nextContext;
                    fade = 1f;
                }
                return;
            }
            // Unity can release an inactive streamed clip after a scene transition.
            // Restore its playback buffer before starting the incoming fade.
            if (clip.loadState != AudioDataLoadState.Loaded && !clip.LoadAudioData())
            {
                Debug.LogWarning("MIA_MUSIC: could not load " + clip.name);
                return;
            }
            bool hadTrack = sources[current].clip;
            int next = 1 - current;
            sources[next].Stop();
            sources[next].clip = clip;
            sources[next].loop = playlist.Count == 1;
            sources[next].volume = 0;
            sources[next].Play();
            current = next;
            context = nextContext;
            fade = hadTrack ? 0f : 1f;
            startedAt = Time.unscaledTime;
        }

        // Each driving track plays once per shuffle, with no repeated track at the bag boundary.
        sealed class Playlist
        {
            readonly List<AudioClip> clips = new List<AudioClip>();
            readonly System.Random random = new System.Random();
            int[] order;
            int cursor;
            AudioClip last;
            public int Count => clips.Count;
            public void Add(AudioClip clip) { clips.Add(clip); }
            public AudioClip Next()
            {
                if (clips.Count == 0) return null;
                if (order == null || cursor >= order.Length)
                {
                    order = new int[clips.Count];
                    for (int i = 0; i < order.Length; i++) order[i] = i;
                    for (int i = order.Length - 1; i > 0; i--)
                    {
                        int other = random.Next(i + 1);
                        (order[i], order[other]) = (order[other], order[i]);
                    }
                    if (order.Length > 1 && clips[order[0]] == last)
                        (order[0], order[1]) = (order[1], order[0]);
                    cursor = 0;
                }
                last = clips[order[cursor++]];
                return last;
            }
        }
        // Clips are imported assets owned by Unity; never destroy them as synthesized PCM.
        void OnDestroy() { foreach (var source in sources) if (source) source.Stop(); }
    }
}
