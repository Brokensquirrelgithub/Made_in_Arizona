using UnityEngine;
namespace MadeInArizona
{
    [CreateAssetMenu(menuName = "Made In Arizona/Weapon")]
    public sealed class WeaponDefinition : ScriptableObject
    {
        public string id, displayName; [TextArea] public string description;
        public int contentOrder;
        public float damage, fireRate, speed, blastRadius;
        public Color projectileColor = Color.yellow;
    }
}
