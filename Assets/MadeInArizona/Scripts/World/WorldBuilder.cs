using System;
using System.Collections.Generic;
using UnityEngine;
using static MadeInArizona.WorldArt;

namespace MadeInArizona
{
    public enum SurfaceKind { Asphalt, Dirt, Sand, Gravel, Mud, Water, Rocks, Oil, Debris }

    /// <summary>Deterministic modular desert stages. Combines original procedural scenery with imported licensed asset catalogs.</summary>
    public partial class WorldBuilder : MonoBehaviour
    {
        public Vector3 PlayerSpawn { get; private set; }
        public List<Vector3> ObjectivePoints { get; private set; } = new List<Vector3>();
        public List<Vector3> EnemySpawns { get; private set; } = new List<Vector3>();
        public Vector3 ExtractionPoint { get; private set; }
        public string ObjectiveLabel { get; private set; }
        struct Surface { public Rect bounds; public SurfaceKind kind; }
        static readonly List<Surface> Surfaces=new List<Surface>();
        System.Random random;
        float R(float min,float max) => Mathf.Lerp(min,max,(float)random.NextDouble());
        int quality => GameManager.Instance && GameManager.Instance.Save != null ? GameManager.Instance.Save.settings.quality : 2;

        void Clear()
        {
            // Disable before deferred destruction so old colliders cannot overlap the new stage.
            for(int i=transform.childCount-1;i>=0;i--) { GameObject child=transform.GetChild(i).gameObject; child.SetActive(false); if(Application.isPlaying) Destroy(child); else DestroyImmediate(child); }
            ObjectivePoints.Clear(); EnemySpawns.Clear(); Surfaces.Clear();
        }
        public static SurfaceKind SurfaceAt(Vector3 position)
        {
            if(GeneratedWorld.Active)return GeneratedWorld.SurfaceAt(position);
            for(int i=Surfaces.Count-1;i>=0;i--) if(Surfaces[i].bounds.Contains(new Vector2(position.x,position.z))) return Surfaces[i].kind;
            return SurfaceKind.Dirt;
        }
        void AddSurface(Rect rectangle,SurfaceKind kind) => Surfaces.Add(new Surface {bounds=rectangle,kind=kind});

        public void BuildCombatArena()
        {
            Clear();random=new System.Random(3080);AtmosphereSystem.Create(transform,false);
            PlayerSpawn=new Vector3(0,1,-20);ExtractionPoint=new Vector3(0,.1f,-38);
            ObjectivePoints.Add(new Vector3(0,.1f,5));ObjectivePoints.Add(new Vector3(0,.1f,15));
            ObjectiveLabel="117° PROVING GROUND";
            var arena=Group("Vehicle combat proving ground",transform,Vector3.zero);
            Box("Arena asphalt",arena,new Vector3(0,-.25f,0),new Vector3(108,.5f,92),new Color(.19f,.20f,.19f),true);
            AddSurface(new Rect(-54,-46,108,92),SurfaceKind.Asphalt);
            for(int side=-1;side<=1;side+=2) {
                var sideWall=Box("Concrete perimeter",arena,new Vector3(side*54,1,0),new Vector3(1,2,93),Cream,true);ApplyWall(sideWall,Cream,17,new Vector3(1,2,93));
                var endWall=Box("Concrete perimeter",arena,new Vector3(0,1,side*46),new Vector3(108,2,1),Cream,true);ApplyWall(endWall,Cream,17,new Vector3(108,2,1));
                for(int n=-3;n<=3;n++)Box("Hazard stripe",arena,new Vector3(n*13,.015f,side*41),new Vector3(5,.02f,.35f),Orange);
                for(int n=0;n<3;n++) {
                    Vector3 pos=new Vector3(side*(17+n*9),0,n*13-12);
                    RoadsideProps.Crate(arena,pos,1.8f);
                    if(n==1)RoadsideProps.Propane(arena,pos+Vector3.forward*4,true);
                }
            }
            Text("117° PROVING GROUND",arena,new Vector3(0,.04f,34),1.4f,Cream,Quaternion.Euler(90,0,0));
            Text("MOVE • AIM • BREAK SOMETHING",arena,new Vector3(0,.04f,-36),.7f,Turquoise,Quaternion.Euler(90,0,0));
        }

        public void BuildGarage()
        {
            Clear(); random=new System.Random(117);
            AtmosphereSystem.Create(transform,true);
            PlayerSpawn=new Vector3(0,1.04f,0); ExtractionPoint=Vector3.zero; ObjectiveLabel="117° AUTO CARE";
            Transform shop=Group("117° Auto Care • headquarters",transform,Vector3.zero);
            Box("Workshop foundation",shop,new Vector3(0,-.22f,1),new Vector3(27,.4f,26),new Color(.25f,.27f,.25f),true);
            Box("Concrete floor",shop,new Vector3(0,.012f,1),new Vector3(24,.03f,23),new Color(.36f,.37f,.32f));
            for(int x=-3;x<=3;x++) Box("Expansion joint",shop,new Vector3(x*3.4f,.032f,1),new Vector3(.025f,.015f,23),new Color(.22f,.24f,.21f));
            for(int z=-3;z<=3;z++) Box("Expansion joint",shop,new Vector3(0,.033f,z*3.5f+1),new Vector3(24,.015f,.025f),new Color(.22f,.24f,.21f));
            Color shopWall=new Color(.29f,.34f,.32f);var backWall=Box("Back wall",shop,new Vector3(0,3.7f,12.1f),new Vector3(25,7.4f,.4f),shopWall,true);ApplyWall(backWall,shopWall,13,new Vector3(25,7.4f,.4f));
            var leftWall=Box("Left wall",shop,new Vector3(-12.35f,2.7f,1),new Vector3(.3f,5.4f,22),shopWall,true);ApplyWall(leftWall,shopWall,13,new Vector3(.3f,5.4f,22));
            Color lowWallColor=new Color(.36f,.32f,.24f);var rightWall=Box("Right low wall",shop,new Vector3(12.35f,.7f,1),new Vector3(.3f,1.4f,22),lowWallColor,true);ApplyWall(rightWall,lowWallColor,16,new Vector3(.3f,1.4f,22));
            for(int x=-2;x<=2;x++)
            {
                Box("Steel portal column",shop,new Vector3(x*5.9f,3.8f,11.7f),new Vector3(.19f,7.6f,.3f),Rust);
                for(int y=0;y<7;y++) Box("Corrugated siding ridge",shop,new Vector3(x*5.9f+2.7f,y+.4f,11.85f),new Vector3(5.4f,.038f,.12f),new Color(.41f,.43f,.35f));
            }
            Box("Main lintel",shop,new Vector3(0,7.25f,11.6f),new Vector3(25,.38f,.4f),Ink);
            Box("Neon backing",shop,new Vector3(0,5.8f,11.66f),new Vector3(12.3f,2.8f,.14f),Ink);
            Text("MADE IN ARIZONA",shop,new Vector3(0,6.13f,11.53f),.93f,Cream);
            Text("117° AUTO CARE  /  SINCE THE WARRANTY EXPIRED",shop,new Vector3(0,5.18f,11.50f),.245f,Turquoise);
            Box("Amber neon border",shop,new Vector3(0,7.08f,11.4f),new Vector3(12,.065f,.055f),Orange,false,4);
            Box("Teal neon border",shop,new Vector3(0,4.5f,11.4f),new Vector3(12,.065f,.055f),Turquoise,false,3);
            Lamp("Warm wall bounce",shop,new Vector3(-3,5,9),new Color(1,.43f,.16f),7,13);
            Lamp("Teal wall bounce",shop,new Vector3(5,4.8f,10),Turquoise,7,12);
            Lamp("Service bay lamp",shop,new Vector3(0,7,1),new Color(1,.85f,.56f),9,17);
            for(int side=-1;side<=1;side+=2)
            {
                Box("Lift platform",shop,new Vector3(side*.87f,.88f,0),new Vector3(.52f,.28f,6),Ink,true);
                Box("Platform yellow lip",shop,new Vector3(side*1.14f,1.01f,0),new Vector3(.055f,.07f,6),new Color(.95f,.65f,.1f));
                Wedge("Drive on ramp",shop,new Vector3(side*.87f,.015f,-4.13f),new Vector3(.66f,.87f,2.35f),Ink,true);
                Box("Lift column",shop,new Vector3(side*2.4f,1.8f,1.1f),new Vector3(.45f,3.6f,.48f),Turquoise);
                Box("Lift arm",shop,new Vector3(side*1.55f,.76f,1.1f),new Vector3(1.55f,.18f,.21f),Ink);
                Box("Lift arm",shop,new Vector3(side*1.55f,.76f,-1),new Vector3(1.55f,.18f,.21f),Ink);
                for(int stripe=0;stripe<5;stripe++) Box("Safety floor stripe",shop,new Vector3(side*3.07f,.04f,-2.2f+stripe*1.15f),new Vector3(.38f,.02f,.55f),new Color(.94f,.66f,.16f));
            }
            Text("CUSTOMER DECLINED\nRECOMMENDED SERVICE",shop,new Vector3(0,.047f,-7.05f),.37f,new Color(.71f,.65f,.43f),Quaternion.Euler(90,0,0));
            BuildWorkbench(shop,new Vector3(-7.5f,0,9.1f));
            BuildWorkbench(shop,new Vector3(6.9f,0,9.1f));
            for(int row=0;row<2;row++) for(int col=0;col<3;col++)
            {
                var tire=Cylinder("Take-off tires",shop,new Vector3(-9.5f+col*.85f,.15f+row*.3f,3.9f),.42f,.3f,Ink);
                Cylinder("Rim center",tire.transform,new Vector3(0,1.01f,0),.15f,.02f,new Color(.42f,.4f,.31f));
            }
            Box("Tool chest",shop,new Vector3(-7.4f,.72f,4.5f),new Vector3(2.2f,1.4f,.85f),new Color(.64f,.15f,.08f));
            for(int i=0;i<5;i++) Box("Drawer pull",shop,new Vector3(-7.4f,.23f+i*.24f,4.055f),new Vector3(1.7f,.025f,.035f),Cream);
            Box("Parts shelving frame",shop,new Vector3(9.75f,1.8f,5.4f),new Vector3(3.4f,3.6f,.19f),Ink);
            for(int level=0;level<3;level++)
            {
                Box("Parts shelf",shop,new Vector3(9.75f,.3f+level*1.1f,5),new Vector3(3.5f,.08f,1.4f),Cream);
                for(int i=0;i<3;i++) Box("Labeled parts bin",shop,new Vector3(8.65f+i*1.1f,.62f+level*1.1f,5),new Vector3(.8f,.53f,.8f),i==0?Turquoise:Rust);
            }
            Text("USEFUL / PROBABLY / 10mm",shop,new Vector3(9.8f,3.75f,4.9f),.23f,Cream);
            RoadsideProps.Barrel(shop,new Vector3(-10,0,-1),Rust);
            RoadsideProps.Barrel(shop,new Vector3(-9,0,-.7f),new Color(.18f,.34f,.36f));
            var engine=Group("Engine with questionable provenance",shop,new Vector3(7,.4f,1.9f));
            Box("Engine stand",engine,new Vector3(0,-.1f,0),new Vector3(1.3f,.2f,1.2f),Orange);
            Box("Block",engine,new Vector3(0,.6f,0),new Vector3(.85f,.8f,.75f),new Color(.32f,.35f,.34f));
            for(int i=0;i<3;i++) Cylinder("Bore",engine,new Vector3((i-1)*.26f,1.02f,0),.10f,.04f,Ink);
            for(int i=0;i<3;i++) Beam("Exhaust header",engine,new Vector3(.45f,.83f,(i-1)*.21f),new Vector3(.7f,.2f,(i-1)*.21f),.06f,Rust);
            BuildOfficeCorner(shop);
            Box("Suzuki dog bed",shop,new Vector3(4.7f,.12f,-3.5f),new Vector3(2.15f,.24f,1.65f),new Color(.40f,.24f,.15f));
            Box("Bed cushion",shop,new Vector3(4.7f,.27f,-3.5f),new Vector3(1.86f,.14f,1.35f),new Color(.74f,.47f,.24f));
            Text("SUZUKI • MANAGEMENT",shop,new Vector3(4.7f,.18f,-4.34f),.16f,Cream);
            Cylinder("Water bowl",shop,new Vector3(6,.13f,-4.2f),.27f,.21f,new Color(.45f,.54f,.56f));
            Cylinder("Water",shop,new Vector3(6,.247f,-4.2f),.235f,.015f,Turquoise);
            SuzukiDog.Create(shop,new Vector3(4.5f,.38f,-3.25f),GameManager.Instance?.Save?.dogCosmetic??0,false,new[]{
                // One-time cushion egress stays high until her paws clear the mattress; the loop then remains on the floor.
                new Vector3(5.0f,.30f,-2.3f),new Vector3(6.0f,0,-2.3f),new Vector3(5.3f,0,-8.6f),
                new Vector3(-5.5f,0,-8.6f),new Vector3(-8.0f,0,-4.5f),new Vector3(-7.0f,0,1.8f),
                new Vector3(-5.2f,0,3.3f),new Vector3(-5.0f,0,6.8f),new Vector3(-3.8f,0,7.6f),
                new Vector3(3.8f,0,7.5f),new Vector3(5.2f,0,5.2f),new Vector3(5.0f,0,1.0f),new Vector3(5.0f,0,-2.3f)
            },1);
            int souvenirs=GameManager.Instance?.Save?.completedMissions?.Count??0;
            for(int i=0;i<Mathf.Min(16,souvenirs);i++)
            {
                float x=-10.8f+(i%8)*.67f, y=4.7f+(i/8)*.65f;
                Box("Recovered campaign plate",shop,new Vector3(x,y,11.41f),new Vector3(.55f,.32f,.03f),i%2==0?Cream:Turquoise);
                Text((i+1).ToString("00"),shop,new Vector3(x,y,11.38f),.13f,Ink);
            }
            // Outdoor apron and silhouettes keep the dollhouse garage grounded in the desert.
            Box("Exterior desert",transform,new Vector3(0,-.3f,5),new Vector3(120,.35f,110),Sand,true);
            RoadsideProps.Cactus(transform,new Vector3(-17,0,-8),1.6f,false);
            RoadsideProps.Cactus(transform,new Vector3(19,0,7),2,false);
            RoadsideProps.JunkCar(transform,new Vector3(-18,0,12),Rust,-20);
            for(int i=0;i<8;i++) Mesa(new Vector3(-90+i*25,-2,80),R(10,23),R(14,23),new Color(.48f,.26f,.18f));
            AddSurface(new Rect(-15,-15,30,30),SurfaceKind.Asphalt);
        }

        void BuildWorkbench(Transform parent,Vector3 pos)
        {
            var g=Group("Mechanic workbench",parent,pos);
            Box("Wood top",g,new Vector3(0,1.5f,0),new Vector3(5,.19f,1.45f),new Color(.48f,.30f,.15f));
            for(int x=-1;x<=1;x+=2) Box("Bench leg",g,new Vector3(x*2.16f,.73f,0),new Vector3(.14f,1.45f,1.12f),Ink);
            Box("Pegboard",g,new Vector3(0,2.7f,.7f),new Vector3(5,2,.12f),new Color(.37f,.29f,.18f));
            for(int i=0;i<9;i++)
            {
                float x=-2+i*.5f;
                Beam("Hanging wrench",g,new Vector3(x,2.35f,.59f),new Vector3(x,2.86f+(i%3)*.11f,.59f),.035f,new Color(.57f,.62f,.59f));
                Cylinder("Socket",g,new Vector3(x,1.69f,0),.065f,.17f,new Color(.51f,.55f,.54f));
            }
            Box("Shop radio",g,new Vector3(1.65f,1.9f,.1f),new Vector3(.8f,.5f,.42f),Ink);
            Box("Radio tuner",g,new Vector3(1.65f,2,.11f-.23f),new Vector3(.6f,.09f,.02f),Turquoise,false,1.5f);
            Cylinder("Questionable energy drink",g,new Vector3(-1.2f,1.79f,-.4f),.09f,.4f,Orange);
            Text("117",g,new Vector3(-1.2f,1.79f,-.494f),.085f,Cream);
        }
        void BuildOfficeCorner(Transform shop)
        {
            Box("Mission planning board",shop,new Vector3(-10.9f,3,7.6f),new Vector3(.17f,2.5f,3.6f),new Color(.51f,.37f,.2f));
            for(int i=0;i<7;i++) Box("Customer work order",shop,new Vector3(-10.79f,2.4f+(i%3)*.6f,6.4f+(i/3)*1.1f),new Vector3(.025f,.43f,.72f),Cream);
            Box("Office desk",shop,new Vector3(8,.72f,-6.6f),new Vector3(3,1.45f,1.6f),new Color(.43f,.29f,.15f));
            Box("CRT monitor",shop,new Vector3(8,1.93f,-6.45f),new Vector3(1.03f,.8f,.7f),new Color(.63f,.62f,.49f));
            Box("Green terminal",shop,new Vector3(8,1.97f,-6.81f),new Vector3(.83f,.57f,.025f),new Color(.12f,.46f,.3f),false,1.8f);
            Text("INVOICE\nUNPAID",shop,new Vector3(8,1.97f,-6.835f),.13f,Cream);
            Box("Keyboard",shop,new Vector3(8,1.49f,-7.18f),new Vector3(.95f,.07f,.32f),new Color(.63f,.62f,.49f));
            Text("THE 10mm IS NOT A SHOP SUPPLY",shop,new Vector3(7.9f,3.5f,11.43f),.22f,Cream);
        }

        public void BuildMission(int missionIndex)
        {
            Clear(); AtmosphereSystem.Create(transform,false); random=new System.Random(117173+missionIndex*311);
            PlayerSpawn=new Vector3(0,1,-60); ExtractionPoint=new Vector3(0,.08f,-65);
            ObjectiveLabel="UNPAID INVOICE / UNLIMITED LIABILITY";
            ObjectivePoints.AddRange(new[]{new Vector3(0,.08f,-14),new Vector3(45,.08f,26),new Vector3(-40,.08f,57),new Vector3(0,.08f,98)});
            EnemySpawns.AddRange(new[]{new Vector3(-35,1,-5),new Vector3(35,1,-5),new Vector3(45,1,40),new Vector3(-45,1,40),new Vector3(-25,1,80),new Vector3(25,1,80),new Vector3(0,1,110),new Vector3(65,1,75),new Vector3(-65,1,90),new Vector3(50,1,-45)});
            bool mountain=missionIndex==5||missionIndex==6||missionIndex==10||missionIndex==12;
            bool monsoon=missionIndex==6||missionIndex==13;
            Color earth=mountain?new Color(.48f,.33f,.23f):new Color(.68f,.43f,.25f);
            Box("Arizona ground",transform,new Vector3(0,-.53f,22),new Vector3(270,1,300),earth,true);
            AddSurface(new Rect(-130,-125,260,300),SurfaceKind.Dirt);
            Road(new Vector3(0,0,-112),new Vector3(0,0,159),15,true);
            Road(new Vector3(-119,0,26),new Vector3(119,0,26),14,true,.006f);
            Road(new Vector3(-77,0,-59),new Vector3(-38,0,107),11,false);
            Road(new Vector3(51,0,-60),new Vector3(72,0,108),11,false,.004f);
            Road(new Vector3(-84,0,96),new Vector3(79,0,96),12,false,.008f);
            AddSurface(new Rect(-7.5f,-112,15,271),SurfaceKind.Asphalt);
            AddSurface(new Rect(-119,19,238,14),SurfaceKind.Asphalt);
            // Distinct handling zones are visually legible, with open alternatives around them.
            Patch("Deep sand",new Vector3(29,0,-36),new Vector2(22,28),SurfaceKind.Sand,new Color(.80f,.55f,.31f));
            Patch("Service gravel",new Vector3(-32,0,-30),new Vector2(25,21),SurfaceKind.Gravel,new Color(.47f,.43f,.35f));
            Patch("Drainage mud",new Vector3(-30,0,76),new Vector2(20,15),SurfaceKind.Mud,new Color(.27f,.24f,.15f));
            Patch("Flash flood wash",new Vector3(38,0,75),new Vector2(30,13),SurfaceKind.Water,monsoon?new Color(.25f,.38f,.39f):new Color(.30f,.43f,.41f));
            Patch("Oil slick",new Vector3(30,0,26),new Vector2(8,9),SurfaceKind.Oil,new Color(.095f,.105f,.085f));
            Patch("Scrap ground",new Vector3(65,0,36),new Vector2(28,24),SurfaceKind.Debris,new Color(.37f,.28f,.2f));
            Patch("Rocky wash",new Vector3(-65,0,67),new Vector2(14,35),SurfaceKind.Rocks,new Color(.48f,.36f,.28f));
            BuildTown(missionIndex);
            BuildJunkyard();
            BuildNorthSite(missionIndex);
            BuildTerrain(mountain);
            BuildDiscoveries();
            if(monsoon) BuildWeather(true);
            else if(missionIndex==5||missionIndex==11) BuildWeather(false);
            // Physical bounds sit beyond the visually readable driving map.
            foreach(float side in new[]{-1f,1f}) Box("Distant canyon boundary",transform,new Vector3(side*119,4,25),new Vector3(2,12,258),earth,true);
            Box("Northern canyon boundary",transform,new Vector3(0,4,153),new Vector3(240,12,2),earth,true);
            Box("Southern canyon boundary",transform,new Vector3(0,4,-103),new Vector3(240,12,2),earth,true);
        }

        void Road(Vector3 a,Vector3 b,float width,bool asphalt,float layer=0)
        {
            float distance=Vector3.Distance(a,b); Vector3 direction=(b-a).normalized;
            var root=Group(asphalt?"Cracked county asphalt":"Graded desert route",transform,(a+b)/2);
            root.localRotation=Quaternion.LookRotation(direction);
            // Flat overlays receive scene shadows but never cast shadows onto the ground
            // millimetres below them. Distinct heights keep intersecting routes out of the same depth plane.
            float height=(asphalt?.045f:0f)+layer;
            GroundOverlay("Road shoulder",root,new Vector3(0,height-.012f,0),width+3,distance,new Color(.44f,.35f,.24f),7);
            GroundOverlay("Road surface",root,new Vector3(0,height,0),width,distance,asphalt?new Color(.16f,.185f,.18f):new Color(.59f,.40f,.25f),asphalt?10:3);
            if(!asphalt) return;
            for(int side=-1;side<=1;side+=2)
            {
                // Leave the perpendicular intersection clear of edge stripes and centre dashes.
                float crossing=root.InverseTransformPoint(new Vector3(0,0,26)).z;
                float gap=Mathf.Abs(direction.z)>.5f?8:9;
                for(int segment=0;segment<2;segment++)
                {
                    float start=segment==0?-distance*.5f:crossing+gap;
                    float end=segment==0?crossing-gap:distance*.5f;
                    GroundOverlay("Faded edge paint",root,new Vector3(side*(width*.5f-.38f),height+.012f,(start+end)*.5f),.1f,end-start,new Color(.67f,.63f,.47f));
                }
                for(float z=-distance*.5f+2;z<distance*.5f-1;z+=9)
                {
                    Vector3 world=root.TransformPoint(new Vector3(0,0,z));
                    if(Mathf.Abs(direction.z)>.5f ? Mathf.Abs(world.z-26)<10 : Mathf.Abs(world.x)<11) continue;
                    GroundOverlay("Double yellow dash",root,new Vector3(side*.18f,height+.014f,z),.10f,4.7f,new Color(.87f,.58f,.19f));
                }
            }
            for(int i=0;i<distance/5;i++)
            {
                Vector3 p=new Vector3(R(-width*.46f,width*.46f),height+.016f,R(-distance*.48f,distance*.48f));
                var crack=GroundOverlay("Tar crack",root,p,.035f,R(1,3),new Color(.077f,.09f,.075f));
                crack.transform.localRotation=Quaternion.Euler(0,R(-35,35),0);
            }
        }
        static GameObject GroundOverlay(string name,Transform parent,Vector3 center,float width,float length,Color color,int groundTexture=-1)
        {
            var vertices=new[]{new Vector3(-width*.5f,0,-length*.5f),new Vector3(-width*.5f,0,length*.5f),new Vector3(width*.5f,0,length*.5f),new Vector3(width*.5f,0,-length*.5f)};
            var go=MeshObject(name,parent,vertices,new[]{0,1,2,0,2,3},color);
            go.transform.localPosition=center;
            var mesh=go.GetComponent<MeshFilter>().sharedMesh;
            mesh.uv=new[]{Vector2.zero,new Vector2(0,length/3),new Vector2(width/3,length/3),new Vector2(width/3,0)};
            mesh.RecalculateTangents();
            if(groundTexture>=0)go.GetComponent<Renderer>().sharedMaterial=GroundMaterial(color,groundTexture);
            go.GetComponent<Renderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }
        void Patch(string name,Vector3 center,Vector2 size,SurfaceKind kind,Color color)
        {
            const int count=16; var vertices=new Vector3[count+1]; var triangles=new int[count*3];
            vertices[0]=center+Vector3.up*.075f;
            for(int i=0;i<count;i++)
            {
                float a=i*Mathf.PI*2/count, radius=R(.89f,1.1f);
                vertices[i+1]=center+new Vector3(Mathf.Sin(a)*size.x*.5f*radius,.075f,Mathf.Cos(a)*size.y*.5f*radius);
                triangles[i*3]=0;triangles[i*3+1]=i+1;triangles[i*3+2]=(i+1)%count+1;
            }
            var go=MeshObject(name,transform,vertices,triangles,color);
            go.GetComponent<Renderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            if(kind==SurfaceKind.Water||kind==SurfaceKind.Oil) go.GetComponent<Renderer>().sharedMaterial=Material(color,.28f,.78f);
            else go.GetComponent<Renderer>().sharedMaterial=GroundMaterial(color,kind==SurfaceKind.Sand?3:kind==SurfaceKind.Gravel?8:kind==SurfaceKind.Mud?5:kind==SurfaceKind.Rocks?12:kind==SurfaceKind.Debris?10:1);
            AddSurface(new Rect(center.x-size.x*.44f,center.z-size.y*.44f,size.x*.88f,size.y*.88f),kind);
            if(kind==SurfaceKind.Water) for(int i=0;i<12;i++) Box("Wash ripples",transform,center+new Vector3(R(-size.x*.4f,size.x*.4f),.09f,R(-size.y*.4f,size.y*.4f)),new Vector3(R(.8f,2.8f),.014f,.045f),new Color(.55f,.62f,.51f));
        }

        void BuildTown(int missionIndex)
        {
            string[] businesses={"117° AUTO CARE","SUNBLAST HVAC & FIREARMS","LAST CHANCE TRANSMISSION","AIR CONDITIONING FINANCE"};
            RoadsideProps.Building(transform,new Vector3(-27,0,-37),businesses[missionIndex%businesses.Length],new Color(.65f,.46f,.30f),21,12,5);
            RoadsideProps.Building(transform,new Vector3(29,0,-16),"DUSTY'S COLD DRINKS",new Color(.71f,.55f,.32f),18,11,4.5f);
            RoadsideProps.Building(transform,new Vector3(-34,0,4),"THE OASIS / TAX & TERPENES",new Color(.43f,.52f,.40f),22,13,5);
            RoadsideProps.Sign(transform,new Vector3(-17,0,-63),"117° AUTO CARE","WE CAN EXPLAIN THE EXTRA PARTS",Turquoise,12);
            RoadsideProps.Sign(transform,new Vector3(17,0,-54),"DUSTY'S","COLD DRINKS • HOT LIABILITIES",Rust,8);
            RoadsideProps.Sign(transform,new Vector3(-44,0,-59),"SONORAN MEADOWS","SHADE COMING IN PHASE 4",new Color(.32f,.41f,.29f),12);
            // Roofless pump canopy lets the overhead camera read all explosive props.
            Transform fuel=Group("Questionable fuel island",transform,new Vector3(23,0,2));
            Box("Concrete island",fuel,new Vector3(0,.1f,0),new Vector3(9,.2f,5),new Color(.52f,.50f,.40f));
            for(int side=-1;side<=1;side+=2)
            {
                Transform pump=Group("Destructible gasoline dispenser",fuel,new Vector3(side*2.6f,0,0));
                Box("Pump pedestal",pump,new Vector3(0,.6f,0),new Vector3(.85f,1.2f,.7f),Rust);
                Box("Pump head",pump,new Vector3(0,1.5f,0),new Vector3(1.06f,.72f,.77f),Cream);
                Box("Pump display",pump,new Vector3(0,1.61f,-.396f),new Vector3(.76f,.27f,.02f),Ink);
                Text("$ 4.173",pump,new Vector3(0,1.62f,-.41f),.105f,Turquoise);
                Beam("Hose",pump,new Vector3(.59f,1.8f,0),new Vector3(.65f,.27f,.15f),.033f,Ink);
                BoundsCollider(pump,new Vector3(0,1,0),new Vector3(1.3f,2,.9f)); MakeBreakable(pump,35,true,ExplosionKind.Gasoline,85);
            }
            for(int side=-1;side<=1;side+=2) { Cylinder("Awning support",fuel,new Vector3(side*5,2.7f,1.8f),.13f,5.4f,Cream); Box("Narrow canopy frame",fuel,new Vector3(0,5.2f,1.8f),new Vector3(11,.5f,.55f),Rust); }
            RoadsideProps.Propane(transform,new Vector3(37,0,-6),true);
            RoadsideProps.PortaPotty(transform,new Vector3(40,0,-9));
            RoadsideProps.Barrel(transform,new Vector3(-17,0,-26),Rust,true);
            RoadsideProps.Barrel(transform,new Vector3(-17.8f,0,-24.8f),Rust,true);
            RoadsideProps.Crate(transform,new Vector3(-22,0,-26),1.1f);
            RoadsideProps.JunkCar(transform,new Vector3(-39,0,-23),new Color(.43f,.50f,.36f),90);
            RoadsideProps.JunkCar(transform,new Vector3(43,0,-29),new Color(.65f,.42f,.22f),-15);
            for(int i=0;i<8;i++)
            {
                float z=-83+i*29;
                RoadsideProps.PowerPole(transform,new Vector3(-11.5f,0,z));
                if(i<7)
                {
                    // Sagging wires sit high above the playable lanes.
                    for(int wire=-1;wire<=1;wire++)
                    {
                        Vector3 a=new Vector3(-11.5f+wire*1.22f,8.95f,z), b=a+new Vector3(0,0,29),mid=(a+b)*.5f-Vector3.up*1.15f;
                        Beam("Power line",transform,a,mid,.018f,Ink); Beam("Power line",transform,mid,b,.018f,Ink);
                    }
                }
            }
            for(int i=0;i<6;i++) RoadsideProps.Fence(transform,new Vector3(-56+i*7,0,-47),6);
            Wedge("County approved jump",transform,new Vector3(43,.03f,-41),new Vector3(7,2.5f,10),new Color(.48f,.35f,.23f),true);
            Box("Ramp lip",transform,new Vector3(43,2.6f,-36),new Vector3(7,.08f,.3f),Cream);
            Text("ROAD WORK\nEVENTUALLY",transform,new Vector3(39,1.8f,-46),.3f,Cream);
        }

        void BuildJunkyard()
        {
            RoadsideProps.Sign(transform,new Vector3(53,0,12),"RICK'S CLEAN TITLES","IF THE VIN FITS, IT SHIPS",new Color(.48f,.23f,.14f),14);
            RoadsideProps.Building(transform,new Vector3(79,0,49),"RICK'S OFFICE",new Color(.46f,.33f,.24f),13,10,3.8f);
            for(int i=0;i<7;i++)
            {
                RoadsideProps.Fence(transform,new Vector3(33+i*7,0,5),6.7f);
                RoadsideProps.Fence(transform,new Vector3(33+i*7,0,58),6.7f);
            }
            for(int i=0;i<3;i++) RoadsideProps.Fence(transform,new Vector3(87,0,37+i*7),6.7f,90);
            for(int i=0;i<12;i++)
            {
                // The objective at (45,26) and the asphalt crossing remain fully open.
                float x=57+(i%4)*6.5f, z=i<8?38+(i/4)*6:14;
                Transform junk=RoadsideProps.JunkCar(transform,new Vector3(x,0,z),new Color(R(.29f,.62f),R(.23f,.43f),R(.13f,.30f)),R(-15,15));
                if(i%3==0) RoadsideProps.JunkCar(transform,new Vector3(x+.12f,1.18f,z),Rust,180+R(-12,12));
            }
            RoadsideProps.Propane(transform,new Vector3(42,0,44),true);
            for(int i=0;i<5;i++) RoadsideProps.Propane(transform,new Vector3(46+(i%3)*1.2f,0,43+(i/3)*1.3f));
            RoadsideProps.PortaPotty(transform,new Vector3(48.8f,0,47.5f));
            for(int i=0;i<6;i++) RoadsideProps.Crate(transform,new Vector3(33+(i%3)*1.7f,0,37+(i/3)*1.5f));
            for(int i=0;i<4;i++) RoadsideProps.Barrel(transform,new Vector3(70+i*1.1f,0,7.5f),i%2==0?Orange:Rust,true);
            Transform food=Group("WONTON DESTRUCTION mobile kitchen",transform,new Vector3(62,0,10));
            Box("Wonton truck body",food,new Vector3(0,1.2f,0),new Vector3(2.6f,2.2f,5),new Color(.79f,.56f,.21f));
            Box("Kitchen roof",food,new Vector3(0,2.39f,-.3f),new Vector3(2.8f,.19f,4.8f),Cream);
            Box("Service window",food,new Vector3(1.32f,1.7f,-.3f),new Vector3(.035f,.82f,2.7f),Ink);
            Text("WONTON\nDESTRUCTION",food,new Vector3(0,1.55f,-2.53f),.26f,Ink);
            Text("DUMPLINGS • COLLATERAL",food,new Vector3(0,.61f,-2.53f),.105f,Ink);
            for(int side=-1;side<=1;side+=2) for(int ax=-1;ax<=1;ax+=2) { var wheel=Cylinder("Food truck tire",food,new Vector3(side*1.28f,.43f,ax*1.6f),.43f,.29f,Ink); wheel.transform.localRotation=Quaternion.Euler(0,0,90); }
            // The food gag is a real discovery, registered only by a player-caused lethal hit.
            BoundsCollider(food,new Vector3(0,1.25f,0),new Vector3(2.8f,2.5f,5.1f));
            food.gameObject.AddComponent<WorldDiscovery>(); MakeBreakable(food,70,true,ExplosionKind.Propane,173);
            RoadsideProps.Propane(food,new Vector3(-1.6f,0,-1.7f));
        }

        void BuildNorthSite(int missionIndex)
        {
            RoadsideProps.Building(transform,new Vector3(-42,0,78),"SONORAN HOA ENFORCEMENT",new Color(.64f,.58f,.41f),24,15,7);
            RoadsideProps.Sign(transform,new Vector3(-20,0,52),"SONORAN LIVING","YOUR SHADE. OUR SUBSCRIPTION.",new Color(.26f,.40f,.35f),12);
            for(int side=-1;side<=1;side+=2)
            {
                float x=-41+side*11;
                for(int i=0;i<3;i++)
                {
                    var barrier=Group("HOA concrete barrier",transform,new Vector3(x,0,45+i*5.5f));
                    Box("Barrier foot",barrier,new Vector3(0,.18f,0),new Vector3(1.25f,.36f,3.3f),new Color(.53f,.51f,.42f));
                    Box("Jersey barrier",barrier,new Vector3(0,.62f,0),new Vector3(.72f,.88f,3.2f),new Color(.65f,.61f,.48f));
                    Box("Violation stripe",barrier,new Vector3(side*.38f,.76f,0),new Vector3(.04f,.23f,3),Orange);
                    BoundsCollider(barrier,new Vector3(0,.5f,0),new Vector3(1.3f,1.15f,3.3f)); MakeBreakable(barrier,32,false,ExplosionKind.Electrical,25);
                }
            }
            RoadsideProps.Propane(transform,new Vector3(-25,0,65),true);
            RoadsideProps.Barrel(transform,new Vector3(-21,0,66),Orange,true);
            RoadsideProps.Barrel(transform,new Vector3(-22,0,67),Orange,true);
            RoadsideProps.Crate(transform,new Vector3(-53,0,67),1.4f);
            RoadsideProps.PortaPotty(transform,new Vector3(-58,0,77));
            RoadsideProps.Building(transform,new Vector3(25,0,119),missionIndex>=12?"SUNSURE SHADE CONTROL":"SUNSURE LOGISTICS",new Color(.44f,.48f,.43f),31,16,8);
            RoadsideProps.Building(transform,new Vector3(-27,0,120),"ASSET RECOVERY / NO REFUNDS",new Color(.59f,.43f,.30f),23,16,6);
            for(int i=0;i<3;i++)
            {
                Transform fuel=RoadsideProps.Propane(transform,new Vector3(43,0,100+i*5),true);
                fuel.localRotation=Quaternion.Euler(0,90,0);
            }
            for(int i=0;i<6;i++) RoadsideProps.Crate(transform,new Vector3(-20+(i%3)*2,0,103+(i/3)*2),1.25f);
            RoadsideProps.Sign(transform,new Vector3(17,0,83),"SUNSURE","THE FUTURE HAS A MONTHLY FEE",new Color(.21f,.38f,.37f),11);
            if(missionIndex==7||missionIndex==14)
            {
                Transform stage=Group("Rave to the bottom",transform,new Vector3(-67,0,39));
                Box("Stage deck",stage,new Vector3(0,.65f,0),new Vector3(11,1.3f,6),Ink,true);
                for(int side=-1;side<=1;side+=2)
                {
                    for(int i=0;i<3;i++)
                    {
                        Box("Sound stack",stage,new Vector3(side*4,1.5f+i*.85f,0),new Vector3(1.4f,.8f,1.1f),Ink);
                        Shape("Speaker cone",stage,PrimitiveType.Cylinder,new Vector3(side*4,1.5f+i*.85f,-.57f),new Vector3(.52f,.025f,.52f),new Color(.28f,.30f,.28f)).transform.localRotation=Quaternion.Euler(90,0,0);
                    }
                    Box("Rave light",stage,new Vector3(side*4,4.25f,0),Vector3.one*.35f,side>0?Turquoise:Orange,false,6);
                    Lamp("Beat light",stage,new Vector3(side*4,4.4f,0),side>0?Turquoise:Orange,9,16);
                }
                Text("REVOKE THE QUIET HOURS",stage,new Vector3(0,3.4f,.15f),.45f,Cream);
            }
            if(missionIndex==8||missionIndex==11)
            {
                // A second elevated carriageway frames the highway battle without blocking objectives.
                Box("Freeway bridge deck",transform,new Vector3(0,9.2f,135),new Vector3(220,.8f,14),new Color(.46f,.45f,.39f),true);
                for(int i=-4;i<=4;i++) Box("Freeway pier",transform,new Vector3(i*24,4.4f,135),new Vector3(2.5f,9,5),new Color(.49f,.46f,.37f),true);
                for(int side=-1;side<=1;side+=2) Box("Freeway parapet",transform,new Vector3(0,10.2f,135+side*7),new Vector3(220,1.4f,.45f),Cream);
                RoadsideProps.Sign(transform,new Vector3(-13,0,128),"EXIT 173","NO THROUGH ACCOUNTABILITY",new Color(.12f,.37f,.27f),11);
            }
        }

        void BuildTerrain(bool mountain)
        {
            int cactusCount=new[]{38,65,92,130}[Mathf.Clamp(quality,0,3)];
            for(int i=0;i<cactusCount;i++)
            {
                Vector3 p=new Vector3(R(-110,110),0,R(-90,139));
                if(!OpenSceneryPosition(p)) continue;
                if(mountain&&i%2==0) Pine(p,R(3,8));
                else RoadsideProps.Cactus(transform,p,R(.65f,1.45f));
            }
            for(int i=0;i<65;i++)
            {
                Vector3 p=new Vector3(R(-112,112),0,R(-90,142));
                if(!OpenSceneryPosition(p)) continue;
                RoadsideProps.Rock(transform,p+Vector3.up*.24f,new Vector3(R(.8f,2.6f),R(.6f,1.3f),R(.9f,2.4f)),R(-.04f,.07f));
            }
            for(int side=-1;side<=1;side+=2) for(int i=0;i<8;i++) Mesa(new Vector3(side*R(132,160),-2,-96+i*36),R(13,26),R(10,30),new Color(.52f,.28f,.18f));
            for(int i=0;i<9;i++) Mesa(new Vector3(-130+i*33,-2,R(170,186)),R(18,30),R(20,43),new Color(.48f,.27f,.20f));
            for(int i=0;i<7;i++) Mesa(new Vector3(-130+i*45,-2,220),R(26,40),R(35,57),new Color(.36f,.28f,.28f));
            // A low ring of layered outcrops frames the roads and gives the landscape a silhouette.
            Mesa(new Vector3(-88,0,-3),11,8,new Color(.59f,.33f,.19f));
            Mesa(new Vector3(96,0,83),12,9,new Color(.58f,.32f,.2f));
            Mesa(new Vector3(-86,0,119),13,13,new Color(.57f,.32f,.2f));
            for(int i=0;i<26;i++)
            {
                Vector3 p=new Vector3(R(-107,107),.1f,R(-86,137)); if(!OpenSceneryPosition(p)) continue;
                Transform grass=Group("Dry scrub",transform,p);
                for(int blade=0;blade<4;blade++) Beam("Brittle branch",grass,Vector3.zero,new Vector3(R(-.48f,.48f),R(.3f,.75f),R(-.48f,.48f)),.023f,new Color(.41f,.35f,.16f));
            }
        }
        bool OpenSceneryPosition(Vector3 p)
        {
            if(Mathf.Abs(p.x)<13||Mathf.Abs(p.z-26)<12||Mathf.Abs(p.z-96)<9) return false;
            if(p.x>-66&&p.x<91&&p.z>-64&&p.z<62) return false;
            if(p.x>-63&&p.x<-16&&p.z>39&&p.z<88) return false;
            if(p.z>98&&p.x>-45&&p.x<50) return false;
            if(Vector3.Distance(p,new Vector3(-58,0,-24))<8||Vector3.Distance(p,new Vector3(68,0,58))<8||Vector3.Distance(p,new Vector3(-60,0,102))<8) return false;
            if(Mathf.Abs(p.x-(-77+(p.z+59)/166*39))<8) return false;
            if(Mathf.Abs(p.x-(51+(p.z+60)/168*21))<8) return false;
            return true;
        }
        public Transform Mesa(Vector3 pos,float radius,float height,Color color)
        {
            Transform g=Group("Layered Sonoran mesa",transform,pos);
            const int segments=9;
            float[] heights={0,.12f,.34f,.40f,.70f,.76f,1};
            float[] radii={1,.91f,.72f,.75f,.56f,.58f,.40f};
            for(int layer=0;layer<heights.Length-1;layer++)
            {
                List<Vector3> vertices=new List<Vector3>(); List<int> tris=new List<int>();
                for(int i=0;i<segments;i++)
                {
                    float a=i*Mathf.PI*2/segments,b=(i+1)*Mathf.PI*2/segments;
                    float wobble=1+.1f*Mathf.Sin(i*2.3f+pos.x);
                    int n=vertices.Count;
                    vertices.Add(new Vector3(Mathf.Sin(a)*radius*radii[layer]*wobble,height*heights[layer],Mathf.Cos(a)*radius*radii[layer]*wobble));
                    vertices.Add(new Vector3(Mathf.Sin(b)*radius*radii[layer],height*heights[layer],Mathf.Cos(b)*radius*radii[layer]));
                    vertices.Add(new Vector3(Mathf.Sin(a)*radius*radii[layer+1]*wobble,height*heights[layer+1],Mathf.Cos(a)*radius*radii[layer+1]*wobble));
                    vertices.Add(new Vector3(Mathf.Sin(b)*radius*radii[layer+1],height*heights[layer+1],Mathf.Cos(b)*radius*radii[layer+1]));
                    tris.AddRange(new[]{n,n+1,n+2,n+2,n+1,n+3});
                }
                Color c=Color.Lerp(color,Cream,layer%2==0?.03f:.19f);
                MeshObject("Sandstone sediment layer",g,vertices.ToArray(),tris.ToArray(),c);
            }
            Vector3[] top=new Vector3[segments+1]; int[] topTris=new int[segments*3]; top[0]=new Vector3(0,height,0);
            for(int i=0;i<segments;i++) { float a=i*Mathf.PI*2/segments; top[i+1]=new Vector3(Mathf.Sin(a)*radius*.4f,height,Mathf.Cos(a)*radius*.4f); topTris[i*3]=0;topTris[i*3+1]=i+1;topTris[i*3+2]=(i+1)%segments+1; }
            MeshObject("Mesa sunlit plateau",g,top,topTris,Color.Lerp(color,Cream,.22f));
            if(Mathf.Abs(pos.x)<120&&pos.z<153) { CapsuleCollider c=g.gameObject.AddComponent<CapsuleCollider>(); c.center=new Vector3(0,height*.4f,0);c.radius=radius*.77f;c.height=height*.9f; }
            return g;
        }
        void Pine(Vector3 p,float height)
        {
            Transform g=Group("High country pine",transform,p);
            Cylinder("Pine trunk",g,new Vector3(0,height*.36f,0),.14f,height*.72f,new Color(.28f,.21f,.14f));
            for(int i=0;i<3;i++)
            {
                float y=height*(.36f+i*.2f),r=height*(.25f-i*.045f);
                Vector3[] v={new Vector3(0,y+height*.34f,0),new Vector3(-r,y,-r),new Vector3(r,y,-r),new Vector3(r,y,r),new Vector3(-r,y,r)};
                MeshObject("Pine crown",g,v,new[]{0,2,1,0,3,2,0,4,3,0,1,4},new Color(.16f+i*.025f,.24f+i*.025f,.15f));
            }
            BoundsCollider(g,new Vector3(0,height*.5f,0),new Vector3(.35f,height,.35f)); MakeBreakable(g,38,false,ExplosionKind.Gasoline,8);
        }
        void BuildDiscoveries()
        {
            RoadsideProps.Sign(transform,new Vector3(-73,0,-75),"MONUMENT TO THE 10mm","LAST SEEN: UNDER THE INTAKE",new Color(.39f,.31f,.20f),12);
            Cylinder("Socket monument plinth",transform,new Vector3(-75,.6f,-67),2.5f,1.2f,new Color(.48f,.44f,.35f),true);
            Cylinder("The missing socket",transform,new Vector3(-75,2.6f,-67),.72f,3.1f,new Color(.55f,.59f,.56f),true);
            Cylinder("Socket opening",transform,new Vector3(-75,4.16f,-67),.48f,.03f,Ink);
            RoadsideProps.Sign(transform,new Vector3(89,0,-48),"LOTTERY OF COOLANT","WE HAVE YOUR SHADE OF GREEN",new Color(.31f,.43f,.22f),11);
            Wedge("Hidden wash jump",transform,new Vector3(-69,.02f,77),new Vector3(7,3.5f,13),new Color(.50f,.34f,.22f),true);
            RoadsideProps.JunkCar(transform,new Vector3(-98,0,66),new Color(.26f,.52f,.49f),25);
            RoadsideProps.Sign(transform,new Vector3(-77,0,105),"CUSTOMER STATES","ONLY VIBRATES ABOVE MACH 1",Rust,11);
        }
        void BuildWeather(bool rain)
        {
            var g=Group(rain?"Monsoon rain curtain":"Dust storm",transform,new Vector3(0,rain?18:3,25));
            ParticleSystem system=g.gameObject.AddComponent<ParticleSystem>();
            var main=system.main; main.loop=true;main.playOnAwake=false;main.startLifetime=rain?2.5f:8;main.startSpeed=0;main.startSize=rain?.06f:1.8f;main.maxParticles=quality==0?250:quality==3?4000:1500;main.simulationSpace=ParticleSystemSimulationSpace.World;
            main.startColor=rain?new Color(.63f,.74f,.71f,.42f):new Color(.79f,.53f,.3f,.09f);
            var emission=system.emission;emission.rateOverTime=quality==0?80:quality==3?950:330;
            var shape=system.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(210,rain?5:4,240);
            var velocity=system.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.World;velocity.x=rain?-2:5;velocity.y=rain?-12:.2f;velocity.z=rain?1:1.8f;
            var renderer=system.GetComponent<ParticleSystemRenderer>();renderer.renderMode=rain?ParticleSystemRenderMode.Stretch:ParticleSystemRenderMode.Billboard;renderer.lengthScale=rain?7:1;renderer.velocityScale=rain?.03f:0;
            Shader shader=Shader.Find("Universal Render Pipeline/Particles/Unlit")??Shader.Find("Particles/Standard Unlit");
            if(shader)
            {
                Material m=new Material(shader);m.SetFloat("_Surface",1);m.SetFloat("_Blend",0);m.SetFloat("_SrcBlend",5);m.SetFloat("_DstBlend",10);m.SetFloat("_ZWrite",0);m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");m.renderQueue=3000;renderer.sharedMaterial=m;
            }
            system.Play();
        }

        public GameObject CreateMarker(Vector3 position,Color color,string label)
        {
            Transform g=Group("Waypoint • " + label,transform,position);
            const int steps=64; Vector3[] v=new Vector3[steps*2];int[] tris=new int[steps*6];
            for(int i=0;i<steps;i++)
            {
                float a=i*Mathf.PI*2/steps;
                v[i*2]=new Vector3(Mathf.Sin(a)*2.1f,.08f,Mathf.Cos(a)*2.1f);v[i*2+1]=new Vector3(Mathf.Sin(a)*2.28f,.08f,Mathf.Cos(a)*2.28f);
                int n=(i+1)%steps;int t=i*6;tris[t]=i*2;tris[t+1]=n*2;tris[t+2]=i*2+1;tris[t+3]=i*2+1;tris[t+4]=n*2;tris[t+5]=n*2+1;
            }
            var ring=MeshObject("Navigation ring",g,v,tris,color);ring.GetComponent<Renderer>().sharedMaterial=Material(color,0,.3f,2.2f);
            for(int i=0;i<4;i++)
            {
                float a=i*Mathf.PI*.5f;
                var arrow=Box("Waypoint tick",g,new Vector3(Mathf.Sin(a)*2.9f,.11f,Mathf.Cos(a)*2.9f),new Vector3(.22f,.04f,.65f),color,false,1.8f);arrow.transform.localRotation=Quaternion.Euler(0,i*90,0);
            }
            if(!string.IsNullOrEmpty(label)) Text(label,g,new Vector3(0,3.2f,0),.36f,color,Quaternion.Euler(45,0,0)).AddComponent<WorldBillboard>();
            return g.gameObject;
        }
    }
}
