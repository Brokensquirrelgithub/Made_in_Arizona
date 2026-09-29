using System.Collections.Generic;
using UnityEngine;
namespace MadeInArizona
{
    public partial class WorldBuilder
    {
        public void BuildGeneratedMission(int missionIndex, WorldGenConfig config)
        {
            Clear(); AtmosphereSystem.Create(transform,false);
            var world=new GameObject("Generated Arizona").AddComponent<GeneratedWorld>();
            world.Configure(config,transform);
            Vector3 home=world.Towns.Count>0?world.Towns[0]:Vector3.zero;
            PlayerSpawn=Ground(home+GeneratedWorld.HomeSpawnOffset,1.05f);
            ExtractionPoint=Ground(home+GeneratedWorld.HomeExtractionOffset,.15f);
            ObjectiveLabel="ARIZONA OPEN ROAD / "+(missionIndex+1).ToString("00");
            // Keep the first mission beats close; later beats invite longer trips. Beats alternate between towns and
            // trail junctions out in open country, and which comes first alternates from one mission to the next.
            Vector3 town1=world.Towns.Count>1?world.Towns[1]:home,town2=world.Towns.Count>2?world.Towns[2]:town1;
            var used=new List<Vector3>();
            Vector3? junction1=Junction(world,(home+town1)*.5f,missionIndex,used),junction2=Junction(world,(town1+town2)*.5f,missionIndex+1,used);
            var beats=new List<Vector3>();
            if(missionIndex%2==0){beats.Add(town1);if(junction1.HasValue)beats.Add(junction1.Value);beats.Add(town2);if(junction2.HasValue)beats.Add(junction2.Value);}
            else{if(junction1.HasValue)beats.Add(junction1.Value);beats.Add(town1);if(junction2.HasValue)beats.Add(junction2.Value);beats.Add(town2);}
            foreach(var beat in beats)ObjectivePoints.Add(Ground(beat,.15f));
            for(int i=0;i<world.Pins.Count && ObjectivePoints.Count<4;i++) if(world.Pins[i].kind!="town") ObjectivePoints.Add(Ground(world.Pins[i].position,.15f));
            for(int i=0;i<world.Pins.Count && EnemySpawns.Count<12;i++) {
                WorldPin pin=world.Pins[(i*5+missionIndex)%world.Pins.Count]; if(pin.kind=="town")continue;
                float a=(i*137.5f+missionIndex*19)*Mathf.Deg2Rad;Vector3 p=pin.position+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*(16+i%3*7);
                if(GeneratedWorld.Contains(p))EnemySpawns.Add(Ground(p,1));
            }
        }
        static Vector3 Ground(Vector3 p,float lift){if(GeneratedWorld.Active)p=GeneratedWorld.Active.ClearOfObstacles(p);p.y=GeneratedWorld.HeightAt(p)+lift;return p;}
        /// <summary>A trail junction clear of towns, among the few nearest a point; the pick rotates with the mission.</summary>
        static Vector3? Junction(GeneratedWorld world,Vector3 near,int pick,List<Vector3> used)
        {
            var candidates=world.TrailJunctions.FindAll(j=>!used.Contains(j)&&world.Towns.TrueForAll(t=>Vector2.Distance(new Vector2(j.x,j.z),new Vector2(t.x,t.z))>110));
            if(candidates.Count==0)return null;
            candidates.Sort((a,b)=>Vector2.Distance(new Vector2(a.x,a.z),new Vector2(near.x,near.z)).CompareTo(Vector2.Distance(new Vector2(b.x,b.z),new Vector2(near.x,near.z))));
            var chosen=candidates[Mathf.Abs(pick)%Mathf.Min(3,candidates.Count)];used.Add(chosen);return chosen;
        }
    }
}
