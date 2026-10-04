using UnityEngine;
namespace MadeInArizona
{
    /// <summary>
    /// Plays an EngineVoice through Unity's audio thread. The source loops a constant clip so Unity keeps calling
    /// OnAudioFilterRead; the filter replaces that signal with the rendered engine. Inputs are plain field writes.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class EngineVoiceSource : MonoBehaviour
    {
        volatile EngineVoice voice;
        int outputRate;
        public EngineVoice Voice => voice;
        public EngineLayout Layout => voice?.Layout;

        void Awake()
        {
            outputRate = AudioSettings.outputSampleRate;
            var source = GetComponent<AudioSource>();
            var carrier = AudioClip.Create("Physical engine carrier", 1024, 1, outputRate, false);
            var ones = new float[1024]; for (int i = 0; i < ones.Length; i++) ones[i] = 1;
            carrier.SetData(ones, 0);
            source.clip = carrier; source.loop = true; source.spatialBlend = 0; source.priority = 70;
            source.volume = 1; source.pitch = 1; source.dopplerLevel = 0; source.Play();
        }

        /// <summary>Switches engine or fits/removes the turbo; the new voice is built here and swapped in whole.</summary>
        public void Bind(EngineLayout layout, bool turbocharged)
        {
            if (layout == null || (voice?.Layout == layout && voice.Turbocharged == turbocharged)) return;
            voice = new EngineVoice(layout, outputRate, turbocharged);
        }

        public void Drive(float rpm, float throttle, float gain, DevTuning tuning)
        {
            var current = voice; if (current == null) return;
            current.TargetRpm = rpm; current.Throttle = throttle; current.Gain = gain;
            current.Variation = tuning.enginePulseVariation; current.Rasp = tuning.engineRasp; current.Body = tuning.engineBody;
            current.Drive = tuning.engineSaturation; current.LoadLevel = tuning.engineLoadLevel; current.OverrunLevel = tuning.engineOverrunLevel;
            current.TurboLevel = tuning.turboWhineLevel;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            var current = voice;
            if (current == null) { System.Array.Clear(data, 0, data.Length); return; }
            current.Render(data, channels);
        }
    }
}
