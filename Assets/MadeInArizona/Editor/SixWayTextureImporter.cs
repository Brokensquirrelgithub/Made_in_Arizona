using UnityEditor;
using UnityEngine;
namespace MadeInArizona.Editor
{
    public sealed class SixWayTextureImporter : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if(!assetPath.Contains("/Resources/SixWay/"))return;
            var texture=(TextureImporter)assetImporter;
            texture.sRGBTexture=false;texture.alphaSource=TextureImporterAlphaSource.FromInput;
            texture.alphaIsTransparency=false;texture.mipmapEnabled=true;
            texture.wrapMode=TextureWrapMode.Clamp;texture.filterMode=FilterMode.Bilinear;
            texture.maxTextureSize=4096;texture.textureCompression=TextureImporterCompression.CompressedHQ;
            texture.isReadable=false;
        }
    }
}
