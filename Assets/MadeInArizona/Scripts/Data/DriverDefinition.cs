using UnityEngine;
namespace MadeInArizona
{
    [CreateAssetMenu(menuName = "Made In Arizona/Driver")]
    public sealed class DriverDefinition : ScriptableObject
    {
        public string id, displayName; [TextArea] public string biography, perk, line;
        public int contentOrder;
        public Color color = Color.white;
        public float repairMultiplier = 1, powerMultiplier = 1, gripMultiplier = 1;
    }
}
