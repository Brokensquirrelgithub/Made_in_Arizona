using System.Collections;
using UnityEngine;

namespace MadeInArizona
{
    public sealed partial class GameManager
    {
        Coroutine coopWorldBuild;
        // Only the host runs MissionManager.Begin/Tick. Guests build the same static map and
        // render snapshots of the host's vehicles and mission state.
        internal void LoadCoopWorld(int mode, int mission, WorldGenConfig config)
        {
            if (coopWorldBuild != null) StopCoroutine(coopWorldBuild);
            coopWorldBuild = StartCoroutine(BuildCoopWorld(mode, mission, config));
        }

        IEnumerator BuildCoopWorld(int mode, int mission, WorldGenConfig config)
        {
            State = GameState.Generating;
            InputManager.Instance.SetEnabled(false);
            Time.timeScale = 1;
            StateChanged?.Invoke();
            yield return null;
            if (mode == 0)
            {
                World.BuildGarage();
                Player = null;
                State = GameState.MainMenu;
                AudioManager.Instance.SetCombat(false);
            }
            else
            {
                IsCombatTrial = mode == 2;
                SelectedMission = Mathf.Clamp(mission, 0, ContentCatalog.Missions.Length - 1);
                if (mode == 2) World.BuildCombatArena();
                else
                {
                    worldConfig = config;
                    World.BuildGeneratedMission(SelectedMission, worldConfig);
                }
                Player = null;
                Mission.SetRemoteMission(IsCombatTrial ? null : ContentCatalog.Missions[SelectedMission]);
                State = GameState.Playing;
                AudioManager.Instance.SetCombat(true);
            }
            DevVisuals.Apply();
            StateChanged?.Invoke();
            CoopSession.Instance?.RemoteWorldReady();
            coopWorldBuild = null;
        }

        internal void SetCoopPlayer(VehicleController vehicle)
        {
            Player = vehicle;
            CurrentVehicle = vehicle ? vehicle.Definition : null;
            if (vehicle)
            {
                CameraController.Instance.Target = vehicle.transform;
                CameraController.Instance.Snap();
                InputManager.Instance.SetEnabled(true);
            }
        }

        internal void SetCoopOutcome(GameState state)
        {
            if (state != GameState.Playing && state != GameState.Won && state != GameState.Lost) return;
            if (state == GameState.Playing && State == GameState.Paused) return;
            if (State == state) return;
            State = state;
            InputManager.Instance.SetEnabled(state == GameState.Playing);
            StateChanged?.Invoke();
        }
    }
}
