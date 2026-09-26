using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MadeInArizona
{
    /// <summary>Deterministic clustered ecology, streamed in small independently seeded tiles.</summary>
    public sealed class LivingWorldDetail : MonoBehaviour
    {
        const float Tile=32;
        const int Radius=4;
        readonly Dictionary<Vector2Int,GameObject> tiles=new Dictionary<Vector2Int,GameObject>();
        readonly List<Vector2Int> remove=new List<Vector2Int>();
        Material material;Texture2D needles;PandazoleNatureCatalog nature;
        WorldGenConfig config;
        Vector2Int lastCenter=new Vector2Int(int.MinValue,0);
        const double BuildBudgetMs=2.5;
        readonly SceneryMesh tileMesh=new SceneryMesh(),propMesh=new SceneryMesh();
        IEnumerator building;Vector2Int buildingKey;
        public int LoadedTiles=>tiles.Count;
        public int DetailInstances {get;private set;}
        public int Trees {get;private set;}
        public int Stones {get;private set;}
        public int PlantClumps {get;private set;}
        public void Initialize(WorldGenConfig value)
        {
            config=value;
            nature=PandazoleNatureCatalog.Load();
            material=new Material(Shader.Find("MadeInArizona/LivingScenery")){name="Wind / bark / fractured stone",enableInstancing=true};
            if(nature&&nature.atlas)material.SetTexture("_NeedleAtlas",nature.atlas);
            else {needles=NeedleTexture();material.SetTexture("_NeedleAtlas",needles);}
        }
        void Update()
        {
            if(!GeneratedWorld.Active||!Camera.main||config==null)return;
            var player=GameManager.Instance?GameManager.Instance.Player:null;
            if(player)Shader.SetGlobalVector("_SceneryVehicle",new Vector4(player.transform.position.x,player.transform.position.y,player.transform.position.z,1));
            Vector3 p=Camera.main.transform.position;
            var center=new Vector2Int(Mathf.FloorToInt(p.x/Tile),Mathf.FloorToInt(p.z/Tile));
            FrameTimingProbe.Clock.Restart();
            if(center!=lastCenter)
            {
                remove.Clear();foreach(var pair in tiles)if(Mathf.Abs(pair.Key.x-center.x)>Radius+1||Mathf.Abs(pair.Key.y-center.y)>Radius+1)remove.Add(pair.Key);
                foreach(var key in remove){Destroy(tiles[key]);tiles.Remove(key);}lastCenter=center;
                // A tile unloaded mid-build is abandoned; its root was destroyed above.
                if(building!=null&&!tiles.ContainsKey(buildingKey))building=null;
            }
            // Tiles are generated incrementally under a per-frame budget, nearest first. Whole-tile builds cost
            // 10-18 ms each and previously ran two per frame at every tile crossing, causing a visible hitch.
            // Tiles next to the camera (spawn, teleport, restart) are still completed immediately.
            var clock=System.Diagnostics.Stopwatch.StartNew();
            bool urgent=NextMissing(center,out _,out int nearest)&&nearest<=1;
            while(urgent||clock.Elapsed.TotalMilliseconds<BuildBudgetMs)
            {
                if(building==null)
                {
                    if(!NextMissing(center,out buildingKey,out _))break;
                    var root=new GameObject("Living ecology "+buildingKey.x+" / "+buildingKey.y);root.transform.SetParent(transform,false);root.transform.localPosition=new Vector3(buildingKey.x*Tile,0,buildingKey.y*Tile);
                    tiles.Add(buildingKey,root);building=BuildTile(buildingKey,root);FrameTimingProbe.StreamingTiles++;
                }
                if(!building.MoveNext()){building=null;urgent=NextMissing(center,out _,out nearest)&&nearest<=1;}
            }
            FrameTimingProbe.StreamingMs+=FrameTimingProbe.Clock.Elapsed.TotalMilliseconds;
        }
        bool NextMissing(Vector2Int center,out Vector2Int key,out int ringFound)
        {
            for(int ring=0;ring<=Radius;ring++)for(int z=-ring;z<=ring;z++)for(int x=-ring;x<=ring;x++)
            {
                if(Mathf.Max(Mathf.Abs(x),Mathf.Abs(z))!=ring)continue;
                key=center+new Vector2Int(x,z);
                if(!tiles.ContainsKey(key)){ringFound=ring;return true;}
            }
            key=default;ringFound=-1;return false;
        }
        IEnumerator BuildTile(Vector2Int key,GameObject root)
        {
            Vector3 origin=new Vector3(key.x*Tile,0,key.y*Tile);
            if(!GeneratedWorld.Contains(origin+new Vector3(16,0,16)))yield break;
            var random=new System.Random(unchecked(config.seed*73856093 ^ key.x*19349663 ^ key.y*83492791));
            var mesh=tileMesh;mesh.Clear();
            float density=Mathf.Clamp(config.vegetation,0,4),half=config.size*.5f;
            // A coarse clearance field keeps the thousands of tiny clumps away from drivable road/town surfaces.
            float[,] clearance=new float[9,9];
            for(int z=0;z<9;z++)for(int x=0;x<9;x++)clearance[x,z]=GeneratedWorld.Active.SceneryClearance(origin+new Vector3(x*4,0,z*4));
            int attempts=Mathf.RoundToInt((nature?120:500)*density);
            for(int i=0;i<attempts;i++)
            {
                if((i&31)==31)yield return null;
                float x=Next(random,0,Tile),z=Next(random,0,Tile);int ix=Mathf.Min(7,(int)(x/4)),iz=Mathf.Min(7,(int)(z/4));
                float clear=Mathf.Lerp(Mathf.Lerp(clearance[ix,iz],clearance[ix+1,iz],x/4-ix),Mathf.Lerp(clearance[ix,iz+1],clearance[ix+1,iz+1],x/4-ix),z/4-iz);
                if(clear<.7f)continue;
                Vector3 world=origin+new Vector3(x,0,z);if(!GeneratedWorld.Contains(world))continue;
                world.y=GeneratedWorld.HeightAt(world);Vector3 local=world-origin;
                float north=Mathf.InverseLerp(-half,half,world.z);
                float patch=Mathf.PerlinNoise(world.x*.045f+config.seed*.013f,world.z*.045f);
                bool forest=north>config.biomeThresholds.scrub;
                bool bank=GeneratedWorld.Active.DistanceToRiver(world)<config.riverWidth+13&&config.riverWidth>0;
                float scale=Next(random,.65f,1.35f);
                Color grass=forest?new Color(.28f,.39f,.12f):bank?new Color(.36f,.48f,.19f):new Color(.57f,.48f,.23f);
                if(i%13==0)
                {
                    float s=Next(random,.12f,.55f);if(!mesh.Nature(nature?nature.Pick(nature.rocks,random):null,local,s,random,StoneColor(forest,random)))mesh.Rock(local,new Vector3(s,s*.6f,s*.8f),StoneColor(forest,random),random);Stones++;
                }
                else if(i%41==0)
                {
                    Color c=forest?new Color(.31f,.22f,.12f):new Color(.50f,.36f,.21f);
                    Vector3 end=local+new Vector3(Next(random,-1,1),.12f,Next(random,-1,1));mesh.Tube(local+Vector3.up*.08f,end,.035f,.018f,c,5);DetailInstances++;
                }
                else if(patch>(forest?.28f:.43f)||bank)
                {
                    float h=(bank?.95f:forest?.6f:.42f)*scale;if(!mesh.Nature(nature?nature.Pick(nature.grasses,random):null,local,h,random,grass))mesh.Grass(local,h,grass,random,i%17==0);PlantClumps++;
                    if(i%19==0)mesh.Fern(local,(forest?1.1f:.65f)*scale,forest?new Color(.19f,.34f,.10f):new Color(.35f,.40f,.19f),random);
                }
            }
            // Clustered larger plants establish silhouettes, with open travel lanes between groups.
            for(int i=0;i<Mathf.RoundToInt(27*density)+5;i++)
            {
                yield return null;
                Vector3 world=origin+new Vector3(Next(random,1,31),0,Next(random,1,31));
                float clear=GeneratedWorld.Active.SceneryClearance(world);if(clear<3||!GeneratedWorld.Contains(world))continue;
                world.y=GeneratedWorld.HeightAt(world);Vector3 p=world-origin;
                float north=Mathf.InverseLerp(-half,half,world.z),patch=Mathf.PerlinNoise(world.x*.023f+config.seed*.001f,world.z*.023f);
                bool forest=north>config.biomeThresholds.scrub;
                bool bank=config.riverWidth>0&&GeneratedWorld.Active.DistanceToRiver(world)<config.riverWidth+17;
                if(i<5)
                {
                    float s=Next(random,.6f,2.6f);Color c=StoneColor(forest,random);
                    if(s>1.2f) BreakableRock(root,p-Vector3.up*.16f,s,c,random);
                    else if(!mesh.Nature(nature?nature.Pick(nature.rocks,random):null,p-Vector3.up*.16f,s,random,c))mesh.Rock(p-Vector3.up*.16f,new Vector3(s,s*.72f,s*.82f),c,random);
                    Stones++;
                    for(int j=0;j<4;j++){Vector3 q=p+new Vector3(Next(random,-s,s),.03f,Next(random,-s,s));float qSize=Next(random,.12f,.4f);if(!mesh.Nature(nature?nature.Pick(nature.rocks,random):null,q,qSize,random,c))mesh.Rock(q,Vector3.one*qSize,c,random);}
                }
                else if(density>0&&forest&&patch>.28f&&i%2==0)
                {
                    float h=Next(random,7,15);BreakableTree(root,p,h,true,random);Trees++;
                    // Ground litter under the canopy, in irregular patches rather than uniform distribution.
                    for(int j=0;j<28;j++){Vector3 q=p+new Vector3(Next(random,-2.8f,2.8f),0,Next(random,-2.8f,2.8f));q.y=GeneratedWorld.HeightAt(q+origin);mesh.Leaf(q,.18f,Next(random,0,6.28f),new Color(.35f,.24f,.12f),0);}
                    if(i%6==0)mesh.Tube(p+new Vector3(1,.24f,1),p+new Vector3(3,.5f,5),.23f,.16f,new Color(.27f,.19f,.11f),8);
                }
                else if(density>0&&bank&&i%3==0)
                {float h=Next(random,5,10);BreakableTree(root,p,h,false,random);Trees++;}
                else if(density>0&&north<config.biomeThresholds.lowland&&i%5==0)
                {float h=Next(random,1.8f,4.5f);if(!mesh.Nature(nature?nature.Pick(nature.cacti,random):null,p,h,random,Color.white))mesh.Cactus(p,h,random);}
                else if(density>0)
                {float h=Next(random,.5f,1.4f);Color c=forest?new Color(.24f,.34f,.12f):new Color(.37f,.41f,.19f);if(!mesh.Nature(nature?nature.Pick(nature.bushes,random):null,p,h,random,c))mesh.Bush(p,h,c,random);}
                DetailInstances++;
            }
            yield return null;
            mesh.Build(root,"Batched foliage / stones / deadwood",material);
        }
        void BreakableRock(GameObject tile,Vector3 at,float size,Color color,System.Random random)
        {
            var root=new GameObject("Breakable scenery rock");root.transform.SetParent(tile.transform,false);root.transform.localPosition=at;
            var shape=propMesh;shape.Clear();
            if(!shape.Nature(nature?nature.Pick(nature.rocks,random):null,Vector3.zero,size,random,color))
                shape.Rock(Vector3.zero,new Vector3(size,size*.72f,size*.82f),color,random);
            shape.Build(root,"Fractured stone",material);
            var collider=root.AddComponent<BoxCollider>();collider.center=Vector3.up*size*.2f;collider.size=new Vector3(size*1.7f,size*.75f,size*1.5f);
            WorldArt.MakeBreakable(root.transform,Mathf.Clamp(12+size*6,18,28),false,ExplosionKind.Ammunition,3);
        }
        void BreakableTree(GameObject tile,Vector3 at,float height,bool pine,System.Random random)
        {
            var root=new GameObject(pine?"Breakable ponderosa":"Breakable cottonwood");root.transform.SetParent(tile.transform,false);root.transform.localPosition=at;
            var shape=propMesh;shape.Clear();
            Mesh source=nature?nature.Pick(pine?nature.pines:nature.broadleafTrees,random):null;
            if(!shape.Nature(source,Vector3.zero,height,random,Color.white))
            {
                if(pine)shape.Pine(Vector3.zero,height,random);
                else shape.Cottonwood(Vector3.zero,height,random);
            }
            shape.Build(root,pine?"Ponderosa crown":"Cottonwood crown",material);
            var collider=root.AddComponent<CapsuleCollider>();
            collider.center=Vector3.up*(pine?height*.325f:2f);collider.height=pine?height*.65f:4f;collider.radius=pine?height*.023f:.24f;
            WorldArt.MakeBreakable(root.transform,pine?26:22,false,ExplosionKind.Ammunition,5);
        }
        static Color StoneColor(bool forest,System.Random r)=>Color.Lerp(forest?new Color(.29f,.32f,.27f):new Color(.43f,.29f,.20f),forest?new Color(.49f,.48f,.38f):new Color(.68f,.48f,.31f),(float)r.NextDouble());
        internal static float Next(System.Random r,float a,float b)=>Mathf.Lerp(a,b,(float)r.NextDouble());
        static Texture2D NeedleTexture()
        {
            const int size=128;var texture=new Texture2D(size,size,TextureFormat.RGBA32,true){name="Ponderosa branching needle silhouette",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Trilinear,anisoLevel=4};
            var pixels=new Color[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                Vector2 p=new Vector2(x/(float)(size-1),y/(float)(size-1)*2-1);float distance=Mathf.Abs(p.y)-.017f*(1-p.x);
                for(int k=0;k<17;k++)for(int side=-1;side<=1;side+=2)
                {
                    float t=.04f+k*.052f;Vector2 a=new Vector2(t,0),b=new Vector2(Mathf.Min(.995f,t+.13f),side*Mathf.Pow(1-t,.65f)*(.68f+.13f*Mathf.Sin(k*2.7f)));
                    Vector2 d=b-a;float u=Mathf.Clamp01(Vector2.Dot(p-a,d)/d.sqrMagnitude);
                    distance=Mathf.Min(distance,Vector2.Distance(p,a+d*u)-.018f*(1-u*.5f));
                }
                float alpha=Mathf.Clamp01(.5f-distance*120);
                float tone=.72f+Mathf.Abs(p.y)*.22f+x/(float)size*.1f;
                pixels[y*size+x]=new Color(tone,tone,Mathf.Min(1,tone*.85f),alpha);
            }
            texture.SetPixels(pixels);texture.Apply(true,false);return texture;
        }
        void OnDestroy(){SceneryMesh.ClearNatureCache();if(material)Destroy(material);if(needles)Destroy(needles);}
    }

    /// <summary>Shared mesh batches keep rich procedural geometry to one renderer per ecology tile.</summary>
    internal sealed class SceneryMesh
    {
        sealed class NatureSource
        {
            public Vector3[] vertices,normals;public Vector2[] uvs;public Color[] colors;public int[] triangles;public Bounds bounds;
            public NatureSource(Mesh mesh){vertices=mesh.vertices;normals=mesh.normals;uvs=mesh.uv;colors=mesh.colors;triangles=mesh.triangles;bounds=mesh.bounds;}
        }
        static readonly Dictionary<Mesh,NatureSource> natureCache=new Dictionary<Mesh,NatureSource>();
        readonly List<Vector3> vertices=new List<Vector3>();readonly List<Vector3> normals=new List<Vector3>();readonly List<int> triangles=new List<int>();readonly List<Color> colors=new List<Color>();readonly List<Vector2> uvs=new List<Vector2>();
        public static void ClearNatureCache()=>natureCache.Clear();
        public int Count=>vertices.Count;
        public void Clear(){vertices.Clear();normals.Clear();triangles.Clear();colors.Clear();uvs.Clear();}
        public bool Nature(Mesh source,Vector3 at,float height,System.Random random,Color tint)
        {
            if(!source||source.vertexCount==0||source.bounds.size.y<.001f)return false;
            if(!natureCache.TryGetValue(source,out NatureSource data)){data=new NatureSource(source);natureCache.Add(source,data);}
            float scale=height/data.bounds.size.y,angle=LivingWorldDetail.Next(random,0,Mathf.PI*2),cs=Mathf.Cos(angle),ss=Mathf.Sin(angle);int start=vertices.Count;
            for(int i=0;i<data.vertices.Length;i++)
            {
                Vector3 q=(data.vertices[i]-new Vector3(data.bounds.center.x,data.bounds.min.y,data.bounds.center.z))*scale;
                vertices.Add(at+new Vector3(q.x*cs-q.z*ss,q.y,q.x*ss+q.z*cs));uvs.Add(data.uvs!=null&&data.uvs.Length==data.vertices.Length?data.uvs[i]:new Vector2(-1,-1));
                Vector3 n=data.normals!=null&&data.normals.Length==data.vertices.Length?data.normals[i]:Vector3.up;normals.Add(new Vector3(n.x*cs-n.z*ss,n.y,n.x*ss+n.z*cs).normalized);
                Color c=data.colors!=null&&data.colors.Length==data.vertices.Length?data.colors[i]:Color.white;c*=tint;c.a=height>1.5f?.32f:.75f;colors.Add(c);
            }
            for(int i=0;i<data.triangles.Length;i++)triangles.Add(start+data.triangles[i]);
            return true;
        }
        public void Tri(Vector3 a,Vector3 b,Vector3 c,Color color,float bend=0)
        {int n=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);Vector3 normal=Vector3.Cross(b-a,c-a).normalized;normals.Add(normal);normals.Add(normal);normals.Add(normal);triangles.Add(n);triangles.Add(n+1);triangles.Add(n+2);color.a=bend;colors.Add(color);colors.Add(color);colors.Add(color);uvs.Add(new Vector2(-1,-1));uvs.Add(new Vector2(-1,-1));uvs.Add(new Vector2(-1,-1));}
        public void Tube(Vector3 a,Vector3 b,float ra,float rb,Color color,int sides=7)
        {
            Vector3 d=(b-a).normalized,u=Vector3.Cross(d,Mathf.Abs(d.y)>.9f?Vector3.right:Vector3.up).normalized,v=Vector3.Cross(d,u);
            for(int i=0;i<sides;i++){float t=i*Mathf.PI*2/sides,t2=(i+1)*Mathf.PI*2/sides;Vector3 n=u*Mathf.Cos(t)+v*Mathf.Sin(t),m=u*Mathf.Cos(t2)+v*Mathf.Sin(t2);Color c=color*Mathf.Lerp(.8f,1.15f,(i%3)/2f);Tri(a+n*ra,b+n*rb,b+m*rb,c);Tri(a+n*ra,b+m*rb,a+m*ra,c);Tri(b,b+n*rb,b+m*rb,c);}
        }
        public void Rock(Vector3 at,Vector3 scale,Color color,System.Random r)
        {
            const int sides=9;var rings=new Vector3[4,sides];
            for(int y=0;y<4;y++)for(int i=0;i<sides;i++){float angle=(i+(y%2)*.3f)*Mathf.PI*2/sides;float rad=(y==0?.65f:y==3?.4f:1)*LivingWorldDetail.Next(r,.78f,1.18f);rings[y,i]=at+Vector3.Scale(new Vector3(Mathf.Cos(angle)*rad,y*.35f-.15f,Mathf.Sin(angle)*rad),scale);}
            for(int y=0;y<3;y++)for(int i=0;i<sides;i++){int j=(i+1)%sides;Color c=color*LivingWorldDetail.Next(r,.82f,1.12f);Tri(rings[y,i],rings[y+1,i],rings[y+1,j],c);Tri(rings[y,i],rings[y+1,j],rings[y,j],c);}
            for(int i=0;i<sides;i++)Tri(at+Vector3.up*scale.y,rings[3,(i+1)%sides],rings[3,i],color*1.1f);
        }
        public void Leaf(Vector3 p,float s,float angle,Color c,float bend=1)
        {
            Vector3 d=new Vector3(Mathf.Cos(angle),.3f,Mathf.Sin(angle))*s,w=new Vector3(-d.z,0,d.x)*.37f;
            Tri(p,p+d*.55f+w,p+d*1.4f,c,bend);Tri(p,p+d*1.4f,p+d*.55f-w,c*.91f,bend);
        }
        public void Grass(Vector3 p,float h,Color c,System.Random r,bool flower)
        {
            for(int i=0;i<7;i++)
            {
                float a=LivingWorldDetail.Next(r,0,6.28f),len=h*LivingWorldDetail.Next(r,.5f,1.3f);Vector3 d=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));Vector3 b=p+d*LivingWorldDetail.Next(r,.02f,.22f),mid=b+Vector3.up*len*.6f+d*len*.2f,tip=b+Vector3.up*len+d*len*.45f,w=Vector3.Cross(d,Vector3.up)*len*.075f;
                Tri(b-w,mid+w,mid-w,c*.77f,.25f);Tri(mid-w,mid+w,tip,c,1);
                if(flower&&i<3){Color petal=new Color(.83f,.64f,.24f);for(int k=0;k<4;k++)Leaf(tip,.1f,k*1.57f,petal);}
            }
        }
        public void Fern(Vector3 p,float s,Color c,System.Random r)
        {
            for(int j=0;j<7;j++)
            {
                float a=j*.897f;Vector3 d=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                for(int k=1;k<6;k++){Vector3 q=p+d*s*k*.15f+Vector3.up*(Mathf.Sin(k/6f*Mathf.PI)*s*.45f);float leaf=s*(.22f-k*.025f);Leaf(q,leaf,a+.9f,c,1);Leaf(q,leaf,a-.9f,c*.9f,1);}
            }
        }
        public void Bush(Vector3 p,float s,Color c,System.Random r)
        {
            for(int b=0;b<8;b++)
            {
                float a=LivingWorldDetail.Next(r,0,6.28f);Vector3 end=p+new Vector3(Mathf.Cos(a)*s*.6f,LivingWorldDetail.Next(r,.4f,1.1f)*s,Mathf.Sin(a)*s*.6f);Tube(p,end,.025f,.007f,new Color(.32f,.24f,.12f),4);
                for(int j=0;j<7;j++){Vector3 q=Vector3.Lerp(p,end,.45f+j*.08f);Leaf(q,s*.32f,a+j*2.4f,c*LivingWorldDetail.Next(r,.85f,1.2f));}
            }
        }
        void NeedleFrond(Vector3 p,float span,float angle,Color color)
        {
            Vector3 d=new Vector3(Mathf.Cos(angle),.25f,Mathf.Sin(angle))*span*1.5f,w=new Vector3(-d.z,0,d.x)*.39f;
            for(int plane=0;plane<2;plane++)
            {
                Vector3 side=plane==0?w:w*.65f+Vector3.up*span*.24f;
                Tri(p-side,p+d-side,p+d+side,color,.55f);int n=uvs.Count;uvs[n-3]=new Vector2(0,0);uvs[n-2]=new Vector2(1,0);uvs[n-1]=new Vector2(1,1);
                Tri(p-side,p+d+side,p+side,color,.55f);n=uvs.Count;uvs[n-3]=new Vector2(0,0);uvs[n-2]=new Vector2(1,1);uvs[n-1]=new Vector2(0,1);
            }
        }
        public void Pine(Vector3 p,float h,System.Random r)
        {
            Color bark=new Color(.31f,.19f,.10f);Vector3 lean=new Vector3(LivingWorldDetail.Next(r,-.5f,.5f),h,LivingWorldDetail.Next(r,-.5f,.5f));Tube(p,p+lean,h*.027f,h*.005f,bark,9);
            for(int tier=0;tier<8;tier++)
            {
                float f=.25f+tier*.086f,span=h*(.27f-tier*.025f);Vector3 center=p+lean*f;
                for(int b=0;b<6;b++)
                {
                    float a=b*1.047f+tier*1.9f;Vector3 d=new Vector3(Mathf.Cos(a),-.12f+LivingWorldDetail.Next(r,-.12f,.2f),Mathf.Sin(a)),end=center+d*span;
                    Tube(center,end,.045f,.012f,bark,4);
                    for(int k=1;k<=5;k++)
                    {
                        Vector3 q=Vector3.Lerp(center,end,k/5f);Color c=Color.Lerp(new Color(.08f,.19f,.09f),new Color(.24f,.35f,.14f),tier/8f)*LivingWorldDetail.Next(r,.86f,1.15f);
                        float leaf=span*(.43f-k*.047f);NeedleFrond(q,leaf,a+.85f,c);NeedleFrond(q,leaf,a-.85f,c);NeedleFrond(q,leaf*.8f,a,c);
                    }
                }
            }
            for(int j=0;j<5;j++)NeedleFrond(p+lean*.92f,h*.06f,j*1.256f,new Color(.20f,.31f,.13f));
        }
        public void Cottonwood(Vector3 p,float h,System.Random r)
        {
            Color bark=new Color(.34f,.29f,.18f);Tube(p,p+Vector3.up*h*.72f,h*.035f,h*.009f,bark,8);
            for(int b=0;b<12;b++){float a=b*2.4f;Vector3 end=p+new Vector3(Mathf.Cos(a)*h*.25f,h*LivingWorldDetail.Next(r,.6f,1),Mathf.Sin(a)*h*.25f);Tube(p+Vector3.up*h*.35f,end,.1f,.02f,bark,5);for(int j=0;j<18;j++){Vector3 q=end+new Vector3(LivingWorldDetail.Next(r,-1.1f,1.1f),LivingWorldDetail.Next(r,-.5f,.6f),LivingWorldDetail.Next(r,-1.1f,1.1f));Leaf(q,.65f,j*2.4f,Color.Lerp(new Color(.20f,.34f,.10f),new Color(.48f,.54f,.19f),(float)r.NextDouble()),.7f);}}
        }
        public void Cactus(Vector3 p,float h,System.Random r)
        {
            Color c=new Color(.24f,.38f,.21f);Tube(p,p+Vector3.up*h,h*.085f,h*.055f,c,12);
            for(int i=0;i<2;i++){float side=i==0?-1:1;Vector3 a=p+Vector3.up*h*(.42f+i*.18f),b=a+Vector3.right*side*h*.28f;Tube(a,b,h*.06f,h*.052f,c,9);Tube(b,b+Vector3.up*h*.32f,h*.052f,h*.025f,c,9);}
        }
        public Mesh ToMesh(string name)
        {var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.SetColors(colors);mesh.SetUVs(0,uvs);mesh.SetNormals(normals);mesh.RecalculateBounds();return mesh;}
        public void Build(GameObject root,string name,Material mat)
        {if(Count==0)return;var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(root.transform,false);var mesh=ToMesh(name);go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterial=mat;go.AddComponent<GeneratedMeshOwner>().Mesh=mesh;}
    }
}
