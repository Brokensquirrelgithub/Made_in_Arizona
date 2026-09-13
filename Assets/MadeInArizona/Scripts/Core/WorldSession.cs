using System;
using System.Collections;
using UnityEngine;
namespace MadeInArizona
{
    public sealed partial class GameManager
    {
        WorldGenConfig worldConfig;
        float configPoll;
        public WorldGenConfig WorldConfig => worldConfig ?? (worldConfig=WorldConfigStore.Load());
        public bool UseGeneratedWorld => !SmokeTestRunner.Active || Array.IndexOf(Environment.GetCommandLineArgs(),"-miaWorldTest")>=0;
        public string GenerationStatus {get;private set;}="Preparing terrain…";
        public void ShowMainMenu()
        {
            if(State!=GameState.Garage)ReturnToGarage();
            State=GameState.MainMenu;Time.timeScale=1;StateChanged?.Invoke();
            var config=WorldConfig;
        }
        public void StartCampaign(int seed,float size)
        {
            if(State==GameState.Generating)return;
            WorldConfig.seed=seed;WorldConfig.size=size;
            if(!WorldConfigStore.Save(WorldConfig)){Notify(WorldConfigStore.LastError);return;}
            StartCoroutine(GenerateCampaign(0));
        }
        IEnumerator GenerateCampaign(int mission)
        {
            State=GameState.Generating;InputManager.Instance.SetEnabled(false);Time.timeScale=1;
            GenerationStatus="Shaping Arizona • seed "+WorldConfig.seed+" • "+Mathf.RoundToInt(WorldConfig.size)+" m";
            StateChanged?.Invoke();yield return null;yield return null;
            StartMission(mission);
            Notify("WORLD READY • M opens the map • explore off-road for rare salvage");
        }
        void PollWorldConfig()
        {
            if(!UseGeneratedWorld||State==GameState.Generating||Time.unscaledTime<configPoll)return;
            configPoll=Time.unscaledTime+.2f;
            var current=WorldConfig;
            if(WorldConfigStore.TryReload(out var changed))
            {
                worldConfig=changed;
                if(GeneratedWorld.Active && (State==GameState.Playing||State==GameState.Paused))
                {
                    StartCoroutine(GenerateCampaign(SelectedMission));
                    Notify("World JSON updated • regenerating current mission; campaign progress retained");
                }
                else Notify("World JSON updated • settings ready for campaign generation");
            }
        }
    }
}
