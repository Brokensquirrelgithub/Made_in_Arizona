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
        public bool UseGeneratedWorld => !SmokeTestRunner.Active || Array.IndexOf(Environment.GetCommandLineArgs(),"-miaWorldTest")>=0 || Array.IndexOf(Environment.GetCommandLineArgs(),"-miaMountainTest")>=0 || Array.IndexOf(Environment.GetCommandLineArgs(),"-miaAssetBehaviourTest")>=0 || Array.IndexOf(Environment.GetCommandLineArgs(),"-miaEscortTest")>=0 || Array.IndexOf(Environment.GetCommandLineArgs(),"-miaMinimapCapture")>=0 || Array.IndexOf(Environment.GetCommandLineArgs(),"-miaGeneratedCampaignTest")>=0 || Array.IndexOf(Environment.GetCommandLineArgs(),"-miaPlaytestRevision")>=0 || Array.IndexOf(Environment.GetCommandLineArgs(),"-miaEnemyBalanceTest")>=0 || Array.IndexOf(Environment.GetCommandLineArgs(),"-miaFrameTimingTest")>=0 || Array.IndexOf(Environment.GetCommandLineArgs(),"-miaTrailReview")>=0 || Array.IndexOf(Environment.GetCommandLineArgs(),"-miaReachabilityTest")>=0 || Array.IndexOf(Environment.GetCommandLineArgs(),"-miaTiltWorld")>=0 || Array.IndexOf(Environment.GetCommandLineArgs(),"-miaSpawnGenerated")>=0 || Array.IndexOf(Environment.GetCommandLineArgs(),"-miaUiCapture")>=0 || Array.IndexOf(Environment.GetCommandLineArgs(),"-miaDriveProbe")>=0;
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
        /// <summary>
        /// Rolls a new seed and rebuilds the generated world. During a sortie the current job restarts on the new
        /// map; garage progression, completed jobs and discoveries are kept.
        /// </summary>
        public void RegenerateWorld()
        {
            if(State==GameState.Generating)return;
            int seed=UnityEngine.Random.Range(1,int.MaxValue);
            bool inWorld=GeneratedWorld.Active&&(State==GameState.Playing||State==GameState.Paused)&&!IsCombatTrial;
            if(!inWorld){StartCampaign(seed,WorldConfig.size);return;}
            WorldConfig.seed=seed;
            if(!WorldConfigStore.Save(WorldConfig)){Notify(WorldConfigStore.LastError);return;}
            StartCoroutine(GenerateCampaign(SelectedMission));
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
