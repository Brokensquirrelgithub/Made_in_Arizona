using System;
using UnityEngine;

namespace MadeInArizona
{
    public sealed partial class GeneratedWorld
    {
        Material townScenery;
        void BuildTownDetail()
        {
            townScenery=new Material(Shader.Find("MadeInArizona/LivingScenery"));
            int townIndex=0;
            foreach(Transform town in transform)
            {
                if(!town.name.StartsWith("Starter town")&&!town.name.StartsWith("Route town"))continue;
                var mesh=new SceneryMesh();
                var random=new System.Random(unchecked(seed*486187739+townIndex*104729));
                int businessIndex=0;
                foreach(Transform business in town)
                {
                    if(!business.name.StartsWith("Town blueprint / business"))continue;
                    Transform body=business.Find("Stucco roadside business");if(!body)continue;
                    Vector3 at=business.localPosition;float w=body.localScale.x,h=body.localScale.y,d=body.localScale.z;
                    var frontage=new SceneryMesh();
                    AddStorefront(frontage,Vector3.zero,w,h,d,businessIndex,random);
                    frontage.Build(business.gameObject,"Lived-in storefront",townScenery);
                    businessIndex++;
                }
                Transform pad=town.Find("Compacted town pad");
                Vector3 townCenter=pad?pad.localPosition+Vector3.up*.15f:Vector3.zero;
                AddTownEdgeClutter(mesh,townCenter,townIndex,random);
                mesh.Build(town.gameObject,"Batched lot detail",townScenery);
                townIndex++;
            }
        }

        void AddStorefront(SceneryMesh mesh,Vector3 at,float w,float h,float d,int index,System.Random random)
        {
            float front=-d*.505f-.055f;
            Color trim=index%2==0?new Color(.78f,.66f,.43f):new Color(.35f,.55f,.52f);
            Color glass=new Color(.075f,.105f,.105f),door=new Color(.20f,.16f,.12f);
            float doorX=-w*.29f;
            TownBox(mesh,at+new Vector3(doorX,1.05f,front),new Vector3(1.05f,2.1f,.10f),door);
            TownBox(mesh,at+new Vector3(doorX,2.14f,front-.025f),new Vector3(1.22f,.10f,.14f),trim);
            TownBox(mesh,at+new Vector3(doorX-.58f,1.05f,front-.025f),new Vector3(.10f,2.18f,.14f),trim);
            TownBox(mesh,at+new Vector3(doorX+.58f,1.05f,front-.025f),new Vector3(.10f,2.18f,.14f),trim);
            TownBox(mesh,at+new Vector3(doorX+.34f,1.02f,front-.09f),new Vector3(.065f,.065f,.07f),new Color(.84f,.62f,.25f));
            float windowX=w*.20f,windowW=Mathf.Max(1.5f,w*.34f);
            TownBox(mesh,at+new Vector3(windowX,1.32f,front),new Vector3(windowW,1.25f,.10f),glass);
            TownBox(mesh,at+new Vector3(windowX,1.97f,front-.025f),new Vector3(windowW+.18f,.09f,.14f),trim);
            TownBox(mesh,at+new Vector3(windowX,.67f,front-.025f),new Vector3(windowW+.18f,.09f,.14f),trim);
            TownBox(mesh,at+new Vector3(windowX,1.32f,front-.03f),new Vector3(.065f,1.35f,.15f),trim*.8f);
            // Low rooftop silhouettes break up the otherwise blank slab roof.
            float roof=h+.38f;
            TownBox(mesh,at+new Vector3(w*.18f,roof+.22f,.15f*d),new Vector3(1.05f,.44f,.85f),new Color(.42f,.44f,.40f));
            TownBox(mesh,at+new Vector3(w*.18f,roof+.46f,.15f*d),new Vector3(.66f,.08f,.48f),new Color(.22f,.25f,.23f));
            Vector3 vent=at+new Vector3(-w*.24f,roof,-d*.08f);
            mesh.Tube(vent,vent+Vector3.up*.72f,.16f,.12f,new Color(.38f,.37f,.33f),7);
            mesh.Tube(vent+Vector3.up*.72f,vent+Vector3.up*.79f,.24f,.24f,new Color(.49f,.47f,.41f),7);
            // A planter at the wall gives each frontage one human-scale object without narrowing the lane.
            float planterX=index%2==0?w*.43f:-w*.43f;
            TownBox(mesh,at+new Vector3(planterX,.22f,front-.52f),new Vector3(.72f,.42f,.58f),new Color(.49f,.26f,.15f));
            Vector3 plant=at+new Vector3(planterX,.42f,front-.52f);
            mesh.Bush(plant,.54f,new Color(.34f,.43f,.19f),random);
        }

        void AddTownEdgeClutter(SceneryMesh mesh,Vector3 townCenter,int townIndex,System.Random random)
        {
            // Side and rear lots stay outside the storefront/road corridor around local z=0.
            for(int side=-1;side<=1;side+=2)
            {
                float x=side*(34+LivingWorldDetail.Next(random,0,4));
                for(int panel=0;panel<3;panel++)
                {
                    float z=-18+panel*7;Vector3 a=TownLocalGround(townCenter,new Vector3(x,0,z));
                    mesh.Tube(a,a+Vector3.up*1.15f,.055f,.045f,new Color(.35f,.28f,.20f),5);
                    for(int rail=0;rail<2;rail++)mesh.Tube(a+Vector3.up*(.35f+rail*.52f),a+new Vector3(0,.35f+rail*.52f,6),.035f,.035f,new Color(.42f,.31f,.21f),5);
                }
                Vector3 garden=TownLocalGround(townCenter,new Vector3(x-side*2.5f,0,13));
                for(int k=0;k<7;k++)mesh.Rock(garden+new Vector3(LivingWorldDetail.Next(random,-2.2f,2.2f),0,LivingWorldDetail.Next(random,-1.6f,1.6f)),Vector3.one*LivingWorldDetail.Next(random,.18f,.48f),new Color(.52f,.34f,.22f),random);
                if(side>0||townIndex%2==0)mesh.Cactus(garden+new Vector3(0,0,.3f),LivingWorldDetail.Next(random,1.5f,2.5f),random);
            }
            Vector3 dump=TownLocalGround(townCenter,new Vector3(townIndex%2==0?-31:31,0,27));
            // Pallets, cans and scrap are one colored mesh, clustered at a back-lot fence rather than scattered in traffic.
            for(int slat=0;slat<6;slat++)TownBox(mesh,dump+new Vector3((slat-2.5f)*.32f,.06f,0),new Vector3(.25f,.12f,1.25f),new Color(.40f,.27f,.15f));
            mesh.Tube(dump+new Vector3(2,.06f,.2f),dump+new Vector3(2.4f,.55f,.65f),.15f,.11f,new Color(.37f,.31f,.25f),7);
            mesh.Tube(dump+new Vector3(2.3f,.08f,-.4f),dump+new Vector3(1.7f,.18f,-.8f),.11f,.08f,new Color(.46f,.28f,.18f),7);
            TownBox(mesh,dump+new Vector3(-1.8f,.38f,.25f),new Vector3(.85f,.76f,.72f),new Color(.28f,.34f,.31f));
        }

        Vector3 TownLocalGround(Vector3 townCenter,Vector3 offset)
        {
            Vector3 point=townCenter+offset;point.y=HeightAt(point)+.025f;return point;
        }

        static void TownBox(SceneryMesh mesh,Vector3 c,Vector3 s,Color color)
        {
            Vector3 h=s*.5f;
            Vector3 a=c+new Vector3(-h.x,-h.y,-h.z),b=c+new Vector3(h.x,-h.y,-h.z),d=c+new Vector3(-h.x,h.y,-h.z),e=c+new Vector3(h.x,h.y,-h.z);
            Vector3 f=c+new Vector3(-h.x,-h.y,h.z),g=c+new Vector3(h.x,-h.y,h.z),i=c+new Vector3(-h.x,h.y,h.z),j=c+new Vector3(h.x,h.y,h.z);
            mesh.Tri(a,e,b,color);mesh.Tri(a,d,e,color);mesh.Tri(f,g,j,color*.86f);mesh.Tri(f,j,i,color*.86f);
            mesh.Tri(a,f,i,color*.76f);mesh.Tri(a,i,d,color*.76f);mesh.Tri(b,e,j,color*.92f);mesh.Tri(b,j,g,color*.92f);
            mesh.Tri(d,i,j,color*1.08f);mesh.Tri(d,j,e,color*1.08f);mesh.Tri(a,b,g,color*.68f);mesh.Tri(a,g,f,color*.68f);
        }
    }
}
