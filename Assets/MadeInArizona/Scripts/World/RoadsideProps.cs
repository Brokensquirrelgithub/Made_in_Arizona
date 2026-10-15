using UnityEngine;
using static MadeInArizona.WorldArt;

namespace MadeInArizona
{
    /// <summary>Reusable original prop constructors used by levels and the editor prefab exporter.</summary>
    public static class RoadsideProps
    {
        // Simplified from the CC0 Wikimedia Commons "Outline of Texas (simplified).svg":
        // https://commons.wikimedia.org/wiki/File:Outline_of_Texas_(simplified).svg
        // Keep the panhandle, western edge and Gulf coast legible at cylinder decal size.
        static readonly Vector2[] TexasOutline = {
            new Vector2(0f,.5644f),new Vector2(.1247f,.4506f),new Vector2(.1646f,.3539f),
            new Vector2(.2554f,.2938f),new Vector2(.2843f,.3156f),new Vector2(.3099f,.3681f),
            new Vector2(.3987f,.3677f),new Vector2(.4450f,.3184f),new Vector2(.4847f,.2263f),
            new Vector2(.5417f,.1621f),new Vector2(.5744f,.0541f),new Vector2(.7035f,0f),
            new Vector2(.7276f,.0175f),new Vector2(.7097f,.1091f),new Vector2(.7354f,.1867f),
            new Vector2(.9155f,.3333f),new Vector2(.9774f,.3520f),new Vector2(.9772f,.3822f),
            new Vector2(1f,.4881f),new Vector2(.96f,.5775f),new Vector2(.959f,.7241f),
            new Vector2(.9343f,.7232f),new Vector2(.8735f,.7606f),new Vector2(.7852f,.7374f),
            new Vector2(.7343f,.7606f),new Vector2(.7238f,.739f),new Vector2(.7182f,.7574f),
            new Vector2(.7013f,.7483f),new Vector2(.6819f,.7642f),new Vector2(.6634f,.7527f),
            new Vector2(.6493f,.7799f),new Vector2(.6191f,.7728f),new Vector2(.5682f,.787f),
            new Vector2(.5532f,.8083f),new Vector2(.5265f,.8032f),new Vector2(.5052f,.8181f),
            new Vector2(.5052f,.9996f),new Vector2(.2733f,1f),new Vector2(.2718f,.5775f),
            new Vector2(.0007f,.577f)
        };
        // Ear-clipped concave silhouette; clockwise winding faces the front of each tank.
        static readonly int[] TexasTriangles = {
            39,1,0,1,3,2,1,4,3,39,4,1,39,5,4,39,6,5,
            7,9,8,9,11,10,7,11,9,7,12,11,7,13,12,6,13,7,
            6,14,13,6,15,14,39,15,6,39,16,15,39,17,16,39,18,17,
            39,19,18,19,21,20,19,22,21,19,23,22,19,24,23,19,25,24,
            25,27,26,19,27,25,27,29,28,19,29,27,29,31,30,19,31,29,
            19,32,31,32,34,33,19,34,32,35,37,36,35,38,37,34,38,35,
            19,38,34,19,39,38
        };
        public static Transform Cactus(Transform parent, Vector3 pos, float scale = 1, bool destructible = true)
        {
            Transform g=Group("Saguaro",parent,pos); g.localScale=Vector3.one*scale;
            Color green=new Color(.26f,.34f,.16f);
            Shape("Ribbed trunk",g,PrimitiveType.Capsule,new Vector3(0,1.7f,0),new Vector3(.65f,1.8f,.65f),green);
            Beam("West arm",g,new Vector3(0,1.6f,0),new Vector3(-.9f,1.6f,0),.21f,green);
            Shape("West finger",g,PrimitiveType.Capsule,new Vector3(-.9f,2.1f,0),new Vector3(.43f,.7f,.43f),green);
            Beam("East arm",g,new Vector3(0,2.2f,0),new Vector3(.8f,2.2f,0),.19f,green);
            Shape("East finger",g,PrimitiveType.Capsule,new Vector3(.8f,2.6f,0),new Vector3(.38f,.6f,.38f),green);
            for(int i=0;i<5;i++) { float a=i*Mathf.PI*2/5; Beam("Saguaro ridge",g,new Vector3(Mathf.Sin(a)*.3f,.35f,Mathf.Cos(a)*.3f),new Vector3(Mathf.Sin(a)*.3f,2.95f,Mathf.Cos(a)*.3f),.025f,new Color(.38f,.43f,.21f)); }
            // A capsule (a line with a radius) is the cheapest collider after a sphere.
            var trunk=g.gameObject.AddComponent<CapsuleCollider>(); trunk.center=new Vector3(0,1.5f,0); trunk.height=3; trunk.radius=.35f;
            if(destructible) { g.gameObject.layer=DestructionSystem.BrittleLayer; MakeBreakable(g,24,false,ExplosionKind.Gasoline,5); g.GetComponent<DestructionSystem>().MakeBrittle(); }
            return g;
        }
        public static Transform Rock(Transform parent,Vector3 pos,Vector3 scale,float hue=0)
        {
            var g=Shape("Faceted sandstone",parent,PrimitiveType.Sphere,pos,scale,new Color(.47f+hue,.26f+hue*.6f,.17f+hue*.3f),true).transform;
            g.localRotation=Quaternion.Euler(13,scale.x*28,24);
            MakeBreakable(g,Mathf.Clamp(12+scale.magnitude*3,16,30),false,ExplosionKind.Ammunition,3);
            // Loose sandstone: cars plough straight through it whatever its size.
            g.GetComponent<DestructionSystem>().MakePlowable();
            return g;
        }
        public static Transform Propane(Transform parent,Vector3 pos, bool large = false)
        {
            Transform g=Group(large?"Propane bulk tank":"Propane cylinder",parent,pos);
            if(large)
            {
                var tank=Use(Shape("Pressure vessel",g,PrimitiveType.Capsule,new Vector3(0,1.45f,0),new Vector3(1.8f,2.8f,1.8f),Cream),Reflective(Cream,SunGlint.Paint,.72f,0,1.6f));
                tank.transform.localRotation=Quaternion.Euler(0,0,90);
                Box("Left cradle",g,new Vector3(-1.65f,.45f,0),new Vector3(.5f,.9f,1.7f),Rust);
                Box("Right cradle",g,new Vector3(1.65f,.45f,0),new Vector3(.5f,.9f,1.7f),Rust);
                Text("SUNBURN PROPANE",g,new Vector3(0,1.12f,-.94f),.3f,Rust);
                AddTexasMark(g,new Vector3(0,1.9f,-1.02f),.85f,false);
                AddTexasMark(g,new Vector3(0,1.9f,1.02f),.85f,true);
                AddTexasTopMark(g,new Vector3(0,2.42f,0),1.15f);
                BoundsCollider(g,new Vector3(0,1.35f,0),new Vector3(5.6f,2.4f,1.8f));
            }
            else
            {
                Use(Shape("Tank",g,PrimitiveType.Capsule,new Vector3(0,.63f,0),new Vector3(.62f,.62f,.62f),Cream),Reflective(Cream,SunGlint.Paint,.7f,0,1.6f));
                Cylinder("Foot",g,new Vector3(0,.08f,0),.32f,.16f,Ink);
                Cylinder("Valve",g,new Vector3(0,1.29f,0),.08f,.19f,Rust);
                Box("Warning band",g,new Vector3(0,.49f,-.312f),new Vector3(.35f,.12f,.025f),Orange);
                AddTexasMark(g,new Vector3(0,.84f,-.36f),.34f,false);
                AddTexasMark(g,new Vector3(0,.84f,.36f),.34f,true);
                BoundsCollider(g,new Vector3(0,.6f,0),new Vector3(.7f,1.4f,.7f));
            }
            MakeBreakable(g,large?65:18,true,ExplosionKind.Propane,large?150:45); return g;
        }
        /// <summary>One visible roadside cache, with individually explosive tanks for chain reactions.</summary>
        public static Transform PropanePile(Transform parent,Vector3 pos)
        {
            Transform pile=Group("Propane tank pile",parent,pos);
            Box("Tank staging pallet",pile,new Vector3(0,.07f,0),new Vector3(8.4f,.14f,5.4f),Rust);
            Propane(pile,Vector3.zero,true);
            for(int i=0;i<4;i++)
            {
                float x=i<2?-3.35f:3.35f,z=i%2==0?-1.75f:1.75f;
                Propane(pile,new Vector3(x,0,z)).localScale=Vector3.one*1.5f;
            }
            return pile;
        }
        /// <summary>A compact pyramid of individually explosive small cylinders.</summary>
        public static Transform SmallPropanePile(Transform parent,Vector3 pos)
        {
            Transform pile=Group("Small propane cylinder stack",parent,pos);
            Box("Tank staging pallet",pile,new Vector3(0,.07f,0),new Vector3(4.2f,.14f,5.1f),Rust);
            for(int row=0;row<3;row++)
            {
                int count=4-row;
                for(int i=0;i<count;i++)
                {
                    float z=(i-(count-1)*.5f)*1.06f;
                    Transform tank=Propane(pile,new Vector3(1.01f,.65f+row*.88f,z));
                    tank.localScale=Vector3.one*1.6f;
                    tank.localRotation=Quaternion.Euler(0,0,90);
                }
            }
            return pile;
        }
        public static Transform FuelTanker(Transform parent,Vector3 pos,Quaternion facing)
        {
            Transform g=Group("Fuel tanker trailer",parent,pos);g.localRotation=facing;
            Box("Tanker chassis",g,new Vector3(0,.82f,0),new Vector3(2.5f,.45f,11.6f),Ink);
            var vessel=Use(Shape("Fuel vessel",g,PrimitiveType.Capsule,new Vector3(0,2.15f,-1.2f),new Vector3(2.2f,4.25f,2.2f),Cream),Reflective(Cream,SunGlint.Metal,.7f,.42f,1.4f));
            vessel.transform.localRotation=Quaternion.Euler(90,0,0);
            Box("Tanker cab",g,new Vector3(0,1.8f,4.55f),new Vector3(2.3f,2.4f,2.1f),new Color(.67f,.27f,.15f));
            Box("Cab windshield",g,new Vector3(0,2.35f,5.64f),new Vector3(1.75f,.78f,.045f),Ink);
            for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2)
            {
                var wheel=Cylinder("Heavy tanker wheel",g,new Vector3(x*1.31f,.53f,z<0?-4.2f:3.55f),.53f,.32f,Ink);
                wheel.transform.localRotation=Quaternion.Euler(0,0,90);
            }
            for(int side=-1;side<=1;side+=2)
            {
                Box("Red flammable stripe",g,new Vector3(side*1.11f,2.05f,-1.25f),new Vector3(.045f,.28f,6.7f),new Color(.85f,.14f,.06f));
                Box("Hazard diamond",g,new Vector3(side*1.125f,2.5f,-2.2f),new Vector3(.05f,.58f,.58f),Orange).transform.localRotation=Quaternion.Euler(45,0,0);
            }
            Cylinder("Leaking valve",g,new Vector3(0,3.36f,-1.2f),.25f,.3f,Rust);
            Lamp("Leak warning",g,new Vector3(0,3.55f,-1.2f),new Color(1,.35f,.08f),0,6);
            BoundsCollider(g,new Vector3(0,1.75f,0),new Vector3(3.05f,3.4f,12));
            MakeBreakable(g,150,true,ExplosionKind.FuelTank,180);
            g.gameObject.AddComponent<WorldHazard>().Configure(WorldHazardKind.FuelTanker);
            return g;
        }
        public static Transform GasPumpIsland(Transform parent,Vector3 pos,Quaternion facing)
        {
            Transform g=Group("Gas pump island",parent,pos);g.localRotation=facing;
            Box("Concrete pump island",g,new Vector3(0,.11f,0),new Vector3(7.5f,.22f,4.2f),new Color(.52f,.49f,.42f));
            for(int side=-1;side<=1;side+=2)
            {
                Transform pump=Group("Gas pump",g,new Vector3(side*1.75f,.22f,0));
                Box("Dispenser body",pump,new Vector3(0,.77f,0),new Vector3(.9f,1.5f,.75f),Cream);
                Box("Red dispenser cap",pump,new Vector3(0,1.63f,0),new Vector3(1.05f,.3f,.85f),new Color(.77f,.13f,.08f));
                Box("Pump display",pump,new Vector3(0,1.16f,-.39f),new Vector3(.65f,.36f,.035f),Ink);
                Text("FUEL",pump,new Vector3(0,1.19f,-.42f),.13f,Cream);
                Beam("Black fuel hose",pump,new Vector3(.52f,1.42f,0),new Vector3(.6f,.38f,-.21f),.055f,Ink);
                Cylinder("Nozzle",pump,new Vector3(.55f,.33f,-.22f),.09f,.28f,Rust);
                BoundsCollider(pump,new Vector3(0,.94f,0),new Vector3(1.25f,1.9f,1));
                MakeBreakable(pump,38,true,ExplosionKind.Gasoline,85);
                pump.gameObject.AddComponent<WorldHazard>().Configure(WorldHazardKind.GasPump);
            }
            return g;
        }
        public static Transform BlastingCrates(Transform parent,Vector3 pos,Quaternion facing)
        {
            Transform g=Group("Mining blast crates",parent,pos);g.localRotation=facing;
            Box("Shipping pallet",g,new Vector3(0,.12f,0),new Vector3(3.4f,.24f,2.6f),Rust);
            Color wood=new Color(.53f,.33f,.18f);
            for(int i=0;i<3;i++)
            {
                Vector3 p=new Vector3(i==2?0:i==0?-1:1,i==2?1.65f:.75f,i==2?0:-.25f);
                Box("Explosives crate",g,p,new Vector3(1.3f,1.15f,1.3f),wood);
                Box("Warning stripe",g,p+new Vector3(0,.12f,-.665f),new Vector3(1.1f,.19f,.035f),Orange);
            }
            for(int i=0;i<5;i++)Cylinder("Dynamite charge",g,new Vector3((i-2)*.25f,2.33f,0),.095f,.54f,new Color(.78f,.12f,.06f));
            Text("BLAST",g,new Vector3(0,.75f,-1.01f),.34f,Cream);
            Lamp("Fuse warning",g,new Vector3(0,2.75f,0),new Color(1,.25f,.05f),0,5);
            BoundsCollider(g,new Vector3(0,1.27f,0),new Vector3(3.3f,2.6f,2.5f));
            MakeBreakable(g,36,true,ExplosionKind.Ammunition,105);
            g.gameObject.AddComponent<WorldHazard>().Configure(WorldHazardKind.BlastingCrates);
            return g;
        }
        public static Transform FireworksStand(Transform parent,Vector3 pos,Quaternion facing)
        {
            Transform g=Group("Fireworks stand",parent,pos);g.localRotation=facing;
            Box("Stand counter",g,new Vector3(0,.8f,0),new Vector3(4.8f,1.3f,2.6f),Rust);
            Box("Striped roof",g,new Vector3(0,3.3f,0),new Vector3(5.4f,.25f,3.3f),new Color(.82f,.2f,.13f));
            for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2)Box("Roof post",g,new Vector3(x*2.25f,2.1f,z*1.16f),new Vector3(.13f,2.5f,.13f),Cream);
            Box("Fireworks sign",g,new Vector3(0,2.8f,-1.49f),new Vector3(4.65f,.65f,.13f),Ink);
            Text("FIREWORKS",g,new Vector3(0,2.82f,-1.57f),.31f,new Color(1,.72f,.16f));
            for(int i=0;i<7;i++)
            {
                var tube=Cylinder("Firework tube",g,new Vector3((i-3)*.55f,1.73f,(i%2==0?.35f:-.2f)),.17f,1.15f,
                    i%3==0?new Color(.24f,.51f,.76f):i%3==1?new Color(.78f,.22f,.36f):Orange);
                tube.transform.localRotation=Quaternion.Euler((i-3)*5,0,(i-3)*7);
            }
            BoundsCollider(g,new Vector3(0,1.7f,0),new Vector3(5.2f,3.4f,3.1f));
            MakeBreakable(g,48,true,ExplosionKind.Ammunition,110);
            g.gameObject.AddComponent<WorldHazard>().Configure(WorldHazardKind.Fireworks);
            return g;
        }
        public static Transform ChemicalDrumCluster(Transform parent,Vector3 pos)
        {
            Transform g=Group("Chemical drum cache",parent,pos);
            Box("Containment pallet",g,new Vector3(0,.09f,0),new Vector3(4.1f,.18f,3.4f),Ink);
            ChemicalDrum(g,new Vector3(-1.05f,.18f,-.6f));
            ChemicalDrum(g,new Vector3(1.05f,.18f,-.6f));
            ChemicalDrum(g,new Vector3(0,.18f,.9f));
            return g;
        }
        public static Transform ChemicalDrum(Transform parent,Vector3 pos)
        {
            Transform g=Group("Chemical drum",parent,pos);
            Color green=new Color(.19f,.52f,.31f);
            Use(Cylinder("Corrosive barrel",g,new Vector3(0,.61f,0),.52f,1.22f,green),Reflective(green,SunGlint.Metal,.55f,.35f,1.2f));
            for(int ring=0;ring<2;ring++)Cylinder("Steel rim",g,new Vector3(0,.14f+ring*.94f,0),.53f,.075f,Ink);
            Box("Acid label",g,new Vector3(0,.69f,-.53f),new Vector3(.65f,.48f,.035f),new Color(.85f,.87f,.27f));
            Text("!",g,new Vector3(0,.71f,-.56f),.34f,Ink);
            BoundsCollider(g,new Vector3(0,.61f,0),new Vector3(1.1f,1.3f,1.1f));
            MakeBreakable(g,25,true,ExplosionKind.Chemical,60);
            g.gameObject.AddComponent<WorldHazard>().Configure(WorldHazardKind.ChemicalDrum);
            return g;
        }
        public static Transform TransformerCabinet(Transform parent,Vector3 pos)
        {
            Transform g=Group("Explosive transformer cabinet",parent,pos);
            Box("Concrete plinth",g,new Vector3(0,.15f,0),new Vector3(2.8f,.3f,2.2f),new Color(.49f,.48f,.41f));
            Color steel=new Color(.32f,.45f,.45f);
            Use(Box("Transformer housing",g,new Vector3(0,1.55f,0),new Vector3(2.15f,2.45f,1.55f),steel),Reflective(steel,SunGlint.Metal,.6f,.55f,1.5f));
            for(int i=-1;i<=1;i++)Cylinder("Ceramic insulator",g,new Vector3(i*.62f,3.03f,0),.17f,.58f,Cream);
            Beam("Yellow high-voltage bolt",g,new Vector3(-.25f,2.14f,-.81f),new Vector3(.14f,1.66f,-.81f),.075f,Orange);
            Beam("Yellow high-voltage bolt",g,new Vector3(.14f,1.66f,-.81f),new Vector3(-.11f,1.12f,-.81f),.075f,Orange);
            BoundsCollider(g,new Vector3(0,1.6f,0),new Vector3(2.3f,3.2f,1.8f));
            MakeBreakable(g,55,true,ExplosionKind.Electrical,115);
            g.gameObject.AddComponent<WorldHazard>().Configure(WorldHazardKind.Transformer);
            return g;
        }
        static void AddTexasMark(Transform parent,Vector3 position,float size,bool back)
        {
            var vertices=new Vector3[TexasOutline.Length];
            for(int i=0;i<TexasOutline.Length;i++)
            {
                Vector2 point=TexasOutline[i]-new Vector2(.5f,.5f);
                vertices[i]=new Vector3(point.x,point.y,0);
            }
            var mark=MeshObject("Red Texas logo",parent,vertices,TexasTriangles,new Color(.9f,.055f,.035f));
            mark.transform.localPosition=position;
            mark.transform.localRotation=Quaternion.Euler(0,back?180:0,0);
            mark.transform.localScale=new Vector3(size,size,1);
        }
        static void AddTexasTopMark(Transform parent,Vector3 position,float size)
        {
            AddTexasMark(parent,position,size,false);
            parent.GetChild(parent.childCount-1).localRotation=Quaternion.Euler(90,0,0);
        }
        public static Transform Barrel(Transform parent,Vector3 pos, Color color,bool explosive = false)
        {
            Transform g=Group(explosive?"Fuel drum":"Used oil drum",parent,pos);
            Use(Cylinder("Drum",g,new Vector3(0,.55f,0),.45f,1.1f,color),Reflective(color,SunGlint.Paint,.5f,.2f,1.8f));
            for(int i=0;i<3;i++) Use(Cylinder("Steel ring",g,new Vector3(0,.08f+i*.46f,0),.465f,.055f,Ink),Reflective(Ink,SunGlint.Metal,.45f,1,1.5f));
            Cylinder("Cap",g,new Vector3(.2f,1.115f,0),.055f,.02f,Ink);
            BoundsCollider(g,new Vector3(0,.55f,0),new Vector3(.92f,1.1f,.92f));
            MakeBreakable(g,24,explosive,ExplosionKind.Gasoline,30); return g;
        }
        public static Transform Crate(Transform parent,Vector3 pos,float size=1)
        {
            Transform g=Group("Salvage pallet",parent,pos); Color wood=new Color(.49f,.33f,.19f);
            for(int i=0;i<4;i++) Box("Pallet plank",g,new Vector3(0,.08f,i*.3f-.45f),new Vector3(1.4f,.14f,.22f),wood);
            Box("Crate",g,new Vector3(0,.6f,0),new Vector3(1.13f,1,.96f),new Color(.57f,.4f,.24f));
            for(int i=-1;i<=1;i+=2) { Box("Strap",g,new Vector3(i*.38f,.6f,-.485f),new Vector3(.09f,1.03f,.045f),Ink); Box("Crate lid",g,new Vector3(i*.38f,1.12f,0),new Vector3(.09f,.045f,1),Ink); }
            g.localScale=Vector3.one*size; BoundsCollider(g,new Vector3(0,.6f,0),new Vector3(1.4f,1.2f,1.2f)); MakeBreakable(g,26,false,ExplosionKind.Ammunition,20); return g;
        }
        public static Transform Fence(Transform parent,Vector3 pos,float length,float angle=0)
        {
            Transform g=Group("Breakaway fence",parent,pos); g.localRotation=Quaternion.Euler(0,angle,0);
            for(int side=-1;side<=1;side+=2) Box("Post",g,new Vector3(side*length*.5f,1,0),new Vector3(.13f,2.2f,.13f),Rust);
            for(int i=0;i<3;i++) Box("Wire",g,new Vector3(0,.45f+i*.62f,0),new Vector3(length,.025f,.025f),Cream);
            for(float x=-length*.5f+.35f;x<length*.5f;x+=.55f) Beam("Diamond mesh",g,new Vector3(x,.12f,0),new Vector3(x+.45f,1.9f,0),.009f,new Color(.47f,.41f,.33f));
            BoundsCollider(g,new Vector3(0,1,0),new Vector3(length,2,.12f)); MakeBreakable(g,15,false,ExplosionKind.Electrical,10); return g;
        }
        public static Transform Sign(Transform parent,Vector3 pos,string title,string subtitle,Color paint,float width = 9)
        {
            Transform g=Group("Roadside sign " + title,parent,pos);
            for(int i=-1;i<=1;i+=2) Box("Sign post",g,new Vector3(i*width*.32f,2.8f,0),new Vector3(.22f,5.6f,.22f),Rust);
            Use(Box("Steel frame",g,new Vector3(0,4.8f,0),new Vector3(width+.24f,3.2f,.28f),Ink),Reflective(Ink,SunGlint.Metal,.5f,1,1.3f));
            Use(Box("Painted board",g,new Vector3(0,4.8f,-.17f),new Vector3(width,2.95f,.07f),paint),Reflective(paint,SunGlint.Sign,.82f,0,1.2f));
            Text(title,g,new Vector3(0,5.15f,-.23f),Mathf.Min(.65f,width/Mathf.Max(title.Length,1)*1.55f),Cream);
            Text(subtitle,g,new Vector3(0,4.18f,-.235f),.23f,Cream);
            BoundsCollider(g,new Vector3(0,3,0),new Vector3(width,6,.3f)); MakeBreakable(g,40,false,ExplosionKind.Electrical,40); return g;
        }
        public static Transform Building(Transform parent,Vector3 pos,string name,Color wall,float width=15,float depth=11,float height=5)
        {
            Transform g=Group(name,parent,pos);
            var shell=Box("Stucco shell",g,new Vector3(0,height*.5f,0),new Vector3(width,height,depth),wall,true);
            ApplyWall(shell,wall,Mathf.Abs(Mathf.RoundToInt(wall.r*17+wall.g*11+wall.b*7))%WallTextureSet.TextureCount,new Vector3(width,height,depth));
            Box("Flat roof",g,new Vector3(0,height+.12f,0),new Vector3(width+.6f,.35f,depth+.6f),Cream);
            Box("Roof lip",g,new Vector3(0,height+.48f,-depth*.5f),new Vector3(width+.7f,.6f,.32f),Rust);
            Use(Box("Shopfront window",g,new Vector3(-width*.21f,1.8f,-depth*.5f-.04f),new Vector3(width*.31f,2.4f,.08f),new Color(.12f,.28f,.31f)),Glass(new Color(.12f,.28f,.31f)));
            Use(Box("Shopfront window",g,new Vector3(width*.25f,1.8f,-depth*.5f-.04f),new Vector3(width*.25f,2.4f,.08f),new Color(.12f,.28f,.31f)),Glass(new Color(.12f,.28f,.31f)));
            Box("Door",g,new Vector3(.5f,1.3f,-depth*.5f-.08f),new Vector3(1.55f,2.6f,.13f),Ink);
            Box("Awning",g,new Vector3(0,height-.75f,-depth*.5f-1.1f),new Vector3(width+.4f,.15f,2.6f),Turquoise);
            Text(name,g,new Vector3(0,height-.23f,-depth*.5f-.22f),Mathf.Min(.5f,width/name.Length*1.6f),Cream);
            Box("Rooftop AC",g,new Vector3(width*.28f,height+.8f,1),new Vector3(2,1.3f,1.5f),new Color(.58f,.59f,.52f));
            for(int i=0;i<5;i++) Box("AC vent",g,new Vector3(width*.28f,height+.45f+i*.17f,.235f),new Vector3(1.7f,.055f,.03f),Ink);
            MakeBreakable(g,260,false,ExplosionKind.Gasoline,150);
            return g;
        }
        /// <summary>Permanent service bay on the generated map. Its walls stay solid under vehicle and weapon fire.</summary>
        public static Transform Garage(Transform parent,Vector3 pos,Quaternion facing)
        {
            Transform g=Group("117° Auto Care Garage",parent,pos);g.localRotation=facing;
            const float width=14,depth=12,height=5.8f;
            Color wall=new Color(.29f,.34f,.32f);
            Box("Garage floor",g,new Vector3(0,-.08f,0),new Vector3(width,.18f,depth),new Color(.43f,.43f,.38f),true);
            Box("Garage back wall",g,new Vector3(0,height*.5f,depth*.5f),new Vector3(width,height,.36f),wall,true);
            for(int side=-1;side<=1;side+=2)
            {
                Box("Garage side wall",g,new Vector3(side*width*.5f,height*.5f,0),new Vector3(.36f,height,depth),wall,true);
                Box("Service bay pier",g,new Vector3(side*4.65f,height*.5f,-depth*.5f),new Vector3(4.7f,height,.36f),wall,true);
                Box("Bay warning stripe",g,new Vector3(side*2.38f,1.25f,-depth*.5f-.22f),new Vector3(.12f,2.5f,.04f),Orange);
            }
            Box("Garage roof",g,new Vector3(0,height+.15f,0),new Vector3(width+1,.3f,depth+1),Cream,true);
            Box("Garage rooftop badge",g,new Vector3(0,height+.32f,0),new Vector3(10,.03f,2.3f),Ink);
            Text("117° AUTO CARE",g,new Vector3(0,height+.35f,0),.52f,Turquoise,Quaternion.Euler(90,0,0));
            Box("Service bay lintel",g,new Vector3(0,height-.48f,-depth*.5f),new Vector3(5.1f,.95f,.4f),Ink,true);
            Box("Closed garage roll-up door",g,new Vector3(0,(height-.95f)*.5f,-depth*.5f),
                new Vector3(5.05f,height-.95f,.28f),new Color(.34f,.38f,.37f),true);
            for(int stripe=1;stripe<9;stripe++)
                Box("Garage door seam",g,new Vector3(0,stripe*(height-.95f)/9f,-depth*.5f-.16f),
                    new Vector3(5.04f,.025f,.02f),Ink);
            Box("Garage sign backing",g,new Vector3(0,height+1.05f,-depth*.5f-.22f),new Vector3(12,1.55f,.18f),Ink);
            Text("117° AUTO CARE",g,new Vector3(0,height+1.05f,-depth*.5f-.34f),.66f,Turquoise);
            Text("GARAGE  •  SERVICE",g,new Vector3(0,height-.46f,-depth*.5f-.24f),.27f,Cream);
            Box("Garage forecourt",g,new Vector3(0,-.055f,-depth*.5f-5),new Vector3(10,.08f,10),new Color(.43f,.41f,.35f));
            return g;
        }
        public static Transform JunkCar(Transform parent,Vector3 pos,Color paint,float yaw=0)
        {
            Transform g=Group("Unclear title history",parent,pos); g.localRotation=Quaternion.Euler(0,yaw,0);
            Use(Box("Abandoned chassis",g,new Vector3(0,.4f,0),new Vector3(1.8f,.7f,3.9f),paint),Reflective(paint,SunGlint.Paint,.45f,.1f,2));
            Box("Crushed cabin",g,new Vector3(0,.92f,-.25f),new Vector3(1.58f,.5f,1.8f),Rust);
            Use(Box("Opaque windshield",g,new Vector3(0,1.05f,.68f),new Vector3(1.36f,.27f,.03f),Ink),Glass(Ink,1.8f));
            for(int x=-1;x<=1;x+=2) for(int z=-1;z<=1;z+=2) { var tire=Cylinder("Flat tyre",g,new Vector3(x*.93f,.3f,z*1.25f),.34f,.22f,Ink); tire.transform.localRotation=Quaternion.Euler(0,0,90); }
            BoundsCollider(g,new Vector3(0,.6f,0),new Vector3(2,1.2f,4)); MakeBreakable(g,65,true,ExplosionKind.Vehicle,75); return g;
        }
        public static Transform PowerPole(Transform parent,Vector3 pos)
        {
            Transform g=Group("Utility pole",parent,pos);
            Cylinder("Creosote pole",g,new Vector3(0,4.7f,0),.18f,9.4f,new Color(.24f,.17f,.11f));
            Box("Crossarm",g,new Vector3(0,8.6f,0),new Vector3(3.1f,.18f,.23f),Rust);
            for(int i=-1;i<=1;i++) Use(Cylinder("Insulator",g,new Vector3(i*1.22f,8.88f,0),.12f,.4f,Cream),Reflective(Cream,SunGlint.Plastic,.88f,0,.8f));
            Use(Cylinder("Transformer",g,new Vector3(.5f,7.1f,0),.43f,1.4f,new Color(.41f,.51f,.48f)),Reflective(new Color(.41f,.51f,.48f),SunGlint.Metal,.55f,.6f,1.4f));
            BoundsCollider(g,new Vector3(0,4.7f,0),new Vector3(.4f,9.4f,.4f));
            // A single round or vehicle impact knocks it over intact; the connected spans handle the cables.
            MakeBreakable(g,1,false,ExplosionKind.Electrical,25);
            var damage=g.GetComponent<DestructionSystem>();damage.MakeTopple(420);damage.MakePlowable();
            return g;
        }
        public static Transform PortaPotty(Transform parent,Vector3 pos)
        {
            Transform g=Group("Portable consequences",parent,pos);
            Use(Box("Plastic shell",g,new Vector3(0,1.2f,0),new Vector3(1.5f,2.4f,1.5f),Turquoise),Reflective(Turquoise,SunGlint.Plastic,.6f,0,1.3f));
            Box("Pale lid",g,new Vector3(0,2.48f,0),new Vector3(1.65f,.22f,1.65f),Cream);
            Box("Door",g,new Vector3(0,1.17f,-.77f),new Vector3(1.16f,2.07f,.04f),new Color(.09f,.43f,.42f));
            Text("EXECUTIVE\nSUITE",g,new Vector3(0,1.64f,-.80f),.19f,Cream);
            BoundsCollider(g,new Vector3(0,1.2f,0),new Vector3(1.5f,2.6f,1.5f)); MakeBreakable(g,22,false,ExplosionKind.Gasoline,50); return g;
        }
    }
}
