using UnityEngine;
using UnityEngine.InputSystem;
namespace MadeInArizona
{
    public sealed partial class GameUI
    {
        bool worldMap;
        string worldSeed;
        float worldSize;
        Vector3? mapWaypoint;
        public void OpenWorldMap(){if(!GeneratedWorld.Active)return;if(game.IsPlaying)game.Pause();worldMap=true;settings=false;devMenu=false;}
        void WorldMenuInput()
        {
            if(Keyboard.current!=null&&Keyboard.current.mKey.wasPressedThisFrame&&GeneratedWorld.Active)
            {if(worldMap){worldMap=false;game.Resume();}else if(game.IsPlaying||game.State==GameState.Paused)OpenWorldMap();}
        }
        void DrawMainMenu()
        {
            Rect(0,0,width,height,new Color(.025f,.04f,.045f,.85f));
            float x=width*.08f,y=height*.13f;
            Text(x,y,800,75,"MADE IN ARIZONA",54,Cream,true);
            Text(x,y+83,690,70,"A repair shop. A new field mechanic. An entire state of bad decisions.",24,Muted);
            float panelW=Mathf.Min(740,width-x-40);
            Rect(x,y+179,panelW,440,Ink);
            Text(x+25,y+201,panelW-50,30,"SEEDED CAMPAIGN WORLD",21,Orange,true);
            if(worldSeed==null){worldSeed=game.WorldConfig.seed.ToString();worldSize=game.WorldConfig.size;}
            Text(x+25,y+254,180,25,"WORLD SEED",15,Muted);
            worldSeed=GUI.TextField(new Rect(x+235,y+248,250,36),worldSeed,11);
            if(Button(x+505,y+248,170,36,"RANDOM SEED"))worldSeed=Random.Range(1,int.MaxValue).ToString();
            Text(x+25,y+308,180,25,"MAP WIDTH",15,Muted);
            worldSize=GUI.HorizontalSlider(new Rect(x+235,y+311,320,26),worldSize,800,3200);
            Text(x+580,y+305,120,28,(worldSize/1000).ToString("0.0")+" km",19,Lime,true);
            Text(x+25,y+355,panelW-50,64,"Sonoran desert • salt flats • riparian washes • pine highlands • craggy mountains. Towns share blueprints; roads and wilderness follow your seed.",16,Muted);
            bool valid=int.TryParse(worldSeed,out int seed);
            if(Button(x+25,y+438,panelW-50,56,"START CAMPAIGN  /  GENERATE WORLD",true,valid))game.StartCampaign(seed,Mathf.Round(worldSize/50)*50);
            if(Button(x+25,y+512,310,48,"GARAGE / SAVED PROGRESSION"))game.ReturnToGarage();
            if(Button(x+355,y+512,panelW-380,48,"QUIT"))Application.Quit();
            Text(x+25,y+575,panelW-50,38,"Starting a world preserves garage upgrades and completed jobs.",13,Muted);
            Text(x,height-65,width-x-40,40,"JSON world editing: "+WorldConfigStore.Path,12,Muted);
            if(WorldConfigStore.LastError!=null)Text(x,height-105,width-x-40,35,WorldConfigStore.LastError,14,Orange);
        }
        void DrawGeneration()
        {
            Rect(0,0,width,height,Ink);
            Text(60,height*.4f,width-120,65,"GENERATING ARIZONA",42,Cream,true);
            Text(60,height*.4f+80,width-120,70,game.GenerationStatus,23,Orange);
            Text(60,height*.4f+160,width-120,45,"Terrain, rivers, town blueprints, roads and off-road discoveries…",17,Muted);
        }
        Vector2 WorldMapPoint(Vector3 point,Rect area)
        {
            var b=GeneratedWorld.Active.WorldBounds;
            return new Vector2(area.x+Mathf.InverseLerp(b.min.x,b.max.x,point.x)*area.width,area.y+(1-Mathf.InverseLerp(b.min.z,b.max.z,point.z))*area.height);
        }
        void RenderWorldMap(Rect area,bool full)
        {
            var world=GeneratedWorld.Active;if(!world)return;
            GUI.DrawTexture(area,world.MapTexture,ScaleMode.StretchToFill,true);
            foreach(var pin in world.Pins)
            {
                bool town=pin.kind=="town";
                if(!town&&!pin.discovered)continue;
                var p=WorldMapPoint(pin.position,area);
                Rect(p.x-3,p.y-3,6,6,town?Cream:pin.requiredTier>WorldExploration.CurrentTier?Orange:Blue);
                if(full)Text(p.x+7,p.y-9,170,22,pin.label,12,town?Cream:Blue,true);
            }
            foreach(var vehicle in VehicleController.Active)
            {
                if(!vehicle||vehicle.IsPlayer||vehicle.Damage.IsDead)continue;
                var ai=vehicle.GetComponent<EnemyAI>();if(!ai)continue;
                var p=WorldMapPoint(vehicle.transform.position,area);
                if(area.Contains(p))Rect(p.x-3,p.y-3,6,6,ai.IsFriendly?Blue:FactionRules.Accent(ai.Faction));
            }
            if(game.Player){var p=WorldMapPoint(game.Player.transform.position,area);Rect(p.x-5,p.y-5,10,10,Lime);}
            if(game.Mission!=null){var p=WorldMapPoint(game.Mission.ObjectivePosition,area);Rect(p.x-4,p.y-4,8,8,Orange);}
            if(mapWaypoint.HasValue){var p=WorldMapPoint(mapWaypoint.Value,area);Text(p.x-8,p.y-13,24,25,"+",22,Blue,true);}
        }
        void DrawGeneratedMinimap(float x,float y,float size)
        {
            Rect(x,y,size,size,Ink);
            Text(x+10,y+8,size-20,25,"ARIZONA / M MAP",12,Muted,true);
            RenderWorldMap(new Rect(x+10,y+35,size-20,size-45),false);
            if(Button(x,y+size+3,size,25,"OPEN MAP / M"))OpenWorldMap();
        }
        void DrawWorldMap()
        {
            Rect(0,0,width,height,new Color(.035f,.055f,.06f,1));
            Text(30,20,width-320,45,"ARIZONA • SEED "+game.WorldConfig.seed,29,Cream,true);
            if(Button(width-260,20,230,42,"CLOSE MAP / M",true)){worldMap=false;game.Resume();}
            float size=Mathf.Min(height-150,width-490);
            var area=new Rect(35,92,size*.9f,size);
            RenderWorldMap(area,true);
            float x=area.xMax+35;
            Text(x,100,400,40,"OFF THE ROAD",24,Orange,true);
            Text(x,148,390,100,"Towns are charted. Secrets appear as you explore. Orange locations demand better equipment. Click the map to place a waypoint.",17,Muted);
            Text(x,261,390,36,"VEHICLE CAPABILITY: TIER "+WorldExploration.CurrentTier,19,Lime,true);
            Text(x,309,390,94,"South: Sonoran cactus and flats\nRiver: riparian vegetation\nNorth: pine forest and mountain ridges",16,Cream);
            DrawWorldCrafting(x,420);
            Text(35,height-42,width-70,28,"Green: you    Orange: mission    Cream: towns    Blue: discoveries / waypoint",14,Muted);
            if(Event.current.type==EventType.MouseDown&&area.Contains(Event.current.mousePosition))
            {
                var b=GeneratedWorld.Active.WorldBounds;var m=Event.current.mousePosition;
                var point=new Vector3(Mathf.Lerp(b.min.x,b.max.x,(m.x-area.x)/area.width),0,Mathf.Lerp(b.max.z,b.min.z,(m.y-area.y)/area.height));
                if(GeneratedWorld.Contains(point)){point.y=GeneratedWorld.HeightAt(point);mapWaypoint=point;}
                Event.current.Use();
            }
        }
        void DrawWorldCrafting(float x,float y)
        {
            Text(x,y,390,30,"FIELD WEAPON WORKBENCH",21,Orange,true);
            Text(x,y+38,390,32,WorldExploration.Inventory,15,Blue);
            for(int i=0;i<WorldExploration.RecipeDescriptions.Length;i++)
            {
                bool crafted=game.Save.collectibles.Contains("world-craft:"+i);
                Text(x,y+88+i*82,380,40,WorldExploration.RecipeDescriptions[i],14,Cream);
                if(Button(x,y+130+i*82,360,30,crafted?"FITTED":"CRAFT UPGRADE",false,!crafted))WorldExploration.TryCraft(i);
            }
            if(game.NotificationUntil>Time.unscaledTime)Text(x,y+342,390,64,game.Notification,14,Lime);
        }
    }
}
