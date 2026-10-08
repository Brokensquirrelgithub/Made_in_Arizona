using UnityEngine;
namespace MadeInArizona
{
    public sealed partial class GameUI
    {
        bool devMenu, devDirty;
        int devTab;
        Vector2 devScroll;
        float devSaveAt;
        readonly string[] devTabs={"DRIVING","DRIFT","COMBAT","ENGINE","SLOT 1","SLOT 2","CAMERA","LIGHT & COLOR","REFLECTIONS","DIRT & SKY"};
        float StylesRadioHeight(string message,float w) => Style(14,Cream).CalcHeight(new GUIContent(message),w)+5;
        public void OpenDevMenu() { if(game.IsPlaying)game.Pause();devMenu=true;settings=false; }
        void MarkDevDirty()
        {
            DevTuning.Apply();
            devDirty=true;
            devSaveAt=Time.unscaledTime+.35f;
        }
        void SaveDev()
        {
            if(!devDirty)return;
            SaveSystem.Save(game.Save);devDirty=false;
        }
        void TickDevSave() { if(devDirty && Time.unscaledTime>=devSaveAt)SaveDev(); }
        void OnApplicationQuit(){SaveDev();}
        void OnApplicationFocus(bool focused){if(!focused)SaveDev();}

        void DrawDevMenu()
        {
            float w=900,h=Mathf.Min(height-40,820),x=width-w-20,y=20;
            Rect(x,y,w,h,new Color(.055f,.081f,.09f,1));
            Text(x+24,y+18,w-48,38,"LIVE DEV TUNING",27,Cream,true);
            bool guest=CoopSession.IsRemoteClient;
            if(Button(x+w-250,y+22,226,34,"+10,000 SCRAP & $",false,!guest))
            {game.Save.salvage+=10000;game.Save.money+=10000;SaveSystem.Save(game.Save);game.Notify("DEV • +10,000 SCRAP AND $10,000");}
            string tab=devTabs[devTab];
            var player=game.Player;
            string carId=player&&player.Definition?player.Definition.id:null;
            var coop=CoopSession.Instance;
            bool carEditable=coop==null||coop.CanEditCar(carId);
            var car=string.IsNullOrEmpty(carId)?null:BalanceTuning.Car(carId);
            WeaponDefinition weapon=tab=="SLOT 1"?player&&player.Weapons?player.Weapons.GarageWeapon:null:
                tab=="SLOT 2"?player&&player.Weapons?player.Weapons.FieldWeapon:null:null;
            bool weaponEditable=!weapon||coop==null||coop.CanEditWeapon(weapon.id);
            // Name exactly what the sliders on this tab change: which car, which weapon, or whose settings.
            bool carTab=tab=="DRIVING"||tab=="DRIFT"||tab=="ENGINE",slotTab=tab=="SLOT 1"||tab=="SLOT 2";
            string editing=tab=="COMBAT"?"WORLD BALANCE":
                carTab?car==null?"NO CAR SELECTED":player.Definition.displayName.ToUpperInvariant():
                slotTab?weapon?weapon.displayName.ToUpperInvariant():"EMPTY "+tab:"THIS PC'S "+tab;
            string detail=tab=="COMBAT"?"Enemy health, damage and pacing for everyone in the session • host controlled":
                carTab?car==null?"Pick a car in the garage to tune its profile":
                    (carEditable?"This car's "+tab.ToLowerInvariant()+" profile • saved for this car only":"Another player owns this car's tuning this session • read only")+
                    (tab=="DRIVING"?" (prop impact damage is world balance)":""):
                slotTab?weapon?(tab=="SLOT 1"?"Garage weapon (slot 1)":"Field weapon (slot 2)")+
                    (weaponEditable?" • this weapon's balance profile, wherever it is equipped":" • another player owns this weapon's tuning • read only"):
                    tab=="SLOT 1"?"No garage weapon equipped":"Pick up a field weapon to tune slot 2":
                "Local presentation • saved on this PC only, never shared";
            Text(x+24,y+58,w-48,26,"EDITING  ›  "+editing,19,carTab||slotTab?Lime:tab=="COMBAT"?Orange:Cream,true);
            Text(x+24,y+84,w-48,22,detail,13,Muted);
            float tabW=(w-40)/5;
            var garageWeapon=player&&player.Weapons?player.Weapons.GarageWeapon:null;
            var fieldWeapon=player&&player.Weapons?player.Weapons.FieldWeapon:null;
            for(int i=0;i<devTabs.Length;i++)
            {
                string label=devTabs[i];
                if(label=="SLOT 1")label="SLOT 1 • "+ShortName(garageWeapon);
                else if(label=="SLOT 2")label="SLOT 2 • "+ShortName(fieldWeapon);
                if(Button(x+20+(i%5)*tabW,y+109+(i/5)*40,tabW-8,34,label,devTab==i,size:label.Length>16?10:12))
                {devTab=i;devScroll=Vector2.zero;}
            }
            bool engineTab=tab=="ENGINE";
            if(engineTab && car!=null)
            {
                Text(x+24,y+205,220,24,"ENGINE MODEL A/B/C",15,Cream,true);
                int model=car.enginePhysical?2:car.enginePreserveEdges?1:0;
                string[] models={"A • ORIGINAL","B • PRESERVED EDGES","C • PHYSICAL"};
                bool previous=GUI.enabled;GUI.enabled=previous&&carEditable;
                for(int m=0;m<models.Length;m++)
                    if(Button(x+245+m*212,y+201,205,32,models[m],model==m,size:14) && model!=m)
                    {var own=BalanceTuning.LocalCar(carId);own.enginePhysical=m==2;if(m<2)own.enginePreserveEdges=m==1;MarkDevDirty();}
                GUI.enabled=previous;
            }
            int top=engineTab?244:202;
            if(tab=="SLOT 1"||tab=="SLOT 2")DrawWeaponBalance(x+20,y+top,w-40,h-(top+95),weapon,weaponEditable);
            else DrawDevControls(x+20,y+top,w-40,h-(top+95),tab,carId,carEditable);
            Text(x+24,y+h-85,w-48,23,SaveSystem.LastError!=null?SaveSystem.LastError:devDirty?"Applying…":
                coop&&coop.HasTuningDraft?"Session draft • host chooses Save or Discard on Leave Co-op":
                "Saved locally • changes apply while driving",12,Muted);
            if(Button(x+20,y+h-53,210,36,"RESET LOCAL TUNING"))
            {
                game.Save.settings.dev=new DevTuning();
                game.Save.settings.cars.Clear();
                game.Save.settings.weaponsBalance.Clear();
                MarkDevDirty();SaveDev();
            }
            if(Button(x+242,y+h-53,210,36,"BACK TO PAUSE")){SaveDev();devMenu=false;}
            string resumeLabel=engineTab&&car!=null?(car.enginePhysical?"RESUME WITH C":car.enginePreserveEdges?"RESUME WITH B":"RESUME WITH A"):
                coop&&coop.HasTuningDraft?"APPLY & RESUME":"SAVE & RESUME";
            if(Button(x+464,y+h-53,236,36,resumeLabel,true)){SaveDev();devMenu=false;game.Resume();}
        }

        /// <summary>A weapon's name cut down to fit a dev-menu tab ("Satellite-Dish Death Ray" becomes "DEATH RAY").</summary>
        static string ShortName(WeaponDefinition weapon)
        {
            if(!weapon)return "EMPTY";
            string name=weapon.displayName.ToUpperInvariant();
            if(name.Length<=12)return name;
            string[] words=name.Split(' ');
            string tail=words.Length>1?words[words.Length-2]+" "+words[words.Length-1]:name;
            if(tail.Length<=12)return tail;
            string last=words[words.Length-1];
            return last.Length<=12?last:name.Substring(0,12);
        }

        void DrawDevControls(float x,float y,float w,float h,string tab,string carId,bool carEditable)
        {
            int count=0;foreach(var c in DevControl.All)if(c.group==tab)count++;
            devScroll=GUI.BeginScrollView(new Rect(x,y,w,h),devScroll,new Rect(0,0,w-22,Mathf.Max(h,count*68)));
            int row=0;
            foreach(var control in DevControl.All)
            {
                if(control.group!=tab)continue;
                var car=control.scope==DevScope.Car&&!string.IsNullOrEmpty(carId)?BalanceTuning.Car(carId):null;
                var carField=car==null?null:typeof(CarTuning).GetField(control.field.Name);
                object source=control.scope==DevScope.Car?car:control.scope==DevScope.Shared?DevTuning.Current:DevTuning.Local;
                if(source==null)continue;
                float yy=row++*68;
                float value=control.scope==DevScope.Car?(float)carField.GetValue(source):(float)control.field.GetValue(source);
                // Mixed tabs mark the rows that are not part of the car profile.
                string scopeTag=control.scope==DevScope.Shared&&tab!="COMBAT"?"  [WORLD • HOST]":"";
                Text(4,yy,540,24,control.label+scopeTag,16,Cream);
                Text(565,yy,70,24,value.ToString(control.max<=.02f?"0.0000":"0.00"),15,Lime,true);
                bool editable=control.scope==DevScope.Local||control.scope==DevScope.Shared&&!CoopSession.IsRemoteClient||
                    control.scope==DevScope.Car&&carEditable;
                bool previous=GUI.enabled;GUI.enabled=previous&&editable;
                float changed=GUI.HorizontalSlider(new Rect(4,yy+31,635,24),value,control.min,control.max);
                GUI.enabled=previous;
                if(editable&&!Mathf.Approximately(value,changed))
                {
                    if(control.scope==DevScope.Car)typeof(CarTuning).GetField(control.field.Name).SetValue(BalanceTuning.LocalCar(carId),changed);
                    else control.field.SetValue(DevTuning.Local,changed);
                    MarkDevDirty();
                }
            }
            GUI.EndScrollView();
        }

        void DrawWeaponBalance(float x,float y,float w,float h,WeaponDefinition weapon,bool editable)
        {
            if(!weapon)
            {Text(x+8,y+18,w-16,80,"This slot is empty. Pick up a field weapon to tune slot 2.",18,Muted);return;}
            Text(x+8,y-2,w-16,24,"Base values are "+weapon.displayName+"'s catalogue stats.",13,Muted);
            y+=24;h-=24;
            var profile=BalanceTuning.Weapon(weapon.id);
            if(profile==null)return;
            int count=weapon.blastRadius>0?4:3;
            devScroll=GUI.BeginScrollView(new Rect(x,y,w,h),devScroll,new Rect(0,0,w-22,Mathf.Max(h,count*84)));
            DrawWeaponRow(0,"Damage per hit","damage",weapon.damage,.1f,5f,profile,editable);
            DrawWeaponRow(1,"Shots per second","fireRate",weapon.fireRate,.25f,4f,profile,editable);
            DrawWeaponRow(2,"Projectile speed","speed",weapon.speed,.25f,3f,profile,editable);
            if(weapon.blastRadius>0)DrawWeaponRow(3,"Blast radius","blastRadius",weapon.blastRadius,0,3f,profile,editable);
            GUI.EndScrollView();
        }

        void DrawWeaponRow(int row,string label,string fieldName,float basis,float low,float high,WeaponTuning profile,bool editable)
        {
            var field=typeof(WeaponTuning).GetField(fieldName);
            float value=(float)field.GetValue(profile);
            float yy=row*84;
            Text(4,yy,540,24,label+"  (base "+basis.ToString("0.##")+")",16,Cream);
            Text(565,yy,70,24,value.ToString("0.##"),15,Lime,true);
            bool previous=GUI.enabled;GUI.enabled=previous&&editable;
            float changed=GUI.HorizontalSlider(new Rect(4,yy+31,635,24),value,basis*low,basis*high);
            GUI.enabled=previous;
            if(editable&&!Mathf.Approximately(value,changed))
            {
                field.SetValue(BalanceTuning.LocalWeapon(profile.id),changed);
                MarkDevDirty();
            }
        }
    }
}
