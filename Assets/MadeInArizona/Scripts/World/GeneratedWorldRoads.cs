using System.Collections.Generic;
using UnityEngine;
using static MadeInArizona.WorldArt;

namespace MadeInArizona
{
    public sealed partial class GeneratedWorld
    {
        // Roads are sampled densely enough that long bends read as curves from vehicle height.
        // Each edge follows the already-baked terrain rather than hovering on a center-line height.
        void BuildRoads()
        {
            var blend=Shader.Find("MadeInArizona/TrailBlend");
            var shoulder=TrailMaterial(blend,new Color(.50f,.41f,.30f),6,0,0,0);
            foreach(Route route in routes)
            {
                float approximateLength=Vector2.Distance(route.a,route.b)*1.08f;
                int count=Mathf.Clamp(Mathf.CeilToInt(approximateLength/1.5f)+1,24,1800);
                const int lanes=AsphaltColumns;
                var left=new Vector3[count];var right=new Vector3[count];var surface=new Vector3[count,lanes];
                var leftOuter=new Vector3[count];var rightOuter=new Vector3[count];
                var centers=new Vector3[count];var normals=new Vector2[count];var distance=new float[count];
                float halfRoad=route.width*.5f;

                for(int i=0;i<count;i++)
                {
                    float u=i/(float)(count-1),du=1f/(count-1);
                    Vector2 p=RoutePoint(route,u);
                    Vector2 before=RoutePoint(route,Mathf.Max(0,u-du));
                    Vector2 after=RoutePoint(route,Mathf.Min(1,u+du));
                    Vector2 tangent=(after-before).normalized;
                    Vector2 n=new Vector2(-tangent.y,tangent.x);normals[i]=n;
                    Vector2 lp=p+n*halfRoad,rp=p-n*halfRoad;
                    Vector2 lo=p+n*(halfRoad+2.6f),ro=p-n*(halfRoad+2.6f);
                    // Several columns across the asphalt let it follow the graded terrain instead of bridging
                    // straight from edge to edge, which let terrain creases show through mid-lane.
                    for(int j=0;j<lanes;j++)surface[i,j]=RoadSurfacePoint(Vector2.Lerp(lp,rp,j/(float)(lanes-1)),tangent,n,halfRoad/(lanes-1),.75f);
                    left[i]=surface[i,0];right[i]=surface[i,lanes-1];
                    leftOuter[i]=RoadPoint(lo,.03f);rightOuter[i]=RoadPoint(ro,.03f);
                    centers[i]=surface[i,lanes/2]+Vector3.up*.012f;
                    if(i>0)distance[i]=distance[i-1]+Vector3.Distance(centers[i-1],centers[i]);
                }

                // Gravel shoulders hide the asphalt seam and dissolve into the terrain like the dirt trails.
                // They stop at bridges: draped over the carved channel they would show through the water.
                if(blend)
                {
                    var line=new List<Vector2>();
                    for(int i=0;i<=count;i++)
                    {
                        bool dry=i<count&&!OverRiver(XZ(centers[i]));
                        if(dry){line.Add(XZ(centers[i]));continue;}
                        if(line.Count>3)SoilStrip("Gravel shoulders",line.ToArray(),halfRoad+1.6f,1.2f,halfRoad-.4f,shoulder,.03f,roads);
                        line.Clear();
                    }
                }
                else
                {
                    Ribbon("Gravel left shoulder",leftOuter,left,high,roads,false);
                    Ribbon("Gravel right shoulder",right,rightOuter,high,roads,false);
                }
                Surface("Smooth winding county road",surface,asphalt,roads,true);
                BuildBridges(surface,centers,normals);
                BuildWornCenterMarkings(centers,normals,distance);
                BuildRoadFurniture(route,centers,normals,distance);

            }
        }

        const int AsphaltColumns=5;const float RoadLift=.1f;
        Vector3 RoadPoint(Vector2 p,float lift)
        {
            Vector3 v=new Vector3(p.x,0,p.y);v.y=HeightAt(v)+lift;return v;
        }

        /// <summary>
        /// Asphalt vertex resting on the highest terrain within half a cell of it. Terrain triangles are larger than
        /// the road grid, so a single point sample lets curved ground (notably where town pads blend into the road)
        /// rise between vertices; the envelope keeps the whole road quad above it.
        /// </summary>
        Vector3 RoadSurfacePoint(Vector2 p,Vector2 tangent,Vector2 normal,float across,float along)
        {
            float h=HeightAt(new Vector3(p.x,0,p.y));
            foreach(Vector2 o in new[]{tangent*along,-tangent*along,normal*across,-normal*across})
            {Vector2 q=p+o;h=Mathf.Max(h,HeightAt(new Vector3(q.x,0,q.y)));}
            // Over the river the asphalt keeps the road's graded profile and becomes a bridge deck above the channel.
            if(OverRiver(p))h=Mathf.Max(h,Mathf.Max(GroundHeight(p.x,p.y),RiverLevel(p.y)+1.8f));
            return new Vector3(p.x,h+RoadLift,p.y);
        }

        /// <summary>A road surface grid: rows run along the route, columns run left to right across it.</summary>
        static void Surface(string name,Vector3[,] grid,Material mat,Transform parent,bool collider)
        {
            int rows=grid.GetLength(0),cols=grid.GetLength(1);
            var v=new Vector3[rows*cols];var uv=new Vector2[v.Length];
            for(int i=0;i<rows;i++)for(int j=0;j<cols;j++){v[i*cols+j]=grid[i,j];uv[i*cols+j]=new Vector2(j/(float)(cols-1),i);}
            var tr=new int[(rows-1)*(cols-1)*6];int t=0;
            for(int i=0;i<rows-1;i++)for(int j=0;j<cols-1;j++)
            {int a=i*cols+j,b=a+1,c=a+cols,d=c+1;tr[t++]=a;tr[t++]=c;tr[t++]=b;tr[t++]=b;tr[t++]=c;tr[t++]=d;}
            var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent,false);
            var m=new Mesh{name=name,vertices=v,triangles=tr,uv=uv};m.RecalculateNormals();m.RecalculateBounds();
            go.GetComponent<MeshFilter>().sharedMesh=m;go.GetComponent<MeshRenderer>().sharedMaterial=mat;
            go.GetComponent<MeshRenderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            if(collider)go.AddComponent<MeshCollider>().sharedMesh=m;go.AddComponent<GeneratedMeshOwner>().Mesh=m;
        }

        void BuildWornCenterMarkings(Vector3[] center,Vector2[] normal,float[] distance)
        {
            var vertices=new List<Vector3>();var triangles=new List<int>();var uv=new List<Vector2>();
            const float cycle=11f,dash=4.5f,halfWidth=.075f;
            for(float start=3;start+dash<distance[distance.Length-1]-3;start+=cycle)
            {
                // Missing paint on a few cycles gives the line a sun-faded, repaired-road rhythm.
                if(Mathf.PerlinNoise(start*.037f,SeedOffset(seed,.013,PerlinPeriod))<.22f)continue;
                AddMarkingPoint(start,-halfWidth,center,normal,distance,vertices,uv);
                AddMarkingPoint(start, halfWidth,center,normal,distance,vertices,uv);
                AddMarkingPoint(start+dash,-halfWidth,center,normal,distance,vertices,uv);
                AddMarkingPoint(start+dash, halfWidth,center,normal,distance,vertices,uv);
                int k=vertices.Count-4;triangles.Add(k);triangles.Add(k+1);triangles.Add(k+2);triangles.Add(k+1);triangles.Add(k+3);triangles.Add(k+2);
            }
            if(vertices.Count==0)return;
            GameObject markings=MeshObject("Worn center markings",roads,vertices.ToArray(),triangles.ToArray(),new Color(.78f,.53f,.17f));
            markings.GetComponent<MeshRenderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void AddMarkingPoint(float along,float lateral,Vector3[] center,Vector2[] normal,float[] distance,List<Vector3> vertices,List<Vector2> uv)
        {
            int i=1;while(i<distance.Length-1&&distance[i]<along)i++;
            float t=Mathf.InverseLerp(distance[i-1],distance[i],along);
            Vector3 p=Vector3.Lerp(center[i-1],center[i],t);
            Vector2 n=Vector2.Lerp(normal[i-1],normal[i],t).normalized;
            vertices.Add(p+new Vector3(n.x*lateral,.018f,n.y*lateral));uv.Add(new Vector2(lateral,along));
        }

        void BuildRoadFurniture(Route route,Vector3[] center,Vector2[] normal,float[] distance)
        {
            float total=distance[distance.Length-1];
            for(float along=55;along<total-35;along+=72)
            {
                int i=1;while(i<distance.Length-1&&distance[i]<along)i++;
                float t=Mathf.InverseLerp(distance[i-1],distance[i],along);
                Vector3 c=Vector3.Lerp(center[i-1],center[i],t);Vector2 n=Vector2.Lerp(normal[i-1],normal[i],t).normalized;
                int side=((Mathf.FloorToInt(along/72)+(int)(route.a.x*.1f))&1)==0?-1:1;
                Vector3 p=c+new Vector3(n.x,0,n.y)*side*(route.width*.5f+3.7f);if(OverRiver(XZ(p)))continue;p.y=HeightAt(p);
                Transform g=Group("Weathered roadside marker",roads,p);
                Box("Marker post",g,new Vector3(0,.48f,0),new Vector3(.10f,.96f,.10f),new Color(.62f,.58f,.47f));
                Use(Box("Amber reflector",g,new Vector3(0,.76f,0),new Vector3(.14f,.13f,.035f),new Color(.92f,.56f,.12f)),Reflective(new Color(.92f,.56f,.12f),SunGlint.Sign,.92f,0,.9f));
            }
        }

    }
}
