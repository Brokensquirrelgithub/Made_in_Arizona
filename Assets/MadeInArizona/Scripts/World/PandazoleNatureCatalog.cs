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
        /// <summary>
        /// Pack models built only from stacked boxes (Grass_07 and Grass_08 are four axis-aligned cubes each) read as
        /// placeholder geometry next to the rest of the ecology, so they are never scattered.
        /// </summary>
        public static readonly string[] Excluded = { "Grass_07", "Grass_08" };
        public static bool IsExcluded(string meshName) => System.Array.IndexOf(Excluded, meshName) >= 0;
        public static PandazoleNatureCatalog Load()
        {
            var catalog = Resources.Load<PandazoleNatureCatalog>("Nature/PandazoleNatureCatalog");
            if (catalog && catalog.grasses != null) catalog.grasses = System.Array.FindAll(catalog.grasses, m => m && !IsExcluded(m.name));
            if (catalog && catalog.bushes != null) catalog.bushes = System.Array.FindAll(catalog.bushes, m => m && !IsExcluded(m.name));
            return catalog;
        }
        public Mesh Pick(Mesh[] choices,System.Random random) => choices!=null&&choices.Length>0?choices[random.Next(choices.Length)]:null;
    }
}
