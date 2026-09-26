using System.Collections.Generic;
using UnityEngine;
using static MadeInArizona.WorldArt;

namespace MadeInArizona
{
    /// <summary>Seeded, Arizona-shaped open world with true mesh terrain and bounded scenery.</summary>
    public sealed partial class GeneratedWorld:MonoBehaviour
    {
        public static GeneratedWorld Active{get;private set;}
        public Bounds WorldBounds{get;private set;}
        public List<WorldPin> Pins{get;private set;}=new List<WorldPin>();
        public List<Vector3> Towns{get;private set;}=new List<Vector3>();
        public Texture2D MapTexture{get;private set;}
        public int Seed=>seed;
        const int Chunks=8,Cells=48;
        static readonly Vector2[] Outline={new Vector2(-.47f,.49f),new Vector2(.45f,.49f),new Vector2(.49f,.18f),new Vector2(.48f,-.48f),new Vector2(-.08f,-.48f),new Vector2(-.47f,-.18f),new Vector2(-.43f,.08f),new Vector2(-.50f,.12f)};
        static readonly Vector2[] TownPlan={new Vector2(-.06f,-.25f),new Vector2(.20f,-.04f),new Vector2(-.20f,.09f),new Vector2(.10f,.27f),new Vector2(-.28f,.31f),new Vector2(.30f,.34f)};
        WorldGenConfig cfg;System.Random rng;int seed,townCount,poiCount;float size,half,amp,riverWidth;
        readonly List<Route> routes=new List<Route>();readonly List<Transform> details=new List<Transform>();readonly Dictionary<long,SurfaceKind> surfaceCache=new Dictionary<long,SurfaceKind>();
        float[,] heights;
        Transform terrainRoot,props,roads;Material desert,high,rock,asphalt,water;
        struct Segment { public Vector2 a,b; public float ha,hb; }
        readonly List<Segment> segments=new List<Segment>();
        struct Route{public Vector2 a,b;public float width;public Route(Vector2 x,Vector2 y,float w){a=x;b=y;width=w;}}

        public void Configure(WorldGenConfig config,Transform parent)
        {
            cfg=config??new WorldGenConfig();seed=cfg.seed;size=Mathf.Clamp(cfg.size,800,3200);half=size*.5f;amp=Mathf.Clamp(cfg.terrainHeight,0,150);riverWidth=Mathf.Clamp(cfg.riverWidth,0,30);townCount=Mathf.Clamp(cfg.townCount,2,TownPlan.Length);poiCount=Mathf.Clamp(cfg.poiCount,4,40);rng=new System.Random(seed);Active=this;
            transform.SetParent(parent,false);WorldBounds=new Bounds(Vector3.up*amp*.25f,new Vector3(size,amp*2.5f,size));
            desert=new Material(Shader.Find("MadeInArizona/BiomeTerrain"));WorldArt.ConfigureBiomeTerrain(desert);high=GroundMaterial(new Color(.42f,.34f,.23f),8);rock=GroundMaterial(new Color(.39f,.22f,.16f),12);asphalt=GroundMaterial(new Color(.10f,.12f,.115f),10);water=new Material(Shader.Find("MadeInArizona/FlowRiver"));water.SetTexture("_BumpMap",WorldArt.SurfaceNormal());
            Plan();BakeRoutes();BuildTerrain();BuildRiver();BuildRoads();BuildTowns();BuildPins();PlanTrails();BuildTrails();BuildEcology();BuildMap();gameObject.AddComponent<RoadPatrolDirector>();
        }
        void Plan(){for(int i=0;i<townCount;i++){Vector2 n=TownPlan[i]+new Vector2(R(-.05f,.05f),R(-.05f,.05f));Vector3 p=new Vector3(n.x*size,0,n.y*size);float bank=RiverX(p.z);if(Mathf.Abs(p.x-bank)<riverWidth+85)p.x=bank+(p.x>=bank?1:-1)*(riverWidth+85);p.y=RawHeight(p.x,p.z);Towns.Add(p);}for(int i=0;i<Towns.Count-1;i++)routes.Add(new Route(XZ(Towns[i]),XZ(Towns[i+1]),14));if(Towns.Count>2)routes.Add(new Route(XZ(Towns[0]),XZ(Towns[2]),11));if(Towns.Count>3)routes.Add(new Route(XZ(Towns[1]),XZ(Towns[3]),10));
            // Elevation levels depend on the planned towns and routes (cliffs keep clear of both), so heights follow.
            PrepareElevation();for(int i=0;i<Towns.Count;i++){Vector3 t=Towns[i];t.y=RawHeight(t.x,t.z);Towns[i]=t;}}
        void BakeRoutes()
        {
            foreach(var route in routes)for(int i=0;i<64;i++)
            {
                Vector2 a=RoutePoint(route,i/64f),b=RoutePoint(route,(i+1)/64f);
                segments.Add(new Segment{a=a,b=b,ha=RawHeight(a.x,a.y),hb=RawHeight(b.x,b.y)});
            }
        }
        void BuildTerrain()
        {
            terrainRoot=Group("Chunked terrain",transform,Vector3.zero);props=Group("Streamed ecology",transform,Vector3.zero);roads=Group("Road and river network",transform,Vector3.zero);float cs=size/Chunks;int grid=Chunks*Cells;float step=size/grid;heights=new float[grid+1,grid+1];
            for(int z=0;z<=grid;z++)for(int x=0;x<=grid;x++)heights[x,z]=HeightInternal(-half+x*step,-half+z*step);
            EnsureReachable(step);
            for(int cz=0;cz<Chunks;cz++)for(int cx=0;cx<Chunks;cx++){float x0=-half+cx*cs,z0=-half+cz*cs;var v=new Vector3[(Cells+1)*(Cells+1)];var uv=new Vector2[v.Length];
                for(int z=0;z<=Cells;z++)for(int x=0;x<=Cells;x++){float wx=x0+x*cs/Cells,wz=z0+z*cs/Cells;int k=z*(Cells+1)+x;v[k]=new Vector3(wx,heights[cx*Cells+x,cz*Cells+z],wz);uv[k]=new Vector2(wx/12,wz/12);}
                var tri=new List<int>();for(int z=0;z<Cells;z++)for(int x=0;x<Cells;x++){int a=z*(Cells+1)+x,b=a+1,c=a+Cells+1,d=c+1;if(Contains((v[a]+v[c]+v[b])/3)){tri.Add(a);tri.Add(c);tri.Add(b);}if(Contains((v[b]+v[c]+v[d])/3)){tri.Add(b);tri.Add(c);tri.Add(d);}}
                var go=new GameObject("Terrain "+cx+"_"+cz,typeof(MeshFilter),typeof(MeshRenderer),typeof(MeshCollider));go.transform.SetParent(terrainRoot,false);var m=new Mesh{name="Arizona terrain chunk",vertices=v,triangles=tri.ToArray(),uv=uv};var normals=new Vector3[v.Length];for(int z=0;z<=Cells;z++)for(int x=0;x<=Cells;x++){int gx=cx*Cells+x,gz=cz*Cells+z;normals[z*(Cells+1)+x]=new Vector3(heights[Mathf.Max(0,gx-1),gz]-heights[Mathf.Min(grid,gx+1),gz],step*2,heights[gx,Mathf.Max(0,gz-1)]-heights[gx,Mathf.Min(grid,gz+1)]).normalized;}m.normals=normals;m.RecalculateTangents();m.RecalculateBounds();go.GetComponent<MeshFilter>().sharedMesh=m;if(tri.Count>0)go.GetComponent<MeshCollider>().sharedMesh=m;go.GetComponent<MeshRenderer>().sharedMaterial=desert;var colors=new Color[v.Length];for(int k=0;k<v.Length;k++)colors[k]=BiomeColor(v[k]);m.colors=colors;go.AddComponent<GeneratedMeshOwner>().Mesh=m;details.Add(Group("Details "+cx+"_"+cz,props,Vector3.zero));
            }
        }
        void BuildRiver()
        {
            if(riverWidth<=0)return;
            int count=Mathf.CeilToInt(size/3)+1;var l=new Vector3[count];var r=new Vector3[count];
            for(int i=0;i<count;i++){float z=Mathf.Lerp(-half*.93f,half*.94f,i/(float)(count-1)),x=RiverX(z);Vector2 tangent=new Vector2(RiverX(z+1)-RiverX(z-1),2).normalized,n=new Vector2(-tangent.y,tangent.x);float w=riverWidth*(.86f+.12f*Mathf.Sin(z*.035f)+.07f*Mathf.Sin(z*.19f+seed)),y=RawHeight(x,z)-3.1f;l[i]=new Vector3(x+n.x*w,y,z+n.y*w);r[i]=new Vector3(x-n.x*w,y,z-n.y*w);}
            Ribbon("Salt River",l,r,water,roads,false);
            for(int i=2;i<count-2;i+=30)for(int side=-1;side<=1;side+=2){Vector3 p=(l[i]+r[i])*.5f;p.x+=side*(riverWidth+R(4,10));p.z+=R(-6,6);p.y=HeightInternal(p.x,p.z);Tree(Detail(p),p,R(5,9));}
        }
        /// <summary>Player spawn and extraction offsets from the starter town, kept clear of buildings.</summary>
        public static readonly Vector3 HomeSpawnOffset=new Vector3(-8,0,-12),HomeExtractionOffset=new Vector3(12,0,-10);
        // Asphalt half-width of the widest route, its gravel shoulder and a margin for awnings and bumpers.
        const float RoadKeepOut=7+2.6f+1.2f;
        readonly List<Quaternion> townFrames=new List<Quaternion>();static readonly float[] LotShift={0,5,-5};readonly List<List<Vector4>> townLots=new List<List<Vector4>>();
        void BuildTowns()
        {
            for(int n=0;n<Towns.Count;n++){Vector3 c=Towns[n];c.y=HeightInternal(c.x,c.z);Towns[n]=c;Transform town=Group(n==0?"Starter town • 117 Junction":"Route town "+(n+1),transform,Vector3.zero);
                // The main street follows a road leaving this town, so storefronts line the road instead of sitting in it.
                Vector2 street=StreetDirection(XZ(c));Quaternion frame=Quaternion.LookRotation(new Vector3(street.x,0,street.y),Vector3.up);townFrames.Add(frame);
                var pad=Box("Compacted town pad",town,c+Vector3.down*.15f,new Vector3(58,.4f,48),new Color(.47f,.37f,.25f),true);pad.transform.localRotation=frame;pad.GetComponent<Renderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;TownApron(town,c,frame,pad.GetComponent<Renderer>());int buildings=n==0?7:4+n%3;var lots=new List<Vector4>();townLots.Add(lots);
                for(int i=0;i<buildings;i++){float side=i%2==0?-1:1,w=R(7,12),d=R(7,13),offset=18+R(0,5);Quaternion facing=frame*Quaternion.Euler(0,side*90,0);
                    // Other routes cross town at their own angles; step lots back from them, or leave the lot empty.
                    for(int attempt=0;attempt<9;attempt++){float lx=side*(offset+attempt/3*6),lz=(i/2-1)*14+LotShift[attempt%3];
                        if(LotTaken(lots,lx,lz,w,d))continue;
                        Vector3 p=c+frame*new Vector3(lx,0,lz);if(!LotClear(p,facing,w,d,n))continue;lots.Add(new Vector4(lx,d,lz,w));p.y=HeightInternal(p.x,p.z);Building(town,p,facing,w,d,i+n);break;}}
                for(int k=0;k<10;k++){float px=k%2==0?-30:30,pz=-22+(k/2)*10;Vector3 q=c+frame*new Vector3(px,0,pz);if(RoadDistance(XZ(q))<RoadKeepOut||LotTaken(lots,px,pz,1.5f,1.5f))continue;q.y=HeightAt(q);if(k%3==0)RoadsideProps.Barrel(town,q,Rust,true);else RoadsideProps.Crate(town,q,1.2f);}
                Text(n==0?"117 JUNCTION":"ARIZONA  "+(n+1),town,c+new Vector3(0,5,19),.55f,Cream,Quaternion.Euler(0,180,0));Vector3 cactus=c+frame*new Vector3(27,0,-18);if(RoadDistance(XZ(cactus))>=RoadKeepOut&&!LotTaken(lots,27,-18,1.5f,1.5f))RoadsideProps.Cactus(town,cactus,1.4f);
            }
            BuildTownDetail();
        }
        /// <summary>Feathered compacted-earth apron replacing the pad's rectangular edge; the pad keeps its collider.</summary>
        void TownApron(Transform town,Vector3 c,Quaternion frame,Renderer pad)
        {
            var blend=Shader.Find("MadeInArizona/TrailBlend");if(!blend)return;
            if(townApron==null)townApron=TrailMaterial(blend,new Color(.49f,.38f,.26f),7,.35f,0,0);
            // Three overlapping strips (main street, cross street, a diagonal yard) form an irregular worn area
            // around the lots instead of a rectangle. A separate stream keeps the town layout itself unchanged.
            var random=new System.Random(unchecked(seed*7919+Towns.IndexOf(c)*104729+17));
            float yard=Rand(random,25,65),shift=Rand(random,-8,8);
            ApronStrip(town,c,frame,0,Vector2.zero,24,30);
            ApronStrip(town,c,frame,90,new Vector2(0,shift),18,34);
            ApronStrip(town,c,frame,yard,new Vector2(Rand(random,-10,10),Rand(random,-10,10)),14,26);
            pad.enabled=false;
        }
        void ApronStrip(Transform town,Vector3 c,Quaternion frame,float angle,Vector2 offset,float halfWidth,float halfLength)
        {
            // Ends are extended past the shader's rounded tip taper so the strip keeps its intended length.
            Quaternion rotation=frame*Quaternion.Euler(0,angle,0);Vector3 forward=rotation*Vector3.forward,center=c+frame*new Vector3(offset.x,0,offset.y);
            float reach=halfLength+halfWidth*.35f;int rows=Mathf.CeilToInt(reach)+1;var line=new Vector2[rows];
            for(int i=0;i<rows;i++)line[i]=XZ(center+forward*Mathf.Lerp(-reach,reach,i/(float)(rows-1)));
            SoilStrip("Town ground apron",line,halfWidth,5f,0,townApron,.025f,town);
        }
        Material townApron;
        /// <summary>Heading of the first route leaving a town, measured a short way out so the curve is respected.</summary>
        Vector2 StreetDirection(Vector2 town)
        {
            foreach(var route in routes)
            {
                if((route.a-town).sqrMagnitude<1){Vector2 d=RoutePoint(route,.04f)-route.a;if(d.sqrMagnitude>.01f)return d.normalized;}
                if((route.b-town).sqrMagnitude<1){Vector2 d=RoutePoint(route,.96f)-route.b;if(d.sqrMagnitude>.01f)return d.normalized;}
            }
            return Vector2.up;
        }
        /// <summary>Frame-local lot overlap. Lots store (x, depth, z, width): width runs along the street, depth away from it.</summary>
        static bool LotTaken(List<Vector4> lots,float x,float z,float w,float d)=>lots.Exists(l=>Mathf.Abs(l.z-z)<(l.w+w)*.5f+1&&Mathf.Abs(l.x-x)<(l.y+d)*.5f+1);
        /// <summary>True when a building footprint, including its front awning, stays off every road and spawn point.</summary>
        bool LotClear(Vector3 at,Quaternion facing,float w,float d,int town)
        {
            for(int x=0;x<=2;x++)for(int z=0;z<=2;z++)
            {
                Vector3 local=new Vector3((x-1)*w*.5f,0,Mathf.Lerp(-d*.5f-1.2f,d*.5f,z*.5f));
                Vector3 p=at+facing*local;
                if(RoadDistance(XZ(p))<RoadKeepOut)return false;
            }
            if(town==0)foreach(Vector3 keep in new[]{HomeSpawnOffset,HomeExtractionOffset})
            {
                Vector3 local=Quaternion.Inverse(facing)*(Towns[0]+keep-at);
                if(Mathf.Abs(local.x)<w*.5f+5&&local.z>-d*.5f-6&&local.z<d*.5f+5)return false;
            }
            return true;
        }
        void Building(Transform parent,Vector3 at,Quaternion facing,float w,float d,int i)
        {
            var p=Group("Town blueprint / business "+i,parent,at);p.localRotation=facing;float h=R(3.2f,5.4f);
            Color c=i%3==0?new Color(.61f,.27f,.15f):i%3==1?new Color(.30f,.42f,.39f):new Color(.72f,.60f,.40f);
            var shell=Box("Stucco roadside business",p,Vector3.up*h*.5f,new Vector3(w,h,d),c,true);ApplyWall(shell,c,(i*5+8)%WallTextureSet.TextureCount,new Vector3(w,h,d));
            Box("Sun bleached roof",p,Vector3.up*(h+.14f),new Vector3(w+1,.28f,d+1),Cream);
            Box("Dark storefront",p,new Vector3(0,h*.48f,-d*.505f),new Vector3(w*.55f,h*.56f,.08f),Ink);
            Box("Shade awning",p,new Vector3(0,h*.78f,-d*.58f),new Vector3(w*.7f,.16f,1.1f),i%2==0?Orange:Turquoise);
            p.gameObject.AddComponent<DestructionSystem>().Configure(260,ExplosionKind.Gasoline,false,150);
        }
        void BuildEcology()
        {
            gameObject.AddComponent<LivingWorldDetail>().Initialize(cfg);
            gameObject.AddComponent<DesertWildlife>();
        }
        public float DistanceToRiver(Vector3 p)=>Mathf.Abs(p.x-RiverX(p.z));
        public float SceneryClearance(Vector3 p)
        {
            float c=Mathf.Min(RoadDistance(XZ(p))-9,TownDistance(XZ(p))-38);
            // Grass may brush a trail edge; larger plants (clearance 3) stand a few metres back.
            c=Mathf.Min(c,TrailEdgeDistance(XZ(p))+.3f);
            if(riverWidth>0)c=Mathf.Min(c,DistanceToRiver(p)-riverWidth-1);
            return c;
        }
        void BuildPins(){for(int i=0;i<Towns.Count;i++)Pins.Add(new WorldPin{id="town_"+i,label=i==0?"117 Junction":"Route Town "+(i+1),kind="town",position=Towns[i],requiredTier=Mathf.Min(3,i),discovered=true});string[] names={"Abandoned mine","Desert overlook","Lost fuel stop","Petroglyph canyon","Radio tower","Mesa camp","Dry lake wreck","Forest lookout"};for(int i=0;i<poiCount;i++){Vector3 p;int guard=0;do{p=RandomPoint();guard++;}while((!Contains(p)||TownDistance(XZ(p))<65||RoadDistance(XZ(p))<25)&&guard<80);if(!Contains(p))continue;p.y=HeightInternal(p.x,p.z);string kind=i%9==0?"hideout":i%7==0?"salvage-tech":i%5==0?"salvage-alloy":i%4==0?"salvage-fuel":"landmark";if(kind=="hideout"&&Vector3.Distance(p,Towns[0])<170)kind="salvage-alloy";var pin=new WorldPin{id="poi_"+i,label=names[i%names.Length],kind=kind,position=p,requiredTier=Mathf.Clamp(Mathf.FloorToInt(Mathf.InverseLerp(-half,half,p.z)*4),0,3)};Pins.Add(pin);POI(p,i,pin.label);}if(cfg.pins!=null)foreach(WorldPin authored in cfg.pins){if(authored==null)continue;Vector3 p=authored.position;if(!Contains(p))continue;p.y=HeightInternal(p.x,p.z);POI(p,Pins.Count,authored.label);Pins.Add(new WorldPin{id=authored.id,label=authored.label,kind=authored.kind,position=p,requiredTier=authored.requiredTier,discovered=authored.discovered});}}
        void POI(Vector3 p,int i,string label){Transform g=Group("POI • "+label,transform,p);if(i%3==0){for(int y=0;y<4;y++)Box("Radio mast",g,new Vector3(0,y*3+1.5f,0),new Vector3(.4f,3,.4f),Cream);Lamp("Beacon",g,new Vector3(0,13,0),Orange,4,10);}else if(i%3==1){Box("Mine portal",g,new Vector3(0,2,0),new Vector3(7,4,1),Ink,true);Box("Mine lintel",g,new Vector3(0,4.2f,0),new Vector3(9,.7f,1.2f),Rust);}else{RoadsideProps.JunkCar(g,Vector3.zero,Rust,R(0,360));RoadsideProps.Crate(g,new Vector3(3,0,1),1.3f);}}
        Color BiomeColor(Vector3 p)
        {
            float n=Mathf.InverseLerp(-half,half,p.z);
            var b=cfg.biomeThresholds;
            Color c=Color.Lerp(new Color(.76f,.62f,.40f),new Color(.64f,.35f,.18f),Mathf.SmoothStep(0,1,Mathf.InverseLerp(b.lowland-.15f,b.lowland+.1f,n)));
            c=Color.Lerp(c,new Color(.25f,.37f,.20f),Mathf.SmoothStep(0,1,Mathf.InverseLerp(b.scrub-.06f,b.scrub+.12f,n)));
            c=Color.Lerp(c,new Color(.46f,.39f,.32f),Mathf.SmoothStep(0,1,Mathf.InverseLerp(b.highland,b.highland+.18f,n)));
            if(riverWidth>0)c=Color.Lerp(new Color(.27f,.40f,.23f),c,Mathf.InverseLerp(riverWidth,riverWidth*4,Mathf.Abs(p.x-RiverX(p.z))));
            return c;
        }
        void BuildMap()
        {
            const int res=256;MapTexture=new Texture2D(res,res,TextureFormat.RGBA32,false){name="Seeded Arizona map",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            var px=new Color[res*res];
            for(int y=0;y<res;y++)for(int x=0;x<res;x++)
            {
                Vector3 p=new Vector3(Mathf.Lerp(-half,half,x/(float)(res-1)),0,Mathf.Lerp(-half,half,y/(float)(res-1)));
                Color c=BiomeColor(p)*Mathf.Lerp(.65f,1.2f,Mathf.InverseLerp(-amp*.3f,amp,RawHeight(p.x,p.z)));c.a=1;
                if(riverWidth>0&&Mathf.Abs(p.x-RiverX(p.z))<riverWidth)c=new Color(.05f,.48f,.57f);
                if(TrailEdgeDistance(XZ(p))<2)c=Color.Lerp(c,new Color(.72f,.55f,.36f),.75f);
                if(RoadDistance(XZ(p))<7)c=new Color(.86f,.68f,.38f);
                if(!Contains(p))c=Color.clear;px[y*res+x]=c;
            }
            MapTexture.SetPixels(px);MapTexture.Apply();
        }
        public static float HeightAt(Vector3 p)=>Active?Active.SampleHeight(p):p.y;
        float SampleHeight(Vector3 p)
        {
            if(heights==null)return HeightInternal(p.x,p.z);
            int grid=Chunks*Cells;float xx=Mathf.Clamp((p.x+half)/size*grid,0,grid-.0001f),zz=Mathf.Clamp((p.z+half)/size*grid,0,grid-.0001f);
            int x=Mathf.FloorToInt(xx),z=Mathf.FloorToInt(zz);float u=xx-x,v=zz-z;
            return u+v<=1?heights[x,z]+(heights[x+1,z]-heights[x,z])*u+(heights[x,z+1]-heights[x,z])*v:heights[x+1,z+1]+(heights[x,z+1]-heights[x+1,z+1])*(1-u)+(heights[x+1,z]-heights[x+1,z+1])*(1-v);
        }
        public static bool Contains(Vector3 p)=>Active&&Active.InOutline(new Vector2(p.x/Active.size,p.z/Active.size));
        public static SurfaceKind SurfaceAt(Vector3 p)
        {
            if(!Active)return SurfaceKind.Dirt;
            if(Active.RoadDistance(XZ(p))<9 || Active.TownDistance(XZ(p))<34)return SurfaceKind.Asphalt;
            if(Active.riverWidth>0&&Mathf.Abs(p.x-Active.RiverX(p.z))<Active.riverWidth)return SurfaceKind.Water;
            if(Active.TrailEdgeDistance(XZ(p))<0)return SurfaceKind.Dirt;
            float n=Mathf.InverseLerp(-Active.half,Active.half,p.z);
            return n<Active.cfg.biomeThresholds.lowland?SurfaceKind.Sand:n>Active.cfg.biomeThresholds.highland?SurfaceKind.Rocks:SurfaceKind.Dirt;
        }
        float HeightInternal(float x,float z)
        {
            float h=RawHeight(x,z);var p=new Vector2(x,z);
            float river=Mathf.Abs(x-RiverX(z));
            if(riverWidth>0&&river<riverWidth*2.2f)h=Mathf.Lerp(RawHeight(RiverX(z),z)-4.5f,h,Mathf.SmoothStep(0,1,river/(riverWidth*2.2f)));
            float roadHeight;float rd=NearestRoad(p,out roadHeight);
            // Terrain is linear between grid vertices, so any triangle touching the road must be fully graded
            // or its raised corners poke through the asphalt. Grade the road, shoulders and one cell diagonal.
            float graded=RoadKeepOut+size/(Chunks*Cells)*1.42f,blend=graded+Mathf.Max(11,size/(Chunks*Cells)*1.5f);
            if(rd<blend)h=Mathf.Lerp(roadHeight,h,Mathf.SmoothStep(0,1,Mathf.InverseLerp(graded,blend,rd)));
            float td=TownDistance(p);
            if(td<65)h=Mathf.Lerp(Towns[TownIndex(p)].y,h,Mathf.SmoothStep(0,1,Mathf.InverseLerp(45,65,td)));
            return h;
        }
        float RawHeight(float x,float z)
        {
            float nx=x/size,nz=z/size;
            float broad=Noise(nx*3.1f+2,nz*3.1f)*.48f+Noise(nx*8.7f-2,nz*8.7f+7)*.18f;
            float north=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.02f,.5f,nz));
            float crags=Mathf.Pow(Noise(nx*19+5,nz*17-3),2.4f)*north;
            // Broad gentle grades read better at gameplay camera height. Elevation levels (and their occasional
            // cliffs) replace the old mesa steps; see GeneratedWorldElevation.
            return ((broad-.35f)*.65f+crags*.28f)*amp+LevelOffset(x,z);
        }
        float Noise(float x,float y)=>Mathf.Clamp01(Mathf.PerlinNoise(x+(seed%10007)*.071f,y-(seed%9973)*.053f));float RiverX(float z)=>-size*.10f+Mathf.Sin(z/size*8.2f+seed*.01f)*size*.055f+Mathf.Sin(z/size*21)*size*.018f;
        Vector2 RoutePoint(Route r,float u){Vector2 p=Vector2.Lerp(r.a,r.b,u),d=(r.b-r.a).normalized,n=new Vector2(-d.y,d.x);return p+n*Mathf.Sin(u*Mathf.PI*2+(r.a.x+r.b.y)*.01f)*size*.016f*Mathf.Sin(u*Mathf.PI);}
        float RoadDistance(Vector2 p){float h;return NearestRoad(p,out h);}
        float RouteHeight(Vector2 p){float h;NearestRoad(p,out h);return h;}
        float NearestRoad(Vector2 p,out float height)
        {
            float best=float.MaxValue;height=0;
            foreach(var segment in segments)
            {
                Vector2 d=segment.b-segment.a;
                float u=Mathf.Clamp01(Vector2.Dot(p-segment.a,d)/Mathf.Max(.01f,d.sqrMagnitude));
                float sq=(segment.a+d*u-p).sqrMagnitude;
                if(sq<best){best=sq;height=Mathf.Lerp(segment.ha,segment.hb,u);}
            }
            return Mathf.Sqrt(best);
        }
        float TownDistance(Vector2 p){float d=float.MaxValue;foreach(var t in Towns)d=Mathf.Min(d,Vector2.Distance(p,XZ(t)));return d;}int TownIndex(Vector2 p){int best=0;float d=float.MaxValue;for(int i=0;i<Towns.Count;i++){float n=(p-XZ(Towns[i])).sqrMagnitude;if(n<d){d=n;best=i;}}return best;}
        bool InOutline(Vector2 p){bool inside=false;for(int i=0,j=Outline.Length-1;i<Outline.Length;j=i++){Vector2 a=Outline[i],b=Outline[j];if((a.y>p.y)!=(b.y>p.y)&&p.x<(b.x-a.x)*(p.y-a.y)/(b.y-a.y)+a.x)inside=!inside;}return inside;}
        Vector3 RandomPoint()=>new Vector3(R(-half*.94f,half*.94f),0,R(-half*.92f,half*.94f));float R(float a,float b)=>Mathf.Lerp(a,b,(float)rng.NextDouble());static Vector2 XZ(Vector3 p)=>new Vector2(p.x,p.z);Transform Detail(Vector3 p){float cs=size/Chunks;int x=Mathf.Clamp(Mathf.FloorToInt((p.x+half)/cs),0,Chunks-1),z=Mathf.Clamp(Mathf.FloorToInt((p.z+half)/cs),0,Chunks-1);return details[z*Chunks+x];}
        static void Ribbon(string name,Vector3[] l,Vector3[] r,Material mat,Transform parent,bool collider){int n=l.Length;var v=new Vector3[n*2];var uv=new Vector2[v.Length];for(int i=0;i<n;i++){v[i*2]=l[i];v[i*2+1]=r[i];uv[i*2]=new Vector2(0,i);uv[i*2+1]=new Vector2(1,i);}var tr=new int[(n-1)*6];for(int i=0;i<n-1;i++){int t=i*6,a=i*2;tr[t]=a;tr[t+1]=a+2;tr[t+2]=a+1;tr[t+3]=a+1;tr[t+4]=a+2;tr[t+5]=a+3;}var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent,false);var m=new Mesh{name=name,vertices=v,triangles=tr,uv=uv};m.RecalculateNormals();m.RecalculateBounds();go.GetComponent<MeshFilter>().sharedMesh=m;go.GetComponent<MeshRenderer>().sharedMaterial=mat;go.GetComponent<MeshRenderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;if(collider)go.AddComponent<MeshCollider>().sharedMesh=m;go.AddComponent<GeneratedMeshOwner>().Mesh=m;}
        void Boulder(Transform p,Vector3 a,float s){var go=Shape("Boulder",p,PrimitiveType.Sphere,a,new Vector3(s*1.3f,s*.7f,s),rock.color);go.transform.rotation=Quaternion.Euler(R(-12,12),R(0,360),R(-10,10));}void Shrub(Transform p,Vector3 a,float s)=>Shape("Creosote",p,PrimitiveType.Sphere,a+Vector3.up*s*.35f,new Vector3(s,s*.65f,s),new Color(.31f,.38f,.18f));void Pine(Transform p,Vector3 a,float h)
        {
            Cylinder("Ponderosa trunk",p,a+Vector3.up*h*.34f,h*.035f,h*.68f,new Color(.25f,.13f,.07f));
            for(int tier=0;tier<3;tier++)
            {
                const int sides=9;var v=new Vector3[sides+1];v[0]=Vector3.up*h*(.78f+tier*.11f);
                float radius=h*(.26f-tier*.055f);
                for(int i=0;i<sides;i++){float angle=i*Mathf.PI*2/sides;v[i+1]=new Vector3(Mathf.Cos(angle)*radius,h*(.35f+tier*.17f),Mathf.Sin(angle)*radius);}
                var tri=new int[sides*3];for(int i=0;i<sides;i++){tri[i*3]=0;tri[i*3+1]=(i+1)%sides+1;tri[i*3+2]=i+1;}
                var crown=MeshObject("Ponderosa layered needles",p,v,tri,new Color(.10f+tier*.018f,.24f+tier*.025f,.14f));crown.transform.localPosition=a;
            }
        }
        void Tree(Transform p,Vector3 a,float h){Cylinder("Cottonwood trunk",p,a+Vector3.up*h*.35f,h*.08f,h*.7f,new Color(.27f,.18f,.1f));Shape("Cottonwood crown",p,PrimitiveType.Sphere,a+Vector3.up*h*.78f,new Vector3(h*.62f,h*.42f,h*.62f),new Color(.25f,.43f,.18f));}void Mesa(Vector3 p,float s,bool crag){Transform g=Group(crag?"Northern crag":"Layered mesa",Detail(p),p);for(int i=0;i<(crag?3:4);i++){float k=1-i/(float)((crag?3:4)+1);Cylinder("Eroded rock tier",g,new Vector3(0,i*s*.16f,0),s*k,s*.22f,i%2==0?new Color(.49f,.26f,.17f):new Color(.58f,.31f,.19f));}}
        void Update(){if(details.Count==0||!Camera.main)return;Vector3 p=Camera.main.transform.position;float cs=size/Chunks,distance=Mathf.Max(360,size*.34f),sq=distance*distance;for(int i=0;i<details.Count;i++){Transform d=details[i];if(!d)continue;int x=i%Chunks,z=i/Chunks;Vector3 center=new Vector3(-half+(x+.5f)*cs,0,-half+(z+.5f)*cs);bool show=(center-p).sqrMagnitude<sq;if(d.gameObject.activeSelf!=show)d.gameObject.SetActive(show);}}
        void OnDisable(){if(Active==this)Active=null;}void OnDestroy(){if(MapTexture)Destroy(MapTexture);if(desert)Destroy(desert);if(water)Destroy(water);if(townScenery)Destroy(townScenery);}
    }
}
