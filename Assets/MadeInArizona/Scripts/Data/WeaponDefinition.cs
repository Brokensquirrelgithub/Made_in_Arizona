using UnityEngine;
namespace MadeInArizona
{
    [CreateAssetMenu(menuName = "Made In Arizona/Weapon")]
    public sealed class WeaponDefinition : ScriptableObject
    {
        public string id, displayName; [TextArea] public string description;
        /// <summary>Sandbox role, in the spirit of Halo: what the weapon is for, what it beats and what beats it.</summary>
        public string role, strength, weakness;
        public int contentOrder;
        public float damage, fireRate, speed, blastRadius;
        public Color projectileColor = Color.yellow;
        public AudioClip[] fireSounds = System.Array.Empty<AudioClip>();
    }
}
