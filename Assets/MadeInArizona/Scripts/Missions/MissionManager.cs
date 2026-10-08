using System;
using System.Collections.Generic;
using UnityEngine;
namespace MadeInArizona
{
    /// <summary>Runs a handcrafted opening job and reusable, fully completable objective state machines.</summary>
    public sealed class MissionManager : MonoBehaviour
    {
        public string Objective { get; private set; }
        public int Stage { get; private set; }
        public int Score { get; private set; }
        public int Kills { get; private set; }
        public int DestructionCount { get; private set; }
        public float Elapsed { get; private set; }
        public float Remaining { get { return definition==null?0:Mathf.Max(0,effectiveTimeLimit-Elapsed); } }
        public int Combo { get; private set; }
        public float Progress { get; private set; }
        public Vector3 ObjectivePosition { get; private set; }
        public bool OptionalComplete { get; private set; }
        public string LastAward { get; private set; }
        public string Debrief { get; private set; }
        public int AwardedMoney { get; private set; }
        public int AwardedSalvage { get; private set; }
        public float EscortHealth { get { return escort&&escort.Damage?escort.Damage.Health/escort.Damage.MaxHealth:0; } }
        public float HoldProgress { get { return heldSeconds; } }
        public MissionDefinition Definition { get { return definition; } }
        MissionDefinition definition;
        int remoteMarkerStage = -1;
        internal void SetRemoteMission(MissionDefinition mission)
        {
            if (!mission)
            {
                if (!trialDefinition) trialDefinition = ScriptableObject.CreateInstance<MissionDefinition>();
                trialDefinition.timeLimit = 180;
                mission = trialDefinition;
            }
            if (marker) Destroy(marker);
            marker = null;
            remoteMarkerStage = -1;
            definition = mission;
            Objective = "Waiting for host";
            Elapsed = 0;
            effectiveTimeLimit = mission ? mission.timeLimit : 180;
            Score = Kills = Stage = 0;
            ObjectivePosition = Vector3.zero;
        }

        internal void ApplyRemoteState(string objective, Vector3 position, float progress, float remaining,
            int stage, int score, int kills, int combo, int destruction, int money, string debrief)
        {
            Objective = objective;
            ObjectivePosition = position;
            if (position != Vector3.zero && Game != null && Game.World != null)
            {
                if (!marker || remoteMarkerStage != stage)
                {
                    if (marker) Destroy(marker);
                    marker = Game.World.CreateMarker(position, new Color(1, .76f, .15f), "OBJECTIVE");
                    remoteMarkerStage = stage;
                }
                else marker.transform.position = position;
            }
            Progress = progress;
            effectiveTimeLimit = remaining;
            Elapsed = 0;
            Stage = stage;
            Score = score;
            Kills = kills;
            Combo = combo;
            DestructionCount = destruction;
            AwardedMoney = money;
            Debrief = debrief;
        }
        int missionIndex, stageKills, wave, checkpoint, bestCombo, pickupCount;
        float heldSeconds, comboTimeout, nextWaveTime, stageStarted, effectiveTimeLimit;
        bool ended, midwaySaid, finalStand, collecting;
        GameObject marker;
        VehicleController escort, suspect, boss;
        const float EscortSpeedMultiplier=3, EscortHealthMultiplier=2.5f;
        readonly List<GameObject> cacheMarkers=new List<GameObject>();
        readonly List<Vector3> cachePositions=new List<Vector3>();
        GameManager Game { get { return GameManager.Instance; } }
        Vector3 PlayerPosition { get { return Game.Player?Game.Player.transform.position:Vector3.zero; } }
        float TeamHealthRatio
        {
            get
            {
                if (!(CoopSession.Instance && CoopSession.Instance.IsHost))
                    return Game.Player && Game.Player.Damage ? Game.Player.Damage.Health / Game.Player.Damage.MaxHealth : 0;
                float total = 0;
                int players = 0;
                foreach (var car in VehicleController.Active)
                    if (car && car.IsPlayer && car.Damage)
                    {
                        total += car.Damage.Health / car.Damage.MaxHealth;
                        players++;
                    }
                return players > 0 ? total / players : 0;
            }
        }
        Vector3 Point(int index)
        {
            var pts=Game.World.ObjectivePoints;
            return TerrainPoint(pts.Count>0?pts[Mathf.Clamp(index,0,pts.Count-1)]:new Vector3(0,.1f,20+index*25));
        }
        MissionDefinition trialDefinition;
        public void BeginCombatTrial()
        {
            if(!trialDefinition)trialDefinition=ScriptableObject.CreateInstance<MissionDefinition>();
            trialDefinition.id="combat_trial";trialDefinition.title="Vehicle Combat Trial";trialDefinition.region="117° Proving Ground";
            trialDefinition.mode=MissionMode.Recovery;trialDefinition.enemyCount=6;trialDefinition.timeLimit=180;
            trialDefinition.opening="JOHNNY: Twenty-four hostiles on the range. First: corporate guns, HOA traps, and scavenger rushes. Watch their colors and warning lines.";
            trialDefinition.closing="Trial complete. Your build can handle a disagreement.";
            trialDefinition.optionalKind=OptionalKind.Health;trialDefinition.optionalThreshold=.5f;
            Begin(trialDefinition,-1);SetObjective("WAVE 1 / 2 • destroy "+SpawnManager.EnemyCount(3)+" hostile vehicles",Point(0));
        }
        void TickCombatTrial()
        {
            int total=SpawnManager.EnemyCount(6);
            if(Stage==0 && Kills>=SpawnManager.EnemyCount(3)) {
                SetStage(1);SpawnWave(3,PlayerPosition);
                DialogueSystem.Instance?.Say("JOHNNY","Second wave: open-house sharks, snowbird roadblocks, and neon tuner cars. Grab their drops and keep moving.",7);
            }
            Progress=Kills/(float)total;
            if(Kills>=total){Objective="TRIAL COMPLETE • "+total+" / "+total+" hostile vehicles destroyed";Finish();return;}
            float nearest=float.MaxValue;
            foreach(var enemy in VehicleController.Active) {
                if(!enemy||enemy.IsPlayer||enemy.Damage.IsDead)continue;
                float distance=(enemy.transform.position-PlayerPosition).sqrMagnitude;
                if(distance<nearest){nearest=distance;ObjectivePosition=enemy.transform.position;}
            }
            Objective="WAVE "+(Stage+1)+" / 2 • "+Kills+" / "+total+" hostile vehicles destroyed";
        }

        public void Begin(MissionDefinition mission,int index)
        {
            definition=mission;missionIndex=index;Stage=0;Score=0;Kills=0;DestructionCount=0;Elapsed=0;Combo=0;Progress=0;
            effectiveTimeLimit=mission.timeLimit;
            if(GeneratedWorld.Active!=null)
            {
                Vector3 size=GeneratedWorld.Active.WorldBounds.size;
                float travelScale=Mathf.Clamp(new Vector2(size.x,size.z).magnitude/230f,1f,4f);
                effectiveTimeLimit=Mathf.Max(mission.timeLimit,mission.timeLimit*travelScale);
            }
            AwardedMoney=0;AwardedSalvage=0;heldSeconds=0;checkpoint=0;wave=0;stageKills=0;bestCombo=0;pickupCount=0;
            ended=false;midwaySaid=false;finalStand=false;collecting=false;OptionalComplete=false;LastAward="";Debrief="";
            nextWaveTime=14;stageStarted=0;escort=null;suspect=null;boss=null;
            if(marker)Destroy(marker);cacheMarkers.Clear();cachePositions.Clear();
            DialogueSystem.Instance?.SayLine(mission.opening,14);
            if(index>=0)AddCaches();
            if(index==0)
            {
                suspect=Spawn(Point(0)+Vector3.up,1);
                SpawnGroup(SpawnManager.EnemyCountMultiplier-1,Point(0));
                if(suspect)
                {
                    suspect.Stats.maxSpeed=55;suspect.Stats.horsepower*=.65f;
                    var ai=suspect.GetComponent<EnemyAI>();if(ai){ai.UseDestination=true;ai.FollowRoads=true;ai.Destination=Point(1);}
                }
                SetObjective("Pursue the stolen shop loaner to Half-Price Horizon Salvage",Point(0));
                return;
            }
            switch(mission.mode)
            {
                case MissionMode.Recovery:
                    SpawnWave(Mathf.Max(3,mission.enemyCount/2),Point(0));
                    SetObjective("Break the repossession crew: destroy "+SpawnManager.EnemyCount(Mathf.Max(3,mission.enemyCount/2))+" vehicles",Point(0));break;
                case MissionMode.Convoy:
                    escort=Spawn(EscortStart(),2);
                    if(escort)
                    {
                        // Three times the van's former 50 km/h pace, with the power to reach it.
                        escort.Stats.maxSpeed=50*EscortSpeedMultiplier;escort.Stats.horsepower*=.65f*EscortSpeedMultiplier;escort.Stats.torque*=EscortSpeedMultiplier;
                        // The van turns round rather than reversing, and follows the roads (never up a cliff) to each drop.
                        escort.AutoReverse=false;
                        var ai=escort.GetComponent<EnemyAI>();
                        if(ai)
                        {
                            ai.IsFriendly=true;escort.Damage.SetDurabilityMultiplier(EscortHealthMultiplier);ai.UseDestination=true;ai.FollowRoads=true;ai.Destination=Point(0);
                            ai.PlanRoute();Quaternion facing=Quaternion.LookRotation(ai.RouteHeading(),Vector3.up);
                            escort.transform.rotation=facing;escort.Body.rotation=facing;
                        }
                    }
                    SetObjective("Escort the evidence van • stay within 32 m",Point(0));SpawnWave(3,Point(0));break;
                case MissionMode.Defense:
                    SetObjective("Reach the broadcast compound and hold the gold perimeter",Point(1));SpawnWave(Mathf.Max(3,mission.enemyCount/3),Point(1));break;
                case MissionMode.Race:case MissionMode.Escape:
                    SetObjective("Checkpoint 1 / "+Mathf.Min(4,mission.targetCount)+" • drive through the gold ring",Point(0));SpawnWave(3,Point(1));break;
                case MissionMode.Boss:
                    SetObjective("Defeat the security screen before the command vehicle arrives",Point(1));SpawnWave(Mathf.Max(3,mission.enemyCount/3),Point(1));break;
                case MissionMode.Demolition:
                    SetObjective("Demolish corporate property: 0 / "+mission.targetCount,Point(1));SpawnWave(Mathf.Max(3,mission.enemyCount/2),Point(1));break;
                default:
                    SetObjective((mission.mode==MissionMode.Rescue?"Open data cache":"Recover component")+" 1 / "+mission.targetCount+" • hold INTERACT nearby",Point(0));
                    SpawnWave(3,Point(0));break;
            }
        }
        public void Tick(float dt)
        {
            if(ended||definition==null||Game==null||!Game.IsPlaying||!Game.Player)return;
            // Exploration before approaching a job is free-roam; races begin their clock immediately.
            bool clockRunning=definition.mode==MissionMode.Race||definition.mode==MissionMode.Escape||Stage>0||Near(ObjectivePosition,150);
            if(clockRunning)Elapsed+=dt;comboTimeout-=dt;if(comboTimeout<=0)Combo=0;
            if(Remaining<=0) { Fail("The job window expired. Retry from the garage with a different build.");return; }
            if(Game.Player.Damage.IsDead && !(CoopSession.Instance && CoopSession.Instance.IsHost && CoopSession.Instance.AnyPlayerAlive))return;
            CheckCaches();UpdateOptional(false);
            if(missionIndex<0)TickCombatTrial();
            else if(missionIndex==0)TickFirstMission();
            else switch(definition.mode)
            {
                case MissionMode.Recovery:TickRecovery();break;
                case MissionMode.Demolition:TickDemolition();break;
                case MissionMode.Defense:TickDefense(dt);break;
                case MissionMode.Convoy:TickConvoy();break;
                case MissionMode.Race:case MissionMode.Escape:TickRace();break;
                case MissionMode.Boss:TickBoss();break;
                default:TickCollection();break;
            }
            if(marker)marker.transform.position=ObjectivePosition;
            Progress=Mathf.Clamp01(Progress);
        }
        void TickFirstMission()
        {
            if(Stage==0)
            {
                ObjectivePosition=suspect&&!suspect.Damage.IsDead?suspect.transform.position:Point(1);
                if(Near(ObjectivePosition,16)||(suspect&&suspect.Damage.IsDead))
                {
                    SetStage(1);stageKills=Kills;SpawnWave(3,Point(1));
                    if(suspect&&!suspect.Damage.IsDead){var ai=suspect.GetComponent<EnemyAI>();if(ai)ai.UseDestination=false;}
                    DialogueSystem.Instance?.SayLine("Johnny: There's our loaner. Customer replaced the wheel nut with a hose clamp.|Stallion: I'll correct the workmanship.|Johnny: Clear the yard first. Propane tanks are persuasive shop tools.",12);
                    SetObjective("Clear the junkyard crew • 0 / "+SpawnManager.EnemyCount(3)+" hostile vehicles",Point(1));
                }
                Progress=.1f;
            }
            else if(Stage==1)
            {
                int required=SpawnManager.EnemyCount(3);
                Objective="Clear the junkyard crew • "+Mathf.Min(required,Kills-stageKills)+" / "+required+" hostile vehicles";
                Progress=.2f+Mathf.Clamp01((Kills-stageKills)/(float)required)*.3f;
                if(Kills-stageKills>=required)
                { SetStage(2);Midpoint();SetObjective("Recover the Sunsprawl ledger • hold INTERACT at the gold ring",Point(1)); }
            }
            else if(Stage==2)
            {
                Progress=.62f;
                if(InteractAt(Point(1)))
                {
                    Score+=600;SetStage(3);SpawnWave(2,Point(0));
                    DialogueSystem.Instance?.SayLine("Stallion: Ledger secured.|Johnny: Bring it home. Somebody sent armed trucks over an invoice smaller than a set of brake pads.",10);
                    SetObjective("Return the ledger to 117° Auto Care • reach extraction",Game.World.ExtractionPoint);
                }
            }
            else {Progress=.82f;TryExtract();}
        }
        void TickRecovery()
        {
            int required=SpawnManager.EnemyCount(Mathf.Max(3,definition.enemyCount/2));
            if(Stage==0&&Kills>=required)
            {SetStage(1);Midpoint();SpawnWave(Mathf.Max(2,definition.enemyCount/3),Point(2));SetObjective("Recover the title archive • hold INTERACT at the gold ring",Point(2));}
            else if(Stage==1&&InteractAt(Point(2))){Score+=650;SetStage(2);SetObjective("Return the title archive to extraction",Game.World.ExtractionPoint);}
            else if(Stage==2)TryExtract();
            Progress=Stage==0?Mathf.Clamp01(Kills/(float)required)*.4f:Stage==1?.55f:.85f;
        }
        void TickDemolition()
        {
            if(Stage==0)
            {
                Objective="Demolish corporate property: "+Mathf.Min(DestructionCount,definition.targetCount)+" / "+definition.targetCount;
                Progress=Mathf.Clamp01(DestructionCount/(float)definition.targetCount)*.75f;
                if(DestructionCount>=definition.targetCount/2&&!midwaySaid){Midpoint();SpawnWave(Mathf.Max(3,definition.enemyCount/3),Point(2));}
                if(DestructionCount>=definition.targetCount){SetStage(1);SetObjective("Site decommissioned • reach extraction",Game.World.ExtractionPoint);}
            }
            else {Progress=.9f;TryExtract();}
        }
        void TickDefense(float dt)
        {
            if(Stage==0)
            {
                bool inside=Near(Point(1),25);if(inside)heldSeconds+=dt;
                Objective=(inside?"UPLINK TRANSMITTING":"RETURN TO GOLD PERIMETER")+" • "+Mathf.FloorToInt(heldSeconds)+" / "+Mathf.CeilToInt(definition.objectiveDuration)+" sec";
                Progress=heldSeconds/definition.objectiveDuration*.8f;
                if(heldSeconds>definition.objectiveDuration*(wave+1)/3f&&wave<2)
                {wave++;SpawnWave(Mathf.Max(3,definition.enemyCount/3),Point(wave));if(wave==1)Midpoint();}
                if(heldSeconds>=definition.objectiveDuration){SetStage(1);SetObjective("Upload complete • get the crew to extraction",Game.World.ExtractionPoint);}
            }
            else {Progress=.9f;TryExtract();}
        }
        void TickConvoy()
        {
            if(!escort||escort.Damage.IsDead){Fail("The evidence van was destroyed. Stay close and clear the road ahead on the next attempt.");return;}
            if(Stage==0)
            {
                var ai=escort.GetComponent<EnemyAI>();bool close=Near(escort.transform.position,32);
                if(ai){ai.UseDestination=true;ai.Destination=Point(checkpoint);ai.HoldPosition=!close;}
                ObjectivePosition=escort.transform.position;
                Objective=(close?"ESCORT MOVING":"REGROUP WITH THE EVIDENCE VAN")+" • transfer "+(checkpoint+1)+" / 3 • hull "+Mathf.RoundToInt(EscortHealth*100)+"%";
                Progress=checkpoint/3f*.8f;
                if(Vector3.Distance(escort.transform.position,Point(checkpoint))<11)
                {
                    checkpoint++;Score+=350;AudioManager.Instance?.PlayObjective();
                    if(checkpoint<3){SpawnWave(Mathf.Max(3,definition.enemyCount/3),Point(checkpoint));Midpoint();}
                    else{SetStage(1);if(ai)ai.HoldPosition=true;SetObjective("Evidence transferred • player return to extraction",Game.World.ExtractionPoint);}
                }
            }
            else {Progress=.9f;TryExtract();}
        }
        void TickRace()
        {
            int total=Mathf.Clamp(definition.targetCount,1,4);
            if(Stage==0)
            {
                if(Near(Point(checkpoint),11))
                {
                    checkpoint++;Score+=350;AudioManager.Instance?.PlayObjective();
                    if(checkpoint>=total){SetStage(1);SetObjective("Course complete • reach extraction before the timer expires",Game.World.ExtractionPoint);}
                    else{SetObjective("Checkpoint "+(checkpoint+1)+" / "+total+" • drive through the gold ring",Point(checkpoint));if(checkpoint==2){Midpoint();SpawnWave(3,Point(3));}}
                }
                Progress=checkpoint/(float)total*.8f;
            }
            else {Progress=.9f;TryExtract();}
        }
        void TickCollection()
        {
            int total=Mathf.Clamp(definition.targetCount,1,4);
            if(Stage==0&&InteractAt(Point(checkpoint)))
            {
                checkpoint++;Score+=300;AudioManager.Instance?.PlayObjective();
                if(checkpoint>=total){SetStage(1);SpawnWave(Mathf.Max(2,definition.enemyCount/3),Point(0));SetObjective("All items secured • reach extraction",Game.World.ExtractionPoint);}
                else
                {
                    SpawnWave(Mathf.Max(2,definition.enemyCount/total),Point(checkpoint));
                    SetObjective((definition.mode==MissionMode.Rescue?"Open data cache":"Recover component")+" "+(checkpoint+1)+" / "+total+" • hold INTERACT nearby",Point(checkpoint));
                    if(checkpoint==1)Midpoint();
                }
            }
            else if(Stage==1)TryExtract();
            Progress=Stage==0?checkpoint/(float)total*.75f:.9f;
        }
        void TickBoss()
        {
            if(Stage==0)
            {
                int required=SpawnManager.EnemyCount(Mathf.Max(3,definition.enemyCount/3));
                Objective="Clear the command vehicle's security screen • "+Mathf.Min(Kills,required)+" / "+required;
                Progress=Mathf.Clamp01(Kills/(float)required)*.25f;
                if(Kills>=required)
                {
                    SetStage(1);boss=Spawn(Point(3)+Vector3.up,7);Midpoint();
                    SpawnGroup(SpawnManager.EnemyCountMultiplier-1,Point(3));
                    SetObjective("Disable external pods, then destroy the command vehicle",Point(3));
                }
            }
            else if(Stage==1)
            {
                if(boss&&!boss.Damage.IsDead)
                {
                    ObjectivePosition=boss.transform.position;float health=boss.Damage.Health/boss.Damage.MaxHealth;
                    Objective="COMMAND VEHICLE • "+Mathf.CeilToInt(health*100)+"% • target exposed pods";Progress=.25f+(1-health)*.55f;
                    if(health<.5f&&!finalStand){finalStand=true;SpawnWave(Mathf.Max(3,definition.enemyCount/4),Point(2));DialogueSystem.Instance?.Say("JOHNNY","Cooling pressure is dropping. They called for reinforcements. Keep moving and finish the machine.",8);}
                }
                else {Score+=2000;SetStage(2);SetObjective("Command key recovered • reach extraction",Game.World.ExtractionPoint);}
            }
            else {Progress=.9f;TryExtract();}
        }
        void TryExtract() { if(Near(Game.World.ExtractionPoint,10))Finish(); }
        bool Near(Vector3 point,float radius)
        {
            if (CoopSession.Instance && CoopSession.Instance.IsHost)
            {
                foreach (var vehicle in VehicleController.Active)
                {
                    if (!vehicle || !vehicle.IsPlayer || !vehicle.Damage || vehicle.Damage.IsDead) continue;
                    Vector3 separation = vehicle.transform.position - point; separation.y = 0;
                    if (separation.sqrMagnitude < radius * radius) return true;
                }
                return false;
            }
            Vector3 delta=PlayerPosition-point;delta.y=0;return delta.sqrMagnitude<radius*radius;
        }
        bool InteractAt(Vector3 point)
        {
            bool here=Near(point,9);
            if(here&&!collecting){Game.Notify("Hold E / gamepad South to recover the marked objective.");collecting=true;}
            if(!here)collecting=false;
            if (!here) return false;
            if (InputManager.Instance != null && InputManager.Instance.Interact && Game.Player &&
                (Game.Player.transform.position - point).sqrMagnitude < 81) return true;
            if (CoopSession.Instance && CoopSession.Instance.IsHost)
                foreach (var player in VehicleController.Active)
                    if (player && player.IsPlayer && player.Damage && !player.Damage.IsDead && player.HasRemoteInput &&
                        player.RemoteControls.interact && (player.transform.position - point).sqrMagnitude < 81) return true;
            return false;
        }
        void SetStage(int stage){Stage=stage;stageStarted=Elapsed;collecting=false;}
        void SetObjective(string text,Vector3 point)
        {
            point=TerrainPoint(point);Objective=text;ObjectivePosition=point;if(marker)Destroy(marker);
            marker=Game.World.CreateMarker(point,new Color(1,.76f,.15f),"OBJECTIVE");
        }
        void Midpoint(){if(midwaySaid)return;midwaySaid=true;DialogueSystem.Instance?.SayLine(definition.midpoint,13);}
        /// <summary>The escort van starts on the road beside the starter town rather than in a lot or ditch.</summary>
        Vector3 EscortStart()
        {
            Vector3 start=Game.World.PlayerSpawn+new Vector3(5,0,12);
            var world=GeneratedWorld.Active;
            if(world){Vector3 road=world.NearestPatrolRoad(start);if(Vector3.Distance(road,start)<40)start=road;}
            return start;
        }
        VehicleController Spawn(Vector3 position,int archetype)
        {
            if(GeneratedWorld.Active){position=TerrainPoint(position);position.y+=1;}
            var target = escort && !escort.Damage.IsDead && archetype % 2 == 0 ? escort : Game.Player;
            var vehicle=SpawnManager.Spawn(position,archetype,target,FactionRules.ForMission(missionIndex,archetype,Mathf.Max(wave,Stage)));
            if(vehicle)vehicle.transform.SetParent(Game.World.transform);return vehicle;
        }
        Vector3 Ground(Vector3 p)
        {
            if(GeneratedWorld.Active==null){p.x=Mathf.Clamp(p.x,-86,86);p.z=Mathf.Clamp(p.z,-64,126);p.y=1.2f;return p;}
            return TerrainPoint(p)+Vector3.up;
        }
        /// <summary>
        /// On the open map, place new crews outside the camera and far enough away to approach as a wave.
        /// </summary>
        Vector3 OffScreen(Vector3 p)
        {
            var cam=Camera.main;var player=Game.Player;
            if(!cam||!player)return p;
            float minimumDistance=GeneratedWorld.Active?65f:24f;
            Vector3 initial=p-player.transform.position;initial.y=0;
            if(!InView(cam,p)&&initial.sqrMagnitude>=minimumDistance*minimumDistance)return p;
            Vector3 away=p-player.transform.position;away.y=0;
            if(away.sqrMagnitude<.01f)away=Vector3.forward;
            away.Normalize();
            Vector3 best=p;float bestTravel=float.MaxValue;
            for(int turn=0;turn<4;turn++)
            {
                Vector3 dir=Quaternion.Euler(0,turn*90,0)*away,q=p;
                for(int step=0;step<45;step++)
                {
                    Vector3 next=Ground(q+dir*6);
                    if((next-q).sqrMagnitude<.01f||GeneratedWorld.Active&&!GeneratedWorld.Contains(next))break;
                    q=next;
                    Vector3 fromPlayer=q-player.transform.position;fromPlayer.y=0;
                    if(fromPlayer.sqrMagnitude<minimumDistance*minimumDistance)continue;
                    if(!InView(cam,q))
                    {
                        // A perspective lens sees much farther down the road. Pick the closest of all four exits
                        // so a wave does not get stranded at the distant map edge when a side exit was nearby.
                        Vector3 travel=q-p;travel.y=0;
                        if(travel.sqrMagnitude<bestTravel){best=q;bestTravel=travel.sqrMagnitude;}
                        break;
                    }
                }
            }
            return best;
        }
        static bool InView(Camera cam,Vector3 p){var v=cam.WorldToViewportPoint(p);return v.z>0&&v.x>-.12f&&v.x<1.12f&&v.y>-.12f&&v.y<1.12f;}
        void SpawnWave(int count,Vector3 center)
        {
            SpawnGroup(SpawnManager.EnemyCount(count),center);
        }
        void SpawnGroup(int count,Vector3 center)
        {
            if(missionIndex<0&&Game.IsCombatTrial)
            {
                // The proving ground is only 108 by 92 metres. Its concrete walls make open-map
                // off-screen placement unusable, so trial crews enter at evenly spaced inner edges.
                var player=Game.Player.transform.position;
                var used=new bool[24];
                for(int i=0;i<count;i++)
                {
                    int slot=(i*2+Stage)%24;
                    for(int offset=0;offset<24;offset++)
                    {
                        int candidate=(slot+offset)%24;
                        float candidateAngle=candidate*Mathf.PI/12f;
                        var candidatePoint=new Vector3(Mathf.Cos(candidateAngle)*46f,1.2f,Mathf.Sin(candidateAngle)*38f);
                        Vector3 delta=candidatePoint-player;delta.y=0;
                        if(!used[candidate]&&delta.sqrMagnitude>=22f*22f){slot=candidate;break;}
                    }
                    used[slot]=true;
                    float angle=slot*Mathf.PI/12f;
                    Vector3 p=new Vector3(Mathf.Cos(angle)*46f,1.2f,Mathf.Sin(angle)*38f);
                    Spawn(p,i%3+(Stage>0?2:0));
                }
                return;
            }
            for(int i=0;i<count;i++)
            {
                float angle=(i*137.5f+missionIndex*31)*Mathf.Deg2Rad;
                Vector3 p=center+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*(18+(i%3)*5+(i/12)*14);
                p=OffScreen(Ground(p));
                Spawn(p,missionIndex<0?i%3+(Stage>0?2:0):missionIndex<2?i%2:(i+wave+missionIndex)%7);
            }
        }
        public void RegisterKill(VehicleController killer = null)
        {
            if(ended||definition==null||Game==null||!Game.IsPlaying)return;
            Kills++;BumpCombo();int points=200+Mathf.Min(Combo,15)*25;Score+=points;
            var scoringCar = killer && killer.IsPlayer ? killer : Game.Player;
            float groundHeight=scoringCar&&GeneratedWorld.Active?GeneratedWorld.HeightAt(scoringCar.transform.position):0;
            bool air=scoringCar&&!scoringCar.Grounded&&scoringCar.transform.position.y-groundHeight>2.8f;
            LastAward=air?"AIRBORNE KILL +"+points:Combo>=3?"MULTI-KILL +"+points:"VEHICLE DESTROYED +"+points;
            if(air)UnlockAchievement("airborne","AIRBORNE KILL");
        }
        public void RegisterDestruction(int points,Vector3 position)
        {
            if(ended||definition==null||Game==null||!Game.IsPlaying)return;
            DestructionCount++;BumpCombo();Score+=Mathf.Max(0,points)+Mathf.Min(Combo,15)*5;
            LastAward=Combo>=8?"INSURANCE EVENT • x"+Combo:Combo>=3?"CHAIN REACTION • x"+Combo:"COLLATERAL DAMAGE";
            if(Combo>=8)UnlockAchievement("insurance","INSURANCE EVENT");
        }
        void BumpCombo(){Combo=comboTimeout>0?Combo+1:1;comboTimeout=3.2f;bestCombo=Mathf.Max(bestCombo,Combo);}
        public void RegisterPickup(string id)
        {
            if(ended||definition==null||Game==null||!Game.IsPlaying)return;
            if(id=="wonton"){Score+=750;UnlockAchievement("wonton","WONTON DESTRUCTION");return;}
            pickupCount++;Score+=150;Game.Save.salvage+=2;
            if(id.StartsWith("plate:")||id.StartsWith("workorder:"))
            {if(!Game.Save.collectibles.Contains(id)){Game.Save.collectibles.Add(id);Game.Notify("Filed in the garage archive: "+id.Replace(':',' '));}}
            else Game.Notify("Recovered salvage • +2 materials, +150 score");
            SaveSystem.Save(Game.Save);
        }
        void AddCaches()
        {
            Vector3[] points={new Vector3(-58,.1f,-24),new Vector3(68,.1f,58),new Vector3(-60,.1f,102)};
            if(GeneratedWorld.Active!=null&&GeneratedWorld.Active.Pins.Count>0)
            {
                int count=GeneratedWorld.Active.Pins.Count;
                points=new Vector3[3];
                for(int i=0;i<points.Length;i++)points[i]=GeneratedWorld.Active.Pins[(Mathf.Max(0,missionIndex)*11+i*5+2)%count].position;
            }
            foreach(var raw in points){Vector3 point=TerrainPoint(raw);cachePositions.Add(point);cacheMarkers.Add(Game.World.CreateMarker(point,new Color(.25f,.95f,.8f),"SALVAGE"));}
        }

        static Vector3 TerrainPoint(Vector3 point)
        {
            var generated=GeneratedWorld.Active;if(generated==null)return point;
            Bounds bounds=generated.WorldBounds;
            point.x=Mathf.Clamp(point.x,bounds.min.x+3,bounds.max.x-3);
            point.z=Mathf.Clamp(point.z,bounds.min.z+3,bounds.max.z-3);
            point=generated.ClearOfObstacles(point);
            point.y=GeneratedWorld.HeightAt(point)+.1f;return point;
        }
        void CheckCaches()
        {
            for(int i=0;i<cachePositions.Count;i++)
            {
                if(!cacheMarkers[i]||!Near(cachePositions[i],5))continue;
                Destroy(cacheMarkers[i]);cacheMarkers[i]=null;
                RegisterPickup((i==0?"plate:":i==1?"workorder:":"salvage:")+definition.id+"-"+i);
                if(i==0)DialogueSystem.Instance?.Say("JOHNNY","Suzuki marked a salvage cache. Her inspection rate remains one biscuit per discovery.",6);
            }
        }
        void UpdateOptional(bool final)
        {
            bool satisfied=false;
            switch(definition.optionalKind)
            {
                case OptionalKind.Destruction:satisfied=DestructionCount>=definition.optionalThreshold;break;
                case OptionalKind.Health:satisfied=final&&TeamHealthRatio>=definition.optionalThreshold;break;
                case OptionalKind.Time:satisfied=final&&Elapsed<=definition.optionalThreshold;break;
                case OptionalKind.Salvage:satisfied=pickupCount>=definition.optionalThreshold;break;
                case OptionalKind.Combo:satisfied=bestCombo>=definition.optionalThreshold;break;
                case OptionalKind.Kills:satisfied=Kills>=definition.optionalThreshold;break;
            }
            if(satisfied&&!OptionalComplete){OptionalComplete=true;Score+=800;Game.Notify("OPTIONAL OBJECTIVE COMPLETE • +800 score");AudioManager.Instance?.PlayObjective();}
        }
        void UnlockAchievement(string id,string title)
        {if(missionIndex<0)return;if(!Game.Save.achievements.Contains(id)){Game.Save.achievements.Add(id);Game.Notify("DISTINCTION UNLOCKED: "+title);}}
        void Finish()
        {
            if(ended)return;ended=true;Progress=1;UpdateOptional(true);
            Score+=Mathf.RoundToInt(Remaining*4)+Mathf.RoundToInt(TeamHealthRatio*1000);
            if(missionIndex<0){Debrief="COMBAT TRIAL COMPLETE • "+Score+" points • no campaign progress changed";Game.CompleteMission();return;}
            var save=Game.Save;bool first=!save.completedMissions.Contains(missionIndex);
            AwardedMoney=(first?definition.reward:Mathf.RoundToInt(definition.reward*.3f))+(OptionalComplete?250:0)+Mathf.Min(500,Score/40);
            AwardedSalvage=3+DestructionCount/8+(OptionalComplete?3:0);save.money+=AwardedMoney;save.salvage+=AwardedSalvage;save.reputation+=first?100:20;
            if(first)save.completedMissions.Add(missionIndex);
            save.unlockedMission=Mathf.Min(ContentCatalog.Missions.Length-1,Mathf.Max(save.unlockedMission,missionIndex+1));
            while(save.bestScores.Count<ContentCatalog.Missions.Length)save.bestScores.Add(0);save.bestScores[missionIndex]=Mathf.Max(save.bestScores[missionIndex],Score);
            if(missionIndex==4)UnlockAchievement("dog_director","DIRECTOR OF GOOD DECISIONS");
            if(missionIndex==14)UnlockAchievement("paid_full","PAID IN FULL");
            Debrief=(first?"FIRST COMPLETION":"REPLAY COMPLETE")+" • $"+AwardedMoney+" • "+AwardedSalvage+" salvage • best combo x"+bestCombo;
            DialogueSystem.Instance?.SayLine(definition.closing,22);SaveSystem.Save(save);Game.CompleteMission();
        }
        void Fail(string reason){ended=true;Debrief=reason;Game.Notify(reason);Game.FailMission();}
    }
}
