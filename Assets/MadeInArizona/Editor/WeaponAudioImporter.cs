#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace MadeInArizona.Editor
{
    public sealed class WeaponAudioImporter : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith("Assets/FreeWeaponSounds/", System.StringComparison.Ordinal)) return;
            var importer = (AudioImporter)assetImporter;
            importer.forceToMono = false;
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.preloadAudioData = true;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = .9f;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = settings;
        }
    }
}
#endif
