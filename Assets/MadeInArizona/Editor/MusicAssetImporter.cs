#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace MadeInArizona.Editor
{
    /// <summary>Stream long recordings while preloading their small playback buffers.</summary>
    public sealed class MusicAssetImporter : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith("Assets/MadeInArizona/Resources/Audio/Music/", System.StringComparison.Ordinal)) return;
            var importer = (AudioImporter)assetImporter;
            importer.forceToMono = false;
            importer.loadInBackground = false;
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.Streaming;
            settings.preloadAudioData = true;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = .85f;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = settings;
        }
    }
}
#endif
