using UnityEngine;

namespace MadeInArizona
{
    /// <summary>Build-safe references to the licensed Outdoor Ground Textures package.</summary>
    public sealed class GroundTextureSet : ScriptableObject
    {
        public const int TextureCount = 14;
        public Texture2D[] diffuse = new Texture2D[TextureCount];
        public Texture2D[] normal = new Texture2D[TextureCount];
        public Texture2D[] occlusion = new Texture2D[TextureCount];
        public Texture2D[] height = new Texture2D[TextureCount];

        static GroundTextureSet cached;
        public static GroundTextureSet Load()
        {
            if (!cached) cached = Resources.Load<GroundTextureSet>("GroundTextureSet");
            return cached;
        }

        public Texture2D Diffuse(int index) => At(diffuse, index);
        public Texture2D Normal(int index) => At(normal, index);
        public Texture2D Occlusion(int index) => At(occlusion, index);
        public Texture2D Height(int index) => At(height, index);
        public bool Has(int index) => Diffuse(index);

        static Texture2D At(Texture2D[] textures, int index)
        {
            if (textures == null || textures.Length == 0) return null;
            index = Mathf.Abs(index) % textures.Length;
            return textures[index];
        }
    }
}
