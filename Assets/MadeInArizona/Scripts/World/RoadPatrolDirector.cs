using System.Collections.Generic;
using UnityEngine;
namespace MadeInArizona
{
    /// <summary>Off-screen road squads, separate from fixed mission waves.</summary>
    public sealed class RoadPatrolDirector:MonoBehaviour
    {
        public const int PatrolLimit=2*SpawnManager.EnemyCountMultiplier;
        public const int HostileLimit=7*SpawnManager.EnemyCountMultiplier;
        readonly List<VehicleController> patrols=new List<VehicleController>();
        float nextPatrol;System.Random random;
        public int EncountersSpawned {get;private set;}
        public int LivePatrols {get{patrols.RemoveAll(v=>!v||v.Damage.IsDead);return patrols.Count;}}
        void Start(){random=new System.Random(GeneratedWorld.Active.Seed^91379);nextPatrol=Time.time+35;}
        void Update()
        {
            if (CoopSession.IsRemoteClient) return;
            var game=GameManager.Instance;if(!game||!game.IsPlaying||!game.Player||!GeneratedWorld.Active)return;
            for(int i=patrols.Count-1;i>=0;i--)
            {
                var car=patrols[i];if(!car){patrols.RemoveAt(i);continue;}
                bool nearby = false;
                foreach (var player in VehicleController.Active)
                    if (player && player.IsPlayer && player.Damage && !player.Damage.IsDead &&
                        Vector3.Distance(car.transform.position,player.transform.position)<=240) { nearby = true; break; }
                if(!nearby){Destroy(car.gameObject);patrols.RemoveAt(i);}
            }
            if(Time.time<nextPatrol)return;
            nextPatrol=Time.time+38+(float)random.NextDouble()*32;
            TrySpawnPatrol();
        }
        public bool TrySpawnPatrol()
        {
            if (CoopSession.IsRemoteClient) return false;
            var game=GameManager.Instance;var world=GeneratedWorld.Active;
            if(!game||!game.IsPlaying||!game.Player||!world||LivePatrols>=PatrolLimit)return false;
            if(random==null)random=new System.Random(world.Seed^91379);
            var players = new List<VehicleController>();
            foreach (var car in VehicleController.Active)
                if (car && car.IsPlayer && car.Damage && !car.Damage.IsDead &&
                    Vector3.Distance(car.transform.position,game.World.PlayerSpawn)>=80) players.Add(car);
            if (players.Count == 0) return false;
            int hostiles=0;foreach(var car in VehicleController.Active)if(car&&!car.IsPlayer&&!car.Damage.IsDead)hostiles++;
            if(hostiles>=HostileLimit)return false;
            bool spawned=false;
            for(int i=0;i<SpawnManager.EnemyCountMultiplier&&LivePatrols<PatrolLimit&&hostiles<HostileLimit;i++)
                if(TrySpawnVehicle(world,players.Count==1?players[0]:players[random.Next(players.Count)])){spawned=true;hostiles++;}
            if(spawned)EncountersSpawned++;
            return spawned;
        }
        bool TrySpawnVehicle(GeneratedWorld world,VehicleController focus)
        {
            for(int attempt=0;attempt<18;attempt++)
            {
                float angle=(float)random.NextDouble()*Mathf.PI*2;
                Vector3 desired=focus.transform.position+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*85;
                Vector3 at=world.NearestPatrolRoad(desired);float distance=Vector3.Distance(at,focus.transform.position);
                if(!GeneratedWorld.Contains(at)||distance<58||distance>120)continue;
                if(Camera.main){var view=Camera.main.WorldToViewportPoint(at);if(view.z>0&&view.x>-.1f&&view.x<1.1f&&view.y>-.1f&&view.y<1.1f)continue;}
                bool occupied=false;
                foreach(var car in VehicleController.Active)
                    if(car&&!car.Damage.IsDead&&(car.transform.position-at).sqrMagnitude<64){occupied=true;break;}
                if(occupied)continue;
                EnemyFaction faction=(EnemyFaction)random.Next(0,FactionRules.Count);
                var enemy=SpawnManager.Spawn(at+Vector3.up*1.15f,WorldExploration.CurrentTier>=2?random.Next(0,5):random.Next(0,2),focus,faction);
                if(!enemy)continue;enemy.name=FactionRules.Name(faction)+" road patrol";enemy.transform.SetParent(world.transform,true);patrols.Add(enemy);return true;
            }
            return false;
        }
    }
    public sealed partial class GeneratedWorld
    {
        public Vector3 NearestPatrolRoad(Vector3 point)
        {
            Vector2 p=new Vector2(point.x,point.z),best=p;float sq=float.MaxValue;
            foreach(var segment in segments){Vector2 d=segment.b-segment.a;float u=Mathf.Clamp01(Vector2.Dot(p-segment.a,d)/Mathf.Max(.001f,d.sqrMagnitude));Vector2 q=segment.a+d*u;float ds=(q-p).sqrMagnitude;if(ds<sq){sq=ds;best=q;}}
            Vector3 result=new Vector3(best.x,0,best.y);result.y=HeightAt(result);return result;
        }
    }
}
