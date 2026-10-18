using System;
using System.Collections.Generic;
using UnityEngine;

namespace MadeInArizona
{
    /// <summary>Small equipment glyphs and a top-down service-manual plate for each actual car.</summary>
    public static class EquipmentIllustrations
    {
        static readonly Dictionary<string,Texture2D> icons=new Dictionary<string,Texture2D>();
        static readonly Dictionary<string,Texture2D> manuals=new Dictionary<string,Texture2D>();
        static readonly Color32 Ink=new Color32(36,40,37,255);
        static readonly Color32 Faint=new Color32(143,142,131,255);
        static readonly Color32 Paper=new Color32(247,245,234,255);

        public static Texture Icon(VehiclePart part,EquipmentSlot slot,string vehicleId)
        {
            if(part&&part.icon)return part.icon.texture;
            string key=part?part.id:"factory/"+vehicleId+"/"+slot;
            if(!icons.TryGetValue(key,out var texture))icons[key]=texture=CreateIcon(key,slot,part?.specialBehavior);
            return texture;
        }
        public static Texture2D Manual(VehicleDefinition car)
        {
            if(!manuals.TryGetValue(car.id,out var texture))manuals[car.id]=texture=CreateManual(car);
            return texture;
        }

        static uint Hash(string value)
        {
            uint hash=2166136261;
            foreach(char letter in value){hash^=letter;hash*=16777619;}
            return hash;
        }
        static Texture2D Finish(Painter painter,string name)
        {
            var texture=new Texture2D(painter.Width,painter.Height,TextureFormat.RGBA32,false)
            {name=name,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.DontSave};
            texture.SetPixels32(painter.Pixels);texture.Apply(false,true);return texture;
        }
        static Texture2D CreateIcon(string key,EquipmentSlot slot,string behavior)
        {
            const int size=56;
            var p=new Painter(size,size);
            uint hash=Hash(key);
            float hue=((int)slot*.125f+(hash%83)/480f)%1f;
            Color accent=Color.HSVToRGB(hue,.50f,.80f);
            var bright=(Color32)Color.Lerp(accent,Color.white,.42f);
            var shadow=(Color32)Color.Lerp(accent,Color.black,.67f);
            var metal=new Color32(220,203,157,255);
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float glint=Mathf.Clamp01(1f-Mathf.Abs(x-y*1.15f-5)/38f)*.18f;
                float noise=(((hash^(uint)(x*73856093)^(uint)(y*19349663))&7)/7f)*.055f;
                p.Pixel(x,y,(Color32)Color.Lerp(shadow,accent,.16f+glint+noise));
            }
            p.Box(1,1,54,54,new Color32(53,42,29,255));
            p.Box(3,3,50,50,metal);
            p.Box(5,5,46,46,new Color32(58,53,43,255));
            p.Pixel(7,7,bright);p.Pixel(48,7,bright);p.Pixel(7,48,bright);p.Pixel(48,48,bright);
            for(int i=0;i<3;i++)p.Line(9,45-i,45-i,9,shadow);
            switch(slot)
            {
                case EquipmentSlot.Engine:
                    p.Rect(14,18,28,23,metal);p.Rect(19,13,18,6,bright);p.Rect(18,24,20,11,bright);
                    p.Circle(23,29,3,shadow);p.Circle(33,29,3,shadow);p.Line(11,22,14,22,bright);p.Line(42,33,46,33,bright);
                    break;
                case EquipmentSlot.Transmission:
                    p.Circle(28,28,15,metal);p.Circle(28,28,9,bright);p.Circle(28,28,4,shadow);
                    for(int i=0;i<8;i++){float a=i*Mathf.PI/4;p.Line(28+Mathf.RoundToInt(Mathf.Cos(a)*15),28+Mathf.RoundToInt(Mathf.Sin(a)*15),
                        28+Mathf.RoundToInt(Mathf.Cos(a)*20),28+Mathf.RoundToInt(Mathf.Sin(a)*20),metal);}
                    break;
                case EquipmentSlot.Drivetrain:
                    p.Line(11,28,45,28,metal,3);p.Circle(28,28,13,bright);p.Circle(28,28,5,shadow);
                    p.Circle(13,28,5,metal);p.Circle(43,28,5,metal);p.Line(28,13,28,43,metal,2);
                    break;
                case EquipmentSlot.Tires:
                    p.Circle(28,28,19,metal,4);p.Circle(28,28,11,bright,2);p.Circle(28,28,4,metal);
                    for(int i=0;i<6;i++){float a=i*Mathf.PI/3;p.Line(28+Mathf.RoundToInt(Mathf.Cos(a)*6),28+Mathf.RoundToInt(Mathf.Sin(a)*6),
                        28+Mathf.RoundToInt(Mathf.Cos(a)*11),28+Mathf.RoundToInt(Mathf.Sin(a)*11),metal,2);}
                    break;
                case EquipmentSlot.Suspension:
                    p.Line(16,12,40,12,metal,3);p.Line(16,44,40,44,metal,3);p.Line(28,12,28,44,bright);
                    for(int i=0;i<5;i++)p.Line(i%2==0?17:39,17+i*5,i%2==0?39:17,22+i*5,metal,2);
                    break;
                case EquipmentSlot.Body:
                    p.Line(28,10,43,17,metal,3);p.Line(43,17,40,37,metal,3);p.Line(40,37,28,47,metal,3);
                    p.Line(28,47,16,37,metal,3);p.Line(16,37,13,17,metal,3);p.Line(13,17,28,10,metal,3);
                    p.Line(28,17,28,40,bright,2);p.Line(19,27,37,27,bright,2);
                    break;
                default:
                    if(behavior=="light_bar")
                    {p.Rect(11,24,34,9,metal);p.Rect(15,20,7,17,bright);p.Rect(34,20,7,17,bright);}
                    else if(behavior=="tire_spikes")
                    {for(int i=0;i<5;i++){int x=13+i*8;p.Line(x,39,x+4,14,metal,2);p.Line(x+4,14,x+8,39,bright,2);}}
                    else if(behavior=="repair_foam")
                    {p.Rect(23,13,10,30,metal);p.Rect(13,23,30,10,metal);p.Rect(26,16,4,24,bright);}
                    else if(behavior=="drift_n2o")
                    {p.Line(27,10,18,30,bright,4);p.Line(18,30,31,27,bright,4);p.Line(31,27,24,46,metal,4);}
                    else
                    {p.Circle(28,28,16,metal,2);p.Line(28,11,28,45,bright,2);p.Line(11,28,45,28,bright,2);p.Circle(28,28,5,metal);}
                    break;
            }
            return Finish(p,"Equipment icon "+key);
        }

        static Texture2D CreateManual(VehicleDefinition car)
        {
            // The plate follows a service drawing: the car runs left to right, front first,
            // and each numbered leader meets the equipment card beside that row.
            const int w=400,h=380;
            var p=new Painter(w,h);
            uint seed=Hash(car.id);
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)
            {byte grain=(byte)((seed^(uint)((x/5)*73856093)^(uint)((y/5)*19349663))&2);
                p.Pixel(x,y,new Color32((byte)(Paper.r-grain),(byte)(Paper.g-grain),(byte)(Paper.b-grain),255));}
            p.Rect(4,4,w-8,h-8,Faint);p.Rect(7,7,w-14,h-14,Faint);
            p.Line(15,38,385,38,Ink);p.Line(15,346,385,346,Ink);
            for(int x=75;x<=325;x+=25){p.Line(x,31,x,38,Faint);p.Line(x,346,x,352,Faint);}
            p.Line(70,19,112,19,Ink);p.Line(70,19,78,15,Ink);p.Line(70,19,78,23,Ink);
            bool truck=Array.IndexOf(car.chassisTags,"truck")>=0;
            bool compact=Array.IndexOf(car.chassisTags,"compact")>=0;
            bool roadster=Array.IndexOf(car.chassisTags,"roadster")>=0;
            int half=Mathf.Clamp(Mathf.RoundToInt(67*car.trackWidth/1.5f),57,78);
            if(compact)half-=4;if(truck)half+=5;if(roadster)half-=4;
            int top=190-half,bottom=190+half;
            int frontWheel=Mathf.Clamp(Mathf.RoundToInt(96-(car.wheelbase-2.4f)*12),82,109);
            int rearWheel=Mathf.Clamp(Mathf.RoundToInt(296+(car.wheelbase-2.4f)*12),279,320);
            int roofBack=truck?225:roadster?258:282;
            // Wheelbase, track and body type are taken from the actual vehicle definition.
            foreach(int axle in new[]{frontWheel,rearWheel})
            {
                p.Line(axle,top-10,axle,bottom+10,Faint);
                foreach(int y in new[]{top-23,bottom+5})
                {
                    p.Rect(axle-26,y,52,19,Ink,2);p.Line(axle-20,y+3,axle+20,y+3,Faint);
                    p.Line(axle-20,y+15,axle+20,y+15,Faint);
                    for(int tread=-18;tread<=18;tread+=9)p.Line(axle+tread,y+4,axle+tread+3,y+14,Faint);
                    p.Circle(axle,y+9,3,Ink);
                }
            }
            // Bumper, lamps, fenders, side rails, boot and panel fasteners.
            p.Line(48,top+20,62,top+5,Ink,2);p.Line(62,top+5,340,top+5,Ink,2);
            p.Line(340,top+5,357,top+19,Ink,2);p.Line(357,top+19,357,bottom-19,Ink,2);
            p.Line(357,bottom-19,340,bottom-5,Ink,2);p.Line(340,bottom-5,62,bottom-5,Ink,2);
            p.Line(62,bottom-5,48,bottom-20,Ink,2);p.Line(48,bottom-20,48,top+20,Ink,2);
            p.Line(43,top+25,43,bottom-25,Ink);p.Line(39,top+31,39,bottom-31,Faint);
            p.Line(362,top+26,362,bottom-26,Ink);p.Line(367,top+32,367,bottom-32,Faint);
            p.Rect(49,top+17,12,16,Ink);p.Rect(49,bottom-33,12,16,Ink);
            p.Rect(344,top+18,11,14,Ink);p.Rect(344,bottom-32,11,14,Ink);
            p.Line(66,top+12,330,top+12,Faint);p.Line(66,bottom-12,330,bottom-12,Faint);
            foreach(int x in new[]{74,138,roofBack+8,334})
            {p.Circle(x,top+12,2,Ink);p.Circle(x,bottom-12,2,Ink);}
            // Hood opening, radiator, belts, cylinder banks and fluid connections.
            p.Line(63,top+24,140,top+24,Faint);p.Line(63,bottom-24,140,bottom-24,Faint);
            p.Line(140,top+24,140,bottom-24,Ink);p.Line(70,top+34,70,bottom-34,Faint);
            p.Rect(72,190-35,54,70,Ink);p.Rect(79,190-28,40,56,Faint);
            for(int bank=-1;bank<=1;bank+=2)
            {
                int y=190+bank*20;
                p.Rect(84,y-8,28,15,Ink);for(int fin=0;fin<3;fin++)p.Line(90+fin*8,y-5,90+fin*8,y+4,Faint);
            }
            p.Circle(76,190,10,Ink);p.Circle(76,190,5,Faint);
            p.Line(126,172,139,164,Faint);p.Line(126,208,139,216,Faint);
            // Gearbox, prop shaft, rear differential and exhaust are visible through a cutaway.
            p.Rect(148,175,37,30,Ink);p.Circle(150,190,8,Faint);p.Circle(181,190,6,Faint);
            p.Line(185,190,rearWheel-12,190,Ink,2);p.Circle(rearWheel,190,13,Ink,2);
            p.Circle(rearWheel,190,5,Faint);p.Line(rearWheel,top+19,rearWheel,bottom-19,Faint);
            p.Line(124,217,156,228,Faint);p.Line(156,228,310,228,Faint);
            p.Rect(307,223,27,10,Ink);p.Line(334,228,351,228,Faint);
            if(truck)
            {
                p.Line(146,top+19,roofBack,top+19,Ink,2);p.Line(146,bottom-19,roofBack,bottom-19,Ink,2);
                p.Line(roofBack,top+19,roofBack,bottom-19,Ink,2);
                p.Rect(roofBack+10,top+19,110,bottom-top-38,Ink);
                for(int slat=0;slat<4;slat++)p.Line(roofBack+21+slat*23,top+25,roofBack+21+slat*23,bottom-25,Faint);
            }
            else
            {
                p.Line(147,top+20,roofBack-8,top+20,Ink,2);p.Line(147,bottom-20,roofBack-8,bottom-20,Ink,2);
                p.Line(147,top+20,153,top+32,Faint);p.Line(147,bottom-20,153,bottom-32,Faint);
                p.Line(roofBack-8,top+20,roofBack,top+35,Ink);p.Line(roofBack-8,bottom-20,roofBack,bottom-35,Ink);
                p.Line(roofBack,top+35,roofBack,bottom-35,Ink,2);
                p.Line(153,top+32,153,bottom-32,Ink);p.Line(153,top+32,roofBack-15,top+32,Faint);
                p.Line(153,bottom-32,roofBack-15,bottom-32,Faint);
                p.Line(158,top+41,roofBack-13,top+41,Faint);p.Line(158,bottom-41,roofBack-13,bottom-41,Faint);
                p.Line(roofBack+8,top+25,roofBack+8,bottom-25,Faint);
                p.Line(roofBack+17,top+21,roofBack+17,bottom-21,Faint);
            }
            // Seats, cushions, wheel and dashboard give the cabin the open cutaway of a manual plate.
            p.Line(163,top+42,163,bottom-42,Ink);p.Line(168,top+46,168,bottom-46,Faint);
            foreach(int y in new[]{top+56,bottom-56})
            {
                p.Rect(187,y-16,31,32,Ink);p.Rect(192,y-11,21,22,Faint);
                p.Line(218,y-12,227,y-18,Ink);p.Line(218,y+12,227,y+18,Ink);
                if(!truck&&!roadster){p.Rect(231,y-16,30,32,Ink);p.Line(235,y-11,257,y-11,Faint);}
            }
            p.Circle(175,top+54,11,Ink);p.Circle(175,top+54,3,Ink);
            p.Line(167,top+54,183,top+54,Faint);p.Line(175,top+46,175,top+63,Faint);
            // Six service callouts line up with the six mechanical cards around the plate.
            Callout(p,24,70,1,68,70,103,178);
            Callout(p,376,70,2,340,70,172,190);
            Callout(p,24,190,3,53,190,frontWheel,top-13);
            Callout(p,376,190,4,344,190,rearWheel,190);
            Callout(p,24,310,5,67,310,frontWheel,bottom+13);
            Callout(p,376,310,6,343,310,330,bottom-14);
            // Registration marks and a simple revision index belong to the plate, not the UI.
            for(int i=0;i<6;i++)p.Line(137+i*11,360,143+i*11,360,Ink);
            for(int i=0;i<4;i++)p.Circle(335+i*10,360,2,i==(seed%4)?Ink:Faint);
            return Finish(p,"Service manual "+car.id);
        }

        static void Callout(Painter p,int x,int y,int number,int elbowX,int elbowY,int targetX,int targetY)
        {
            p.Circle(x,y,13,Ink);p.Number(x-7,y-5,number);
            p.Line(x+(x<200?13:-13),y,elbowX,elbowY,Ink);
            p.Line(elbowX,elbowY,targetX,targetY,Faint);
            p.Circle(targetX,targetY,3,Ink);
        }

        sealed class Painter
        {
            public readonly int Width,Height;
            public readonly Color32[] Pixels;
            public Painter(int width,int height){Width=width;Height=height;Pixels=new Color32[width*height];}
            public void Pixel(int x,int y,Color32 color){if(x>=0&&x<Width&&y>=0&&y<Height)Pixels[(Height-1-y)*Width+x]=color;}
            public void Line(int x0,int y0,int x1,int y1,Color32 color,int weight=1)
            {
                int dx=Math.Abs(x1-x0),dy=Math.Abs(y1-y0),sx=x0<x1?1:-1,sy=y0<y1?1:-1,error=dx-dy;
                while(true)
                {
                    for(int ox=-weight/2;ox<=weight/2;ox++)for(int oy=-weight/2;oy<=weight/2;oy++)Pixel(x0+ox,y0+oy,color);
                    if(x0==x1&&y0==y1)break;
                    int doubled=2*error;
                    if(doubled>-dy){error-=dy;x0+=sx;}
                    if(doubled<dx){error+=dx;y0+=sy;}
                }
            }
            public void Rect(int x,int y,int w,int h,Color32 color,int weight=1)
            {for(int i=0;i<weight;i++){Line(x+i,y+i,x+w-i-1,y+i,color);Line(x+i,y+h-i-1,x+w-i-1,y+h-i-1,color);
                Line(x+i,y+i,x+i,y+h-i-1,color);Line(x+w-i-1,y+i,x+w-i-1,y+h-i-1,color);}}
            public void Box(int x,int y,int w,int h,Color32 color)
            {for(int py=y;py<y+h;py++)for(int px=x;px<x+w;px++)Pixel(px,py,color);}
            public void Circle(int cx,int cy,int radius,Color32 color,int weight=1)
            {
                for(int ring=0;ring<weight;ring++)
                    for(int degree=0;degree<360;degree++)
                    {float angle=degree*Mathf.Deg2Rad;Pixel(cx+Mathf.RoundToInt(Mathf.Cos(angle)*(radius-ring)),
                        cy+Mathf.RoundToInt(Mathf.Sin(angle)*(radius-ring)),color);}
            }
            public void Number(int x,int y,int value)
            {
                Digit(x,y,0);Digit(x+8,y,value%10);
            }
            void Digit(int x,int y,int value)
            {
                string[] shapes={"111101101101111","010110010010111","111001111100111",
                    "111001111001111","101101111001001","111100111001111","111100111101111",
                    "111001001001001","111101111101111","111101111001111"};
                string shape=shapes[Mathf.Clamp(value,0,9)];
                for(int row=0;row<5;row++)for(int column=0;column<3;column++)
                    if(shape[row*3+column]=='1')Box(x+column*2,y+row*2,2,2,Ink);
            }
        }
    }
}
