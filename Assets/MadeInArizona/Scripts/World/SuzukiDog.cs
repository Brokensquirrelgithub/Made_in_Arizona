using UnityEngine;
using static MadeInArizona.WorldArt;

namespace MadeInArizona
{
    /// <summary>Suzuki is cosmetic, has no damage component or collision, and always returns safely to the garage.</summary>
    public partial class SuzukiDog : MonoBehaviour
    {
        Transform tail, head; readonly Transform[] legs=new Transform[4];
        Vector3 home;Vector3[] roamingRoute;float clock,pauseUntil;bool riding,sniffing;int cosmetic,waypoint,loopStart;
        public bool IsRiding=>riding;
        public bool IsRoaming=>!riding&&roamingRoute!=null&&roamingRoute.Length>1&&clock>=pauseUntil;
        public bool IsSniffing=>sniffing;
        public bool HasRoamingRoute=>roamingRoute!=null&&roamingRoute.Length>1;
        public int CurrentWaypoint=>waypoint;
        public float DistanceFromHome=>Vector3.Distance(transform.localPosition,home);
        public float LocalFloorHeight=>transform.localPosition.y;

        public static SuzukiDog Create(Transform parent,Vector3 position,int cosmetic = 0,bool riding = false,Vector3[] roamingWaypoints = null,int roamingLoopStart = 0)
        {
            Transform root=Group("Suzuki • senior recovery specialist",parent,position);
            SuzukiDog dog=root.gameObject.AddComponent<SuzukiDog>();dog.home=position;dog.riding=riding;dog.cosmetic=cosmetic;
            if(!riding&&roamingWaypoints!=null&&roamingWaypoints.Length>1)
            {dog.roamingRoute=(Vector3[])roamingWaypoints.Clone();dog.waypoint=0;dog.loopStart=Mathf.Clamp(roamingLoopStart,0,roamingWaypoints.Length-1);dog.pauseUntil=1.5f;}
            dog.Build();
            foreach(var renderer in root.GetComponentsInChildren<Renderer>()) {
                if(renderer.sharedMaterial.color.r>.85f && renderer.sharedMaterial.color.b>.85f) renderer.sharedMaterial=WorldArt.ScatteringMaterial(renderer.sharedMaterial.color);
            }
            return dog;
        }
        void Build()
        {
            Color tan=new Color(.94f,.97f,1f), pale=Color.white, dark=new Color(.075f,.10f,.14f);
            Shape("Body",transform,PrimitiveType.Capsule,new Vector3(0,.52f,0),new Vector3(.43f,.51f,.45f),tan).transform.localRotation=Quaternion.Euler(90,0,0);
            Shape("Chest",transform,PrimitiveType.Sphere,new Vector3(0,.57f,.30f),new Vector3(.40f,.51f,.35f),pale);
            head=Group("Attentive head",transform,new Vector3(0,.83f,.39f));
            Shape("Head",head,PrimitiveType.Sphere,Vector3.zero,new Vector3(.49f,.43f,.44f),tan);
            Shape("White muzzle",head,PrimitiveType.Sphere,new Vector3(0,-.075f,.22f),new Vector3(.32f,.21f,.28f),pale);
            Shape("Nose",head,PrimitiveType.Sphere,new Vector3(0,-.04f,.34f),new Vector3(.11f,.08f,.075f),dark);
            for(int side=-1;side<=1;side+=2)
            {
                Shape("Eye",head,PrimitiveType.Sphere,new Vector3(side*.15f,.055f,.188f),Vector3.one*.085f,new Color(.12f,.63f,.95f));
                Shape("Eye glint",head,PrimitiveType.Sphere,new Vector3(side*.15f+.012f,.07f,.217f),Vector3.one*.018f,Color.white);
                // Upright triangular husky ears with a silver inner face toward the front.
                var ear=Pyramid("Pointed ear",head,new Vector3(side*.15f,.13f,-.02f),new Vector3(.17f,.3f,.09f),tan); ear.transform.localRotation=Quaternion.Euler(-6,side*12,-side*14);
                Pyramid("Silver inner ear",ear.transform,new Vector3(0,.08f,.22f),new Vector3(.62f,.72f,.5f),new Color(.58f,.66f,.75f));
                Shape("Black pupil",head,PrimitiveType.Sphere,new Vector3(side*.15f,.055f,.228f),Vector3.one*.034f,dark);
            }
            for(int i=0;i<4;i++)
            {
                float side=i%2==0?-1:1, z=i<2?.27f:-.29f;
                legs[i]=Group("Paw " + i,transform,new Vector3(side*.155f,.43f,z));
                Shape("Leg",legs[i],PrimitiveType.Capsule,new Vector3(0,-.18f,0),new Vector3(.12f,.2f,.13f),i<2?pale:tan);
                Shape("Paw",legs[i],PrimitiveType.Sphere,new Vector3(0,-.34f,.04f),new Vector3(.16f,.1f,.22f),pale);
            }
            tail=Group("Enthusiastic tail",transform,new Vector3(0,.63f,-.39f));
            Beam("Plumed tail base",tail,Vector3.zero,new Vector3(0,.27f,-.22f),.12f,tan);
            Beam("Husky curled plume",tail,new Vector3(0,.27f,-.22f),new Vector3(0,.44f,-.08f),.14f,pale);
            Beam("Curled white tip",tail,new Vector3(0,.44f,-.08f),new Vector3(0,.35f,.12f),.12f,pale);
            for(int i=0;i<7;i++) Shape("Plush white neck ruff",transform,PrimitiveType.Sphere,new Vector3(Mathf.Sin(i*6.28f/7)*.15f,.62f+Mathf.Cos(i*6.28f/7)*.19f,.25f),Vector3.one*.25f,pale);
            Shape("Tail tip",tail,PrimitiveType.Sphere,new Vector3(0,.35f,-.25f),Vector3.one*.23f,pale);
            Box("Turquoise collar",transform,new Vector3(0,.63f,.29f),new Vector3(.42f,.085f,.13f),Turquoise);
            Shape("10 mm socket name tag",transform,PrimitiveType.Cylinder,new Vector3(0,.54f,.39f),new Vector3(.08f,.025f,.08f),Cream).transform.localRotation=Quaternion.Euler(90,0,0);
            if(cosmetic%4==1)
            {
                Shape("Safety supervisor helmet",head,PrimitiveType.Sphere,new Vector3(0,.22f,0),new Vector3(.53f,.24f,.48f),Orange);
                Box("Helmet brim",head,new Vector3(0,.17f,.13f),new Vector3(.55f,.035f,.39f),Orange);
            }
            else if(cosmetic%4==2)
            {
                for(int side=-1;side<=1;side+=2) { Shape("Desert doggles",head,PrimitiveType.Sphere,new Vector3(side*.13f,.05f,.207f),new Vector3(.2f,.15f,.045f),Ink); Shape("Doggles lens",head,PrimitiveType.Sphere,new Vector3(side*.13f,.05f,.23f),new Vector3(.155f,.105f,.025f),Turquoise); }
                Box("Doggles bridge",head,new Vector3(0,.05f,.23f),new Vector3(.1f,.025f,.025f),Ink);
            }
            else if(cosmetic%4==3)
            {
                var cape=Wedge("Recovery captain cape",transform,new Vector3(0,.68f,-.2f),new Vector3(.53f,.14f,.61f),new Color(.71f,.15f,.24f)); cape.transform.localRotation=Quaternion.Euler(180,0,0);
                Cylinder("Senior specialist badge",head,new Vector3(0,.21f,.02f),.12f,.06f,Cream);
            }
        }
        void Update()
        {
            if(Time.timeScale<=0) return; clock+=Time.deltaTime;
            float wag=IsWashing?13:8;
            if(tail) tail.localRotation=Quaternion.Euler(0,Mathf.Sin(clock*wag)*27,Mathf.Sin(clock*wag)*9);
            bool walking=!riding&&roamingRoute!=null&&roamingRoute.Length>1&&clock>=pauseUntil;
            // A dirty car in the garage: she fetches the hose and washes it (SuzukiDogWash).
            bool washing=UpdateWash(ref walking);
            if(washing){}
            else if(walking)
            {
                sniffing=false;
                Vector3 target=roamingRoute[waypoint];
                Vector3 delta=target-transform.localPosition;
                delta.y=0;
                if(delta.sqrMagnitude>.002f)transform.localRotation=Quaternion.Slerp(transform.localRotation,Quaternion.LookRotation(delta),Time.deltaTime*3.8f);
                // A dusty car waiting: she trots the rest of her round to the hose instead of strolling.
                transform.localPosition=Vector3.MoveTowards(transform.localPosition,target,Time.deltaTime*(WashPending?Trot:.82f));
                if((transform.localPosition-target).sqrMagnitude<.025f)
                {
                    int reached=waypoint;
                    waypoint++;if(waypoint>=roamingRoute.Length)waypoint=loopStart;
                    if(reached==WashStartWaypoint&&WashPending){BeginWash();}
                    else if(reached<loopStart||WashPending){pauseUntil=clock;sniffing=false;}
                    else
                    {
                        float pause=1.8f+Mathf.Abs(Mathf.Sin((waypoint+1)*2.17f+cosmetic))*3.2f;
                        pauseUntil=clock+pause;sniffing=((waypoint+cosmetic)%3)!=0;
                    }
                }
            }
            else if(clock>=pauseUntil)sniffing=false;
            if(head&&!washing)
            {
                float pitch=sniffing?34+Mathf.Sin(clock*4.2f)*5:Mathf.Sin(clock*.8f)*5;
                float yaw=sniffing?Mathf.Sin(clock*2.3f)*8:Mathf.Sin(clock*.6f)*17;
                head.localRotation=Quaternion.Euler(pitch,yaw,Mathf.Sin(clock*.42f)*4);
            }
            for(int i=0;i<legs.Length;i++)if(legs[i])
            {
                float phase=(i==0||i==3)?0:Mathf.PI;
                float stride=walking?Mathf.Sin(clock*11+phase)*24:0;
                legs[i].localRotation=Quaternion.Slerp(legs[i].localRotation,Quaternion.Euler(stride,0,0),Time.deltaTime*12);
            }
        }
    }
}
