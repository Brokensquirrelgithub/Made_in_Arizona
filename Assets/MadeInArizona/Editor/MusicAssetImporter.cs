#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace MadeInArizona.Editor
{
    /// <summary>Keep long soundtrack recordings streamed instead of decoding the album into RAM.</summary>
    public sealed class MusicAssetImporter : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith("Assets/MadeInArizona/Resources/Audio/Music/", System.StringComparison.Ordinal)) return;
            var importer = (AudioImporter)assetImporter;
            importer.forceToMono = false;
            importer.loadInBackground = true;
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.Streaming;
            settings.preloadAudioData = false;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = .85f;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = settings;
        }
    }
}
#endif
