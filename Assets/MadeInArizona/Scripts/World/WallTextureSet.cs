using UnityEngine;

namespace MadeInArizona
{
    /// <summary>Build-safe references to the licensed 18 High Resolution Wall Textures package.</summary>
    public sealed class WallTextureSet : ScriptableObject
    {
        public const int TextureCount = 18;
        public Texture2D[] diffuse = new Texture2D[TextureCount];
        public Texture2D[] normal = new Texture2D[TextureCount];
        public Texture2D[] occlusion = new Texture2D[TextureCount];
        public Texture2D[] metallic = new Texture2D[TextureCount];

        static WallTextureSet cached;
        public static WallTextureSet Load()
        {
            if (!cached) cached = Resources.Load<WallTextureSet>("WallTextureSet");
            return cached;
        }

        public Texture2D Diffuse(int index) => At(diffuse, index);
        public Texture2D Normal(int index) => At(normal, index);
        public Texture2D Occlusion(int index) => At(occlusion, index);
        public Texture2D Metallic(int index) => At(metallic, index);

        static Texture2D At(Texture2D[] textures, int index)
        {
            if (textures == null || textures.Length == 0) return null;
            index = Mathf.Abs(index) % textures.Length;
            return textures[index];
        }
    }
}
