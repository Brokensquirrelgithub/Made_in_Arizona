using UnityEngine;
using static MadeInArizona.WorldArt;

namespace MadeInArizona
{
    /// <summary>Reusable original prop constructors used by levels and the editor prefab exporter.</summary>
    public static class RoadsideProps
    {
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
            BoundsCollider(g,new Vector3(0,1.5f,0),new Vector3(.7f,3,.7f));
            if(destructible) MakeBreakable(g,24,false,ExplosionKind.Gasoline,5); return g;
        }
        public static Transform Rock(Transform parent,Vector3 pos,Vector3 scale,float hue=0)
        {
            var g=Shape("Faceted sandstone",parent,PrimitiveType.Sphere,pos,scale,new Color(.47f+hue,.26f+hue*.6f,.17f+hue*.3f),true).transform;
            g.localRotation=Quaternion.Euler(13,scale.x*28,24);
            MakeBreakable(g,Mathf.Clamp(12+scale.magnitude*3,16,30),false,ExplosionKind.Ammunition,3);
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
                Text("SUNBURN PROPANE",g,new Vector3(0,1.5f,-.91f),.3f,Rust);
                BoundsCollider(g,new Vector3(0,1.35f,0),new Vector3(5.6f,2.4f,1.8f));
            }
            else
            {
                Use(Shape("Tank",g,PrimitiveType.Capsule,new Vector3(0,.63f,0),new Vector3(.62f,.62f,.62f),Cream),Reflective(Cream,SunGlint.Paint,.7f,0,1.6f));
                Cylinder("Foot",g,new Vector3(0,.08f,0),.32f,.16f,Ink);
                Cylinder("Valve",g,new Vector3(0,1.29f,0),.08f,.19f,Rust);
                Box("Warning band",g,new Vector3(0,.63f,-.312f),new Vector3(.35f,.19f,.025f),Orange);
                BoundsCollider(g,new Vector3(0,.6f,0),new Vector3(.7f,1.4f,.7f));
            }
            MakeBreakable(g,large?65:18,true,ExplosionKind.Propane,large?150:45); return g;
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
            BoundsCollider(g,new Vector3(0,4.7f,0),new Vector3(.4f,9.4f,.4f)); MakeBreakable(g,60,true,ExplosionKind.Electrical,65); return g;
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
