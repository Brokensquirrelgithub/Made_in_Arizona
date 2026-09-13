using UnityEngine;

namespace MadeInArizona
{
    /// <summary>Build-time catalog of the licensed Pandazole low-poly nature meshes.</summary>
    public sealed class PandazoleNatureCatalog : ScriptableObject
    {
        public Texture2D atlas;
        public Mesh[] pines;
        public Mesh[] broadleafTrees;
        public Mesh[] cacti;
        public Mesh[] rocks;
        public Mesh[] grasses;
        public Mesh[] bushes;
        public static PandazoleNatureCatalog Load() => Resources.Load<PandazoleNatureCatalog>("Nature/PandazoleNatureCatalog");
        public Mesh Pick(Mesh[] choices,System.Random random) => choices!=null&&choices.Length>0?choices[random.Next(choices.Length)]:null;
    }
}
