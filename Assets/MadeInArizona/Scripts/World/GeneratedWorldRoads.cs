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
            foreach(Route route in routes)
            {
                float approximateLength=Vector2.Distance(route.a,route.b)*1.08f;
                int count=Mathf.Clamp(Mathf.CeilToInt(approximateLength/1.5f)+1,24,1800);
                var left=new Vector3[count];var right=new Vector3[count];
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
                    left[i]=RoadPoint(lp,.085f);right[i]=RoadPoint(rp,.085f);
                    leftOuter[i]=RoadPoint(lo,.025f);rightOuter[i]=RoadPoint(ro,.025f);
                    centers[i]=(left[i]+right[i])*.5f+Vector3.up*.025f;
                    if(i>0)distance[i]=distance[i-1]+Vector3.Distance(centers[i-1],centers[i]);
                }

                // Gravel shoulders hide the asphalt seam and make the road settle into the ground.
                Ribbon("Gravel left shoulder",leftOuter,left,high,roads,false);
                Ribbon("Gravel right shoulder",right,rightOuter,high,roads,false);
                Ribbon("Smooth winding county road",left,right,asphalt,roads,true);
                BuildWornCenterMarkings(centers,normals,distance);
                BuildRoadFurniture(route,centers,normals,distance);

                if(riverWidth>0)for(int i=1;i<count;i++)
                {
                    float bank0=centers[i-1].x-RiverX(centers[i-1].z),bank1=centers[i].x-RiverX(centers[i].z);
                    if(bank0*bank1<=0&&Mathf.Abs(bank0-bank1)<riverWidth*3.5f)
                    {Vector3 d=(centers[i]-centers[i-1]).normalized;BuildBridge(centers[i],route,new Vector2(d.x,d.z));break;}
                }
            }
        }

        Vector3 RoadPoint(Vector2 p,float lift)
        {
            Vector3 v=new Vector3(p.x,0,p.y);v.y=HeightAt(v)+lift;return v;
        }

        void BuildWornCenterMarkings(Vector3[] center,Vector2[] normal,float[] distance)
        {
            var vertices=new List<Vector3>();var triangles=new List<int>();var uv=new List<Vector2>();
            const float cycle=11f,dash=4.5f,halfWidth=.075f;
            for(float start=3;start+dash<distance[distance.Length-1]-3;start+=cycle)
            {
                // Missing paint on a few cycles gives the line a sun-faded, repaired-road rhythm.
                if(Mathf.PerlinNoise(start*.037f,seed*.013f)<.22f)continue;
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
                Vector3 p=c+new Vector3(n.x,0,n.y)*side*(route.width*.5f+3.7f);p.y=HeightAt(p);
                Transform g=Group("Weathered roadside marker",roads,p);
                Box("Marker post",g,new Vector3(0,.48f,0),new Vector3(.10f,.96f,.10f),new Color(.62f,.58f,.47f));
                Box("Amber reflector",g,new Vector3(0,.76f,0),new Vector3(.14f,.13f,.035f),new Color(.92f,.56f,.12f));
            }
        }

        void BuildBridge(Vector3 p,Route route,Vector2 tangent)
        {
            Transform g=Group("Concrete river bridge",roads,p);
            g.localRotation=Quaternion.LookRotation(new Vector3(tangent.x,0,tangent.y));
            float len=Mathf.Max(12,riverWidth*4);
            Box("Bridge deck",g,Vector3.down*.16f,new Vector3(route.width+1.2f,.32f,len),new Color(.38f,.36f,.31f),true);
            for(int side=-1;side<=1;side+=2)Box("Bridge rail",g,new Vector3(side*(route.width*.5f+.25f),.46f,0),new Vector3(.18f,.72f,len),Cream,true);
        }
    }
}
