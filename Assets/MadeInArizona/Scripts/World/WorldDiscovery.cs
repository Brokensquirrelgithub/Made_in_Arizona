using UnityEngine;

namespace MadeInArizona
{
    public sealed class WorldDiscovery : MonoBehaviour
    {
        public void OnDestroyed(GameObject source)
        {
            var game = GameManager.Instance;
            if (!game || !game.IsPlaying || game.Save.achievements.Contains("wonton-destruction")) return;
            game.Save.achievements.Add("wonton-destruction");
            game.Notify("WONTON DESTRUCTION • An unfortunate day for the lunch special.");
            DialogueSystem.Instance.Say("JOHNNY", "The insurance form has a separate box for dumplings. It does now.");
            SaveSystem.Save(game.Save);
        }
    }
}
