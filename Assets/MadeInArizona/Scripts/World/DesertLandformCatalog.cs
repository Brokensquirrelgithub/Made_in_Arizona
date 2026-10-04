using UnityEngine;

namespace MadeInArizona
{
    /// <summary>
    /// Art for the generated world's large landforms and cover rocks. Every list may stay empty: the world then builds
    /// procedural placeholders of the same size, so the desert packs can be linked later without touching placement.
    /// Models are scaled to the planned footprint and sunk into the ground. Cliff prefabs repeat along long interior
    /// chains and the world boundary; cover-rock prefabs add smaller stones at their bases. Models without colliders
    /// get mesh colliders.
    /// </summary>
    [CreateAssetMenu(menuName = "Made in Arizona/Desert Landform Catalog", fileName = "DesertLandformCatalog")]
    public sealed class DesertLandformCatalog : ScriptableObject
    {
        [Tooltip("Broad flat-topped mesas, roughly round, 30-60 m across.")] public GameObject[] mesas;
        [Tooltip("Tall narrow buttes and spires, 16-26 m across.")] public GameObject[] buttes;
        [Tooltip("Long cliff walls and ridges; the longest horizontal axis follows the wall.")] public GameObject[] cliffs;
        [Tooltip("Large boulders and boulder piles that cars cannot cross.")] public GameObject[] boulders;
        [Tooltip("Medium rocks, about 4-6 m across and 2-3.5 m tall, that stop gunfire.")] public GameObject[] coverRocks;
        [Tooltip("Large closed rock models piled along mountain chains and the boundary rim (two per footprint, scaled up). " +
            "When empty, chains fall back to the cliff models.")] public GameObject[] mountainRocks;
        [Tooltip("Render the models with the terrain's rock shader (matches the ground and turns see-through when it hides the car).")]
        public bool useTerrainMaterial = true;
        [Tooltip("With the terrain shader: keep each pack model's own texture and colour instead of the striped terrain rock.")]
        public bool keepPackColours = true;

        public const string ResourcePath = "Landforms/DesertLandformCatalog";
        public static DesertLandformCatalog Load() => Resources.Load<DesertLandformCatalog>(ResourcePath);

        public GameObject Pick(GeneratedWorld.LandformKind kind, System.Random random)
        {
            var choices = kind == GeneratedWorld.LandformKind.Mesa ? mesas : kind == GeneratedWorld.LandformKind.Butte ? buttes
                : kind == GeneratedWorld.LandformKind.Cliff ? cliffs : kind == GeneratedWorld.LandformKind.Boulders ? boulders : coverRocks;
            return PickFrom(choices, random);
        }
        public bool HasMountainRocks => mountainRocks != null && System.Array.Exists(mountainRocks, r => r);
        public GameObject PickMountainRock(System.Random random) => PickFrom(mountainRocks, random);
        static GameObject PickFrom(GameObject[] choices, System.Random random)
        {
            if (choices == null || choices.Length == 0) return null;
            var pick = choices[random.Next(choices.Length)];
            return pick ? pick : null;
        }
    }
}
