using UnityEngine;
namespace MadeInArizona
{
    public enum MissionMode { Recovery, Demolition, Defense, Convoy, Race, Rescue, Boss, Escape, Salvage }
    public enum OptionalKind { Destruction, Health, Time, Salvage, Combo, Kills }
    [CreateAssetMenu(menuName = "Made In Arizona/Mission")]
    public sealed class MissionDefinition : ScriptableObject
    {
        public string id, title, region;
        [TextArea] public string briefing, opening, midpoint, closing, optionalObjective;
        public int contentOrder, reward = 650, enemyCount = 4, targetCount = 3;
        public float timeLimit = 420, objectiveDuration = 45;
        public MissionMode mode; public OptionalKind optionalKind;
        public float optionalThreshold = 8;
        public string forcedVehicle;
    }
}
