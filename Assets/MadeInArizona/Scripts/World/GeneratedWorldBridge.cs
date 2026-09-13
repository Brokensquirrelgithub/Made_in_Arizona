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
            PlayerSpawn=Ground(home+new Vector3(-8,0,-12),1.05f);
            ExtractionPoint=Ground(home+new Vector3(12,0,-10),.15f);
            ObjectiveLabel="ARIZONA OPEN ROAD / "+(missionIndex+1).ToString("00");
            // Keep the first mission beats close; later beats invite longer trips.
            for(int i=1;i<world.Towns.Count && ObjectivePoints.Count<3;i++) ObjectivePoints.Add(Ground(world.Towns[i],.15f));
            for(int i=0;i<world.Pins.Count && ObjectivePoints.Count<4;i++) if(world.Pins[i].kind!="town") ObjectivePoints.Add(Ground(world.Pins[i].position,.15f));
            for(int i=0;i<world.Pins.Count && EnemySpawns.Count<12;i++) {
                WorldPin pin=world.Pins[(i*5+missionIndex)%world.Pins.Count]; if(pin.kind=="town")continue;
                float a=(i*137.5f+missionIndex*19)*Mathf.Deg2Rad;Vector3 p=pin.position+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*(16+i%3*7);
                if(GeneratedWorld.Contains(p))EnemySpawns.Add(Ground(p,1));
            }
        }
        static Vector3 Ground(Vector3 p,float lift){p.y=GeneratedWorld.HeightAt(p)+lift;return p;}
    }
}
