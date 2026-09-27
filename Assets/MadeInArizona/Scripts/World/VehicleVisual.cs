using UnityEngine;
using static MadeInArizona.WorldArt;

namespace MadeInArizona
{
    /// <summary>Original mechanically readable vehicle models, generated without imported vehicle artwork.</summary>
    public static class VehicleVisual
    {
        public static Transform Build(VehicleDefinition definition, Transform parent, bool enemy, EnemyFaction faction = EnemyFaction.Sunsprawl)
        {
            Transform root=Group("Original procedural vehicle model",parent,Vector3.zero);
            string id=definition.id ?? "thimble";
            bool tiny=id=="thimble", jeep=id=="juniper", buggy=id=="skitter", van=id=="sidehustle", trophy=id=="sunskip", diesel=id=="foreclosure", monster=id=="vincent";
            bool pickup=trophy||diesel||id=="perennial"||monster;
            float width=definition.trackWidth+.06f, length=definition.wheelbase+1.12f;
            float lift=Mathf.Clamp(definition.rideHeight-.56f,0,.45f);
            float sill=.49f+lift, cabHeight=van?1.28f:jeep?.97f:.74f;
            Color paint=enemy?FactionRules.Paint(faction):definition.color;
            Color glass=new Color(.11f,.25f,.30f), metal=new Color(.32f,.34f,.32f), rubber=new Color(.035f,.041f,.039f);
            Box("Boxed ladder frame",root,new Vector3(0,.3f+lift*.3f,0),new Vector3(width*.73f,.2f,length*.9f),Ink);
            for(int axle=-1;axle<=1;axle+=2)
            {
                var axleObj=Cylinder("Solid axle housing",root,new Vector3(0,.34f,axle*definition.wheelbase*.5f),.07f,width+.2f,metal); axleObj.transform.localRotation=Quaternion.Euler(0,0,90);
                Shape("Differential pumpkin",root,PrimitiveType.Sphere,new Vector3(0,.34f,axle*definition.wheelbase*.5f),new Vector3(.32f,.24f,.31f),Ink);
            }
            PaintBox("Painted lower body",root,new Vector3(0,sill,0),new Vector3(width,.44f,length),paint);
            PaintBox("Hood",root,new Vector3(0,sill+.34f,length*.31f),new Vector3(width*.98f,.23f,length*.33f),paint);
            Box("Front bumper",root,new Vector3(0,sill-.08f,length*.52f),new Vector3(width+.16f,.2f,.19f),Ink);
            Box("Rear bumper",root,new Vector3(0,sill-.1f,-length*.52f),new Vector3(width+.16f,.18f,.19f),Ink);
            // Twin tailpipes poke out under the rear bumper; nitro flames leave from each mouth (see NitroExhaust).
            for(int side=-1;side<=1;side+=2)
            {
                float pipeX=side*width*.3f,pipeY=sill-.25f,pipeEnd=-length*.52f-.2f;
                Beam("Tailpipe",root,new Vector3(pipeX,pipeY,-length*.4f),new Vector3(pipeX,pipeY,pipeEnd),.055f,new Color(.6f,.61f,.58f));
                var mouth=Cylinder("Tailpipe mouth",root,new Vector3(pipeX,pipeY,pipeEnd-.005f),.042f,.02f,Ink); mouth.transform.localRotation=Quaternion.Euler(90,0,0);
                Group(NitroExhaust.ExitName,root,new Vector3(pipeX,pipeY,pipeEnd-.03f)).localRotation=Quaternion.Euler(0,180,0);
            }
            Box("Radiator grille",root,new Vector3(0,sill+.16f,length*.506f),new Vector3(width*.52f,.25f,.04f),Ink);
            for(int i=0;i<7;i++) Box("Cooling fin",root,new Vector3((i-3)*width*.065f,sill+.16f,length*.531f),new Vector3(.025f,.2f,.026f),metal);
            for(int side=-1;side<=1;side+=2)
            {
                Shape("Headlight",root,jeep?PrimitiveType.Sphere:PrimitiveType.Cube,new Vector3(side*width*.35f,sill+.17f,length*.515f),new Vector3(width*.19f,.2f,.07f),new Color(1,.81f,.42f),false,0,.8f,2.6f);
                Box("Tail light",root,new Vector3(side*width*.39f,sill+.05f,-length*.508f),new Vector3(.16f,.22f,.05f),new Color(.95f,.035f,.025f),false,1.4f);
                PaintBox("Door armor",root,new Vector3(side*(width*.508f),sill+.14f,-.2f),new Vector3(.055f,.37f,length*.3f),Color.Lerp(paint,Ink,.18f));
                if(enemy) Box("Faction stripe",root,new Vector3(side*width*.54f,sill+.23f,-.2f),new Vector3(.025f,.095f,length*.3f),FactionRules.Accent(faction),false,1.1f);
                Box("Door handle",root,new Vector3(side*width*.542f,sill+.42f,-.35f),new Vector3(.05f,.04f,.18f),metal);
                Box("Mirror stem",root,new Vector3(side*width*.56f,sill+.76f,length*.15f),new Vector3(.22f,.035f,.04f),Ink);
                PaintBox("Wing mirror",root,new Vector3(side*width*.64f,sill+.79f,length*.15f),new Vector3(.12f,.14f,.2f),paint);
                for(int ax=-1;ax<=1;ax+=2)
                {
                    float z=ax*definition.wheelbase*.5f;
                    PaintBox("Squared fender flare",root,new Vector3(side*width*.50f,sill+.12f,z),new Vector3(.23f,.12f,.9f),jeep||trophy||monster?Ink:paint);
                    Beam("Suspension damper",root,new Vector3(side*width*.38f,.34f,z+.15f),new Vector3(side*width*.42f,sill+.28f,z-.15f),.044f,Orange);
                    Transform wheel=Group(ax>0?(side<0?"Wheel_FL":"Wheel_FR"):(side<0?"Wheel_RL":"Wheel_RR"),root,new Vector3(side*(definition.trackWidth*.5f+.06f),.36f+lift*.3f,z));
                    float radius=tiny?.35f:buggy?.4f:trophy||monster?.52f:.43f;
                    var tire=Cylinder("All terrain tire",wheel,Vector3.zero,radius,.31f,rubber); tire.transform.localRotation=Quaternion.Euler(0,0,90);
                    var rim=Cylinder("Beadlock rim",wheel,new Vector3(side*.17f,0,0),radius*.62f,.036f,jeep?Cream:metal); rim.transform.localRotation=Quaternion.Euler(0,0,90);
                    var hub=Cylinder("Hub",wheel,new Vector3(side*.194f,0,0),radius*.2f,.04f,Ink); hub.transform.localRotation=Quaternion.Euler(0,0,90);
                    for(int lug=0;lug<6;lug++) { float a=lug*Mathf.PI/3; Shape("Wheel bolt",wheel,PrimitiveType.Sphere,new Vector3(side*.206f,Mathf.Sin(a)*radius*.36f,Mathf.Cos(a)*radius*.36f),Vector3.one*.044f,Cream); }
                    if(!tiny) for(int tread=0;tread<12;tread++) { float a=tread*Mathf.PI/6; var block=Box("Tread block",wheel,new Vector3(0,Mathf.Sin(a)*radius,Mathf.Cos(a)*radius),new Vector3(.33f,.085f,.12f),rubber); block.transform.localRotation=Quaternion.Euler(-a*Mathf.Rad2Deg,0,0); }
                }
            }
            float roofY=sill+.45f+cabHeight;
            float cabLength=van?length*.78f:jeep?length*.58f:pickup?length*.36f:length*.54f;
            float cabZ=pickup?length*.065f:-length*.1f;
            if(!buggy)
            {
                Painted(Box("Cab glass volume",root,new Vector3(0,sill+.42f+cabHeight*.5f,cabZ),new Vector3(width*.91f,cabHeight,cabLength),glass),glass,0);
                PaintBox("Roof panel",root,new Vector3(0,roofY,cabZ-.055f),new Vector3(width*.95f,.13f,cabLength+.08f),jeep?Cream:paint);
                for(int side=-1;side<=1;side+=2)
                {
                    for(int edge=-1;edge<=1;edge+=2) PaintBox("Window pillar",root,new Vector3(side*width*.46f,sill+.44f+cabHeight*.5f,cabZ+edge*cabLength*.49f),new Vector3(.08f,cabHeight,.08f),paint);
                    PaintBox("Center pillar",root,new Vector3(side*width*.465f,sill+.45f+cabHeight*.5f,cabZ-.07f),new Vector3(.065f,cabHeight,.1f),paint);
                    PaintBox("Window sill",root,new Vector3(side*width*.475f,sill+.48f,cabZ),new Vector3(.065f,.08f,cabLength),paint);
                }
                Box("Windshield divider",root,new Vector3(0,sill+.46f+cabHeight*.5f,cabZ+cabLength*.505f),new Vector3(.043f,cabHeight,.022f),Ink);
                Beam("Windshield wiper",root,new Vector3(-width*.33f,sill+.5f,cabZ+cabLength*.51f),new Vector3(-width*.07f,sill+.67f,cabZ+cabLength*.51f),.012f,Ink);
            }
            else
            {
                roofY=sill+1.28f;
                for(int side=-1;side<=1;side+=2)
                {
                    PaintBeam("Front cage tube",root,new Vector3(side*width*.46f,sill,length*.28f),new Vector3(side*width*.39f,roofY,length*.10f),.055f,paint);
                    PaintBeam("Rear cage tube",root,new Vector3(side*width*.46f,sill,-length*.36f),new Vector3(side*width*.39f,roofY,-length*.25f),.055f,paint);
                    PaintBeam("Roof cage tube",root,new Vector3(side*width*.39f,roofY,length*.10f),new Vector3(side*width*.39f,roofY,-length*.25f),.055f,paint);
                    Box("Bucket seat",root,new Vector3(side*.37f,sill+.55f,-.15f),new Vector3(.43f,.75f,.25f),Ink);
                }
                PaintBeam("Cross brace",root,new Vector3(-width*.39f,roofY,-length*.25f),new Vector3(width*.39f,roofY,-length*.25f),.055f,paint);
                Box("Sun roof",root,new Vector3(0,roofY+.03f,-length*.07f),new Vector3(width*.85f,.06f,length*.4f),Ink);
            }
            if(pickup)
            {
                Box("Pickup bed floor",root,new Vector3(0,sill+.25f,-length*.32f),new Vector3(width*.87f,.05f,length*.32f),Ink);
                for(int side=-1;side<=1;side+=2) PaintBox("Bed rail",root,new Vector3(side*width*.46f,sill+.42f,-length*.34f),new Vector3(.16f,.35f,length*.32f),paint);
                PaintBox("Tailgate",root,new Vector3(0,sill+.41f,-length*.485f),new Vector3(width,.35f,.1f),paint);
                var spare=Cylinder("Spare in bed",root,new Vector3(0,sill+.39f,-length*.34f),.5f,.25f,Ink);
                if(diesel) for(int side=-1;side<=1;side+=2) { Cylinder("Diesel stack",root,new Vector3(side*width*.38f,roofY*.65f,-length*.19f),.09f,roofY*.9f,metal); Cylinder("Stack opening",root,new Vector3(side*width*.38f,roofY*1.103f,-length*.19f),.075f,.018f,Ink); }
            }
            if(jeep)
            {
                var spare=Cylinder("Rear mounted spare",root,new Vector3(0,sill+.52f,-length*.58f),.45f,.24f,Ink); spare.transform.localRotation=Quaternion.Euler(90,0,0);
                Box("Recovery winch",root,new Vector3(0,sill+.02f,length*.59f),new Vector3(.52f,.25f,.26f),metal);
                Cylinder("Snorkel",root,new Vector3(-width*.56f,roofY*.65f,length*.12f),.055f,roofY*.75f,Ink);
                Box("Snorkel cap",root,new Vector3(-width*.56f,roofY*1.04f,length*.12f),new Vector3(.15f,.1f,.17f),Ink);
            }
            if(van)
            {
                Box("Roof ladder",root,new Vector3(-width*.27f,roofY+.19f,-.15f),new Vector3(.065f,.07f,length*.86f),metal);
                Box("Roof ladder",root,new Vector3(width*.07f,roofY+.19f,-.15f),new Vector3(.065f,.07f,length*.86f),metal);
                for(int i=0;i<10;i++) Box("Ladder rung",root,new Vector3(-width*.1f,roofY+.19f,-length*.42f+i*length*.08f),new Vector3(width*.39f,.055f,.06f),metal);
                Text("COLD AIR / HOT TAKES",root,new Vector3(0,sill+.9f,-length*.505f),.135f,Cream);
            }
            if(trophy||monster) for(int side=-1;side<=1;side+=2) Beam("Bed cage",root,new Vector3(side*width*.43f,sill+.35f,-length*.44f),new Vector3(side*width*.36f,roofY,-length*.08f),.075f,monster?Cream:Ink);
            if(monster) { Box("Salvaged hood armor",root,new Vector3(.12f,sill+.48f,length*.31f),new Vector3(width*.64f,.14f,length*.26f),new Color(.20f,.49f,.43f)); Box("Questionable plow",root,new Vector3(0,sill-.08f,length*.6f),new Vector3(width*1.3f,.65f,.16f),Rust); }
            if(enemy && faction==EnemyFaction.OpenHouseRealty)
            {
                Box("Open house roof sign",root,new Vector3(0,roofY+.33f,cabZ-.28f),new Vector3(width*.68f,.46f,.08f),FactionRules.Accent(faction));
                Text("OPEN HOUSE",root,new Vector3(0,roofY+.35f,cabZ-.33f),.13f,Ink);
            }
            if(enemy && faction==EnemyFaction.SnowbirdConvoy)
            {
                Box("Snowbird roof luggage",root,new Vector3(0,roofY+.23f,cabZ-.33f),new Vector3(width*.65f,.32f,.72f),Cream);
                Box("Snowbird luggage strap",root,new Vector3(0,roofY+.41f,cabZ-.33f),new Vector3(width*.1f,.04f,.78f),FactionRules.Accent(faction));
            }
            if(enemy && faction==EnemyFaction.CarOtaku)
            {
                Box("Otaku rear wing",root,new Vector3(0,roofY+.08f,-length*.49f),new Vector3(width*1.08f,.08f,.31f),FactionRules.Accent(faction),false,1.7f);
                for(int side=-1;side<=1;side+=2) Box("Otaku wing mount",root,new Vector3(side*width*.32f,roofY-.1f,-length*.49f),new Vector3(.07f,.4f,.08f),Ink);
            }
            Text(enemy?FactionRules.Tag(faction):"AZ • 173",root,new Vector3(0,sill-.085f,-length*.55f),.115f,Cream);
            Transform turret=BuildTurret(root,new Vector3(0,roofY+.14f,cabZ+.1f),enemy);
            if(enemy) Box("Faction turret beacon",turret,new Vector3(-.22f,.17f,-.24f),new Vector3(.1f,.08f,.12f),FactionRules.Accent(faction),false,2);
            if(!enemy) Box("Turquoise friend beacon",turret,new Vector3(-.22f,.17f,-.24f),new Vector3(.10f,.08f,.12f),Turquoise,false,3);
            return root;
        }

        /// <summary>Painted body panels use the glossy clear-coat material; glass gets a mirror finish.</summary>
        static GameObject Painted(GameObject part,Color color,float metallic=.35f)
        {
            part.GetComponent<Renderer>().sharedMaterial=CarPaint(color,metallic);return part;
        }
        static GameObject PaintBox(string name,Transform parent,Vector3 pos,Vector3 size,Color color)=>Painted(Box(name,parent,pos,size,color),color);
        static GameObject PaintBeam(string name,Transform parent,Vector3 a,Vector3 b,float radius,Color color)=>Painted(Beam(name,parent,a,b,radius,color),color);

        public static Transform BuildTurret(Transform parent,Vector3 position,bool enemy=false)
        {
            Transform turret=Group("Turret",parent,position);
            Cylinder("Traverse ring",turret,Vector3.zero,.36f,.14f,Ink);
            Box("Gun receiver",turret,new Vector3(0,.21f,.12f),new Vector3(.35f,.28f,.62f),new Color(.22f,.24f,.22f));
            Box("Ammo box",turret,new Vector3(-.3f,.2f,.02f),new Vector3(.24f,.32f,.35f),new Color(.37f,.37f,.17f));
            Beam("Heavy barrel",turret,new Vector3(0,.24f,.3f),new Vector3(0,.24f,1.17f),.058f,Ink);
            for(int i=0;i<4;i++) { var sleeve=Cylinder("Cooling jacket ring",turret,new Vector3(0,.24f,.47f+i*.16f),.075f,.05f,new Color(.25f,.27f,.24f)); sleeve.transform.localRotation=Quaternion.Euler(90,0,0); }
            Box("Muzzle brake",turret,new Vector3(0,.24f,1.2f),new Vector3(.16f,.15f,.19f),Ink);
            Box("Belt feed",turret,new Vector3(-.2f,.37f,.08f),new Vector3(.25f,.03f,.12f),Cream);
            Group("Muzzle",turret,new Vector3(0,.24f,1.32f));
            return turret;
        }
    }
}
