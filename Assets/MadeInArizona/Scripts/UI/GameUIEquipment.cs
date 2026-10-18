using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MadeInArizona
{
    public sealed partial class GameUI
    {
        static readonly EquipmentSlot[] MechanicalSlots={EquipmentSlot.Engine,EquipmentSlot.Transmission,
            EquipmentSlot.Drivetrain,EquipmentSlot.Tires,EquipmentSlot.Suspension,EquipmentSlot.Body};
        int equipmentFocus=10;
        public int EquipmentFocus => equipmentFocus;
        public bool EquipmentBrowsing => browsingParts;
        int vehicleBrowseIndex=-1;
        bool browsingParts,showIncompatible,showEquipmentDetails;
        VehiclePart draggingPart;
        Vector2 equipmentScroll;
        readonly List<int> equipmentChoices=new List<int>();
        static readonly Color[] PaintChoices={Color.clear,new Color(.94f,.32f,.13f),new Color(.13f,.75f,.74f),
            new Color(.07f,.08f,.09f),new Color(.83f,.73f,.52f)};
        public static Color GaragePaint(int index)=>PaintChoices[Mathf.Clamp(index,1,PaintChoices.Length-1)];

        EquipmentSlot FocusSlot => equipmentFocus<6?MechanicalSlots[equipmentFocus]:EquipmentSlot.Accessory;
        int FocusAccessory => Mathf.Clamp(equipmentFocus-6,0,3);
        VehicleDefinition FocusVehicle => ContentCatalog.Vehicles[game.Save.selectedVehicle];

        void ChangeGarageVehicle(int delta)
        {
            if(vehicleBrowseIndex<0)vehicleBrowseIndex=game.Save.selectedVehicle;
            vehicleBrowseIndex=(vehicleBrowseIndex+delta+ContentCatalog.Vehicles.Length)%ContentCatalog.Vehicles.Length;
            browsingParts=false;
        }
        void ConfirmGarageVehicle()
        {
            int index=vehicleBrowseIndex<0?game.Save.selectedVehicle:vehicleBrowseIndex;
            if(GarageManager.SelectVehicle(index,game.Save))game.RefreshGarageVehicle();
            else game.Notify(GarageManager.LastMessage);
        }
        void TickEquipmentInput(Gamepad pad)
        {
            if(pad.buttonNorth.wasPressedThisFrame)showEquipmentDetails=!showEquipmentDetails;
            if(browsingParts)
            {
                if(pad.buttonEast.wasPressedThisFrame){browsingParts=false;return;}
                GatherEquipmentChoices();
                int row=equipmentChoices.IndexOf(selectedPart);
                if(pad.dpad.down.wasPressedThisFrame||pad.leftStick.down.wasPressedThisFrame)row++;
                if(pad.dpad.up.wasPressedThisFrame||pad.leftStick.up.wasPressedThisFrame)row--;
                if(equipmentChoices.Count>0)
                {
                    row=Mathf.Clamp(row,0,equipmentChoices.Count-1);
                    selectedPart=equipmentChoices[row];
                    equipmentScroll.y=Mathf.Clamp((row-2)*58,0,Mathf.Max(0,equipmentChoices.Count*58-218));
                }
                if(pad.buttonSouth.wasPressedThisFrame&&equipmentChoices.Count>0)BuyOrEquipCandidate(ContentCatalog.Parts[selectedPart]);
                return;
            }
            if(pad.buttonEast.wasPressedThisFrame){equipmentFocus=10;return;}
            if(pad.buttonWest.wasPressedThisFrame&&equipmentFocus<10)
            {if(GarageManager.Unequip(game.Save,FocusSlot,FocusAccessory)){game.RefreshGarageVehicle();game.Notify(GarageManager.LastMessage);}return;}
            int horizontal=pad.dpad.right.wasPressedThisFrame||pad.leftStick.right.wasPressedThisFrame?1:
                pad.dpad.left.wasPressedThisFrame||pad.leftStick.left.wasPressedThisFrame?-1:0;
            int vertical=pad.dpad.down.wasPressedThisFrame||pad.leftStick.down.wasPressedThisFrame?1:
                pad.dpad.up.wasPressedThisFrame||pad.leftStick.up.wasPressedThisFrame?-1:0;
            if(equipmentFocus==10&&horizontal!=0){ChangeGarageVehicle(horizontal);return;}
            if(equipmentFocus==11&&horizontal!=0)
            {game.SelectedMission=Mathf.Clamp(game.SelectedMission+horizontal,0,game.Save.unlockedMission);return;}
            if(equipmentFocus<10&&horizontal!=0)equipmentFocus=Mathf.Clamp(equipmentFocus+horizontal,0,9);
            if(vertical!=0)
            {
                if(equipmentFocus==10)equipmentFocus=vertical>0?0:11;
                else if(equipmentFocus==11)equipmentFocus=vertical<0?8:12;
                else if(equipmentFocus==12)equipmentFocus=vertical<0?11:10;
                else if(vertical<0)equipmentFocus=equipmentFocus<2?10:equipmentFocus-2;
                else equipmentFocus=equipmentFocus>=8?11:equipmentFocus+2;
            }
            if(pad.buttonSouth.wasPressedThisFrame)ConfirmEquipment();
        }
        void TickEquipmentKeyboard(Keyboard keyboard)
        {
            if(keyboard.escapeKey.wasPressedThisFrame&&browsingParts){browsingParts=false;return;}
            if(keyboard.yKey.wasPressedThisFrame)showEquipmentDetails=!showEquipmentDetails;
            if(keyboard.backspaceKey.wasPressedThisFrame&&equipmentFocus<10&&!browsingParts)
            {if(GarageManager.Unequip(game.Save,FocusSlot,FocusAccessory)){game.RefreshGarageVehicle();game.Notify(GarageManager.LastMessage);}return;}
            int horizontal=keyboard.rightArrowKey.wasPressedThisFrame?1:keyboard.leftArrowKey.wasPressedThisFrame?-1:0;
            int vertical=keyboard.downArrowKey.wasPressedThisFrame?1:keyboard.upArrowKey.wasPressedThisFrame?-1:0;
            if(browsingParts)
            {
                GatherEquipmentChoices();
                if(equipmentChoices.Count==0)return;
                int row=Mathf.Clamp(equipmentChoices.IndexOf(selectedPart)+vertical,0,equipmentChoices.Count-1);
                selectedPart=equipmentChoices[row];equipmentScroll.y=Mathf.Clamp((row-2)*58,0,Mathf.Max(0,equipmentChoices.Count*58-218));
                return;
            }
            if(equipmentFocus==10&&horizontal!=0){ChangeGarageVehicle(horizontal);return;}
            if(equipmentFocus==11&&horizontal!=0)
            {game.SelectedMission=Mathf.Clamp(game.SelectedMission+horizontal,0,game.Save.unlockedMission);return;}
            if(equipmentFocus<10&&horizontal!=0)equipmentFocus=Mathf.Clamp(equipmentFocus+horizontal,0,9);
            if(vertical!=0)
            {
                if(equipmentFocus==10)equipmentFocus=vertical>0?0:11;
                else if(equipmentFocus==11)equipmentFocus=vertical<0?8:12;
                else if(equipmentFocus==12)equipmentFocus=vertical<0?11:10;
                else if(vertical<0)equipmentFocus=equipmentFocus<2?10:equipmentFocus-2;
                else equipmentFocus=equipmentFocus>=8?11:equipmentFocus+2;
            }
        }
        void ConfirmEquipment()
        {
            if(browsingParts)
            {GatherEquipmentChoices();if(equipmentChoices.Count>0)BuyOrEquipCandidate(ContentCatalog.Parts[selectedPart]);return;}
            if(equipmentFocus==11)
            {if(CoopSession.Instance&&CoopSession.Instance.InLobby)CoopSession.Instance.SetReady(!CoopSession.Instance.LocalReady);
                else game.StartMission(game.SelectedMission);return;}
            if(equipmentFocus==12){if(!(CoopSession.Instance&&CoopSession.Instance.InLobby))game.StartCombatTrial();return;}
            if(equipmentFocus==10){ConfirmGarageVehicle();return;}
            browsingParts=true;GatherEquipmentChoices();equipmentScroll=Vector2.zero;
        }
        void GatherEquipmentChoices()
        {
            equipmentChoices.Clear();
            if(equipmentFocus>=10)return;
            for(int group=0;group<3;group++)
                for(int i=0;i<ContentCatalog.Parts.Length;i++)
                {
                    var part=ContentCatalog.Parts[i];
                    if(part.equipmentSlot!=FocusSlot||(int)part.compatibilityClass!=group)continue;
                    if(showIncompatible||GarageManager.Compatible(part,FocusVehicle))equipmentChoices.Add(i);
                }
            if(equipmentChoices.Count>0&&!equipmentChoices.Contains(selectedPart))selectedPart=equipmentChoices[0];
        }
        void BuyOrEquipCandidate(VehiclePart part)
        {
            if(!part||part.equipmentSlot!=FocusSlot)
            {game.Notify("This part does not fit that slot.");return;}
            if(!GarageManager.Compatible(part,FocusVehicle)){game.Notify("Incompatible chassis or vehicle.");return;}
            if(!game.Save.ownedParts.Contains(part.id)&&!GarageManager.BuyPart(part,game.Save))
            {game.Notify(GarageManager.LastMessage);return;}
            if(GarageManager.TryEquip(part,game.Save,FocusSlot,FocusAccessory))
            {game.RefreshGarageVehicle();browsingParts=false;}
            game.Notify(GarageManager.LastMessage);
        }
        void TickEquipmentPointer()
        {
            var mouse=Mouse.current;if(mouse==null)return;
            float scale=Mathf.Max(.45f,Mathf.Min(Screen.width/1440f,Screen.height/900f)*
                Mathf.Clamp(game.Save.settings.uiScale,.85f,1.2f));
            float uiWidth=Screen.width/scale,uiHeight=Screen.height/scale;
            Vector2 screen=mouse.position.ReadValue();
            Vector2 pointer=new Vector2(screen.x/scale,(Screen.height-screen.y)/scale);
            float top=169,panelHeight=uiHeight-245,side=uiWidth-520;
            if(mouse.leftButton.wasPressedThisFrame&&browsingParts&&equipmentFocus<10)
            {
                float listHeight=Mathf.Min(245,panelHeight*.39f);
                if(new Rect(side+18,top+46,452,listHeight).Contains(pointer))
                {
                    GatherEquipmentChoices();
                    int row=Mathf.FloorToInt((pointer.y-top-46+equipmentScroll.y)/58);
                    if(row>=0&&row<equipmentChoices.Count)draggingPart=ContentCatalog.Parts[equipmentChoices[row]];
                }
            }
            if(!mouse.leftButton.wasReleasedThisFrame||!draggingPart)return;
            var candidate=draggingPart;draggingPart=null;
            for(int i=0;i<10;i++)
            {
                float x=32+17+(i%2)*282;
                float y=top+(i<6?133+(i/2)*66:360+((i-6)/2)*66);
                if(!new Rect(x,y,269,61).Contains(pointer))continue;
                equipmentFocus=i;BuyOrEquipCandidate(candidate);return;
            }
        }
        void EquipmentSlotButton(int index,float x,float y,float w,float h)
        {
            var slot=index<6?MechanicalSlots[index]:EquipmentSlot.Accessory;
            var installed=GarageManager.Installed(game.Save,FocusVehicle.id,slot,Mathf.Max(0,index-6));
            string label=(index<6?slot.ToString().ToUpperInvariant():"ACCESSORY "+(index-5))+
                "\n"+(installed?installed.displayName:"Factory / Empty");
            if(Button(x,y,w,h,label,equipmentFocus==index))
            {equipmentFocus=index;browsingParts=true;GatherEquipmentChoices();equipmentScroll=Vector2.zero;}
        }
        VehicleStats CandidateStats(VehiclePart candidate)
        {
            if(!candidate||!GarageManager.Compatible(candidate,FocusVehicle))return null;
            var preview=JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(game.Save));
            if(!preview.ownedParts.Contains(candidate.id))preview.ownedParts.Add(candidate.id);
            var loadout=GarageManager.Loadout(preview,FocusVehicle.id);
            if(FocusSlot==EquipmentSlot.Accessory)loadout.accessories[FocusAccessory]=candidate.id;
            else loadout.mechanical[(int)FocusSlot]=candidate.id;
            return GarageManager.StatsFor(FocusVehicle,preview);
        }
        void DrawEquipment(float top,float panelHeight)
        {
            var vehicle=FocusVehicle;
            if(vehicleBrowseIndex<0)vehicleBrowseIndex=game.Save.selectedVehicle;
            var browsed=ContentCatalog.Vehicles[vehicleBrowseIndex];
            float left=32,side=width-520,rightWidth=488;
            Rect(left,top,590,panelHeight,Ink);
            Tag(left+18,top+12,"MOTOR POOL / "+vehicle.chassisTags[0].ToUpperInvariant()+" CHASSIS",Orange);
            if(Button(left+16,top+39,43,42,"◀",equipmentFocus==10))ChangeGarageVehicle(-1);
            if(Button(left+528,top+39,43,42,"▶",equipmentFocus==10))ChangeGarageVehicle(1);
            Text(left+66,top+43,455,31,browsed.displayName.ToUpperInvariant(),22,Cream,true,TextAnchor.MiddleCenter);
            if(Button(left+123,top+77,340,28,browsed.id==vehicle.id?"SELECTED VEHICLE":
                game.Save.ownedVehicles.Contains(browsed.id)?"SELECT / A":
                game.Save.unlockedMission<browsed.unlockMission?"LOCKED / WORK ORDER "+(browsed.unlockMission+1):"BUY & SELECT / $"+browsed.cost,
                equipmentFocus==10,browsed.id==vehicle.id||game.Save.unlockedMission>=browsed.unlockMission,12))ConfirmGarageVehicle();
            Tag(left+18,top+108,"MECHANICAL / ONE PART PER SYSTEM",Lime);
            float slotWidth=269,slotHeight=61;
            for(int i=0;i<6;i++)EquipmentSlotButton(i,left+17+(i%2)*282,top+133+(i/2)*66,slotWidth,slotHeight);
            Tag(left+18,top+335,"ACCESSORIES / ANY FOUR COMBINATIONS",Lime);
            for(int i=0;i<4;i++)EquipmentSlotButton(i+6,left+17+(i%2)*282,top+360+(i/2)*66,slotWidth,slotHeight);
            var mission=ContentCatalog.Missions[game.SelectedMission];
            float missionY=top+panelHeight-150;
            Rect(left+16,missionY,558,132,Panel);
            Tag(left+26,missionY+8,"DISPATCH / WORK ORDER "+(game.SelectedMission+1).ToString("00"),Orange);
            if(Button(left+23,missionY+34,43,39,"◀",equipmentFocus==11))game.SelectedMission=Mathf.Max(0,game.SelectedMission-1);
            Text(left+74,missionY+39,422,30,mission.title,18,Cream,true,TextAnchor.MiddleCenter);
            if(Button(left+515,missionY+34,43,39,"▶",equipmentFocus==11))game.SelectedMission=Mathf.Min(game.Save.unlockedMission,game.SelectedMission+1);
            if(Button(left+23,missionY+83,350,38,"START MISSION / A",equipmentFocus==11))
            {if(CoopSession.Instance&&CoopSession.Instance.InLobby)CoopSession.Instance.SetReady(!CoopSession.Instance.LocalReady);else game.StartMission(game.SelectedMission);}
            if(Button(left+383,missionY+83,175,38,"COMBAT TRIAL",equipmentFocus==12,!(CoopSession.Instance&&CoopSession.Instance.InLobby),12))game.StartCombatTrial();
            Text(647,top+16,Mathf.Max(100,side-660),64,"CAR PREVIEW\nRMB / RIGHT STICK TO ROTATE",14,Muted,true,TextAnchor.MiddleCenter);
            Rect(side,top,rightWidth,panelHeight,Ink);
            if(equipmentFocus>=10)
            {
                if(equipmentFocus==11)
                {
                    Tag(side+20,top+20,"WORK ORDER "+(game.SelectedMission+1).ToString("00")+" / "+mission.region.ToUpperInvariant(),Orange);
                    Text(side+20,top+62,rightWidth-40,68,mission.title.ToUpperInvariant(),28,Cream,true);
                    Text(side+20,top+150,rightWidth-40,170,mission.briefing,19);
                    Tag(side+20,top+335,"OPTIONAL OBJECTIVE",Lime);
                    Text(side+20,top+365,rightWidth-40,115,mission.optionalObjective,17,Muted);
                    Text(side+20,top+panelHeight-93,rightWidth-40,55,"REWARD $"+mission.reward.ToString("N0")+" + SALVAGE",19,Lime,true);
                }
                else
                {
                    Tag(side+20,top+20,equipmentFocus==12?"COMBAT TRIAL":"MOTOR POOL / SELECT A CAR",Orange);
                    Text(side+20,top+59,rightWidth-40,105,equipmentFocus==12?
                        "Two waves of hostile vehicles. Test the equipment build without spending a campaign work order.":browsed.description,18);
                    DrawEquipmentStats(side+20,top+190,rightWidth-40,GarageManager.StatsFor(equipmentFocus==10?browsed:vehicle,game.Save),null);
                }
                return;
            }
            var slot=FocusSlot;
            Tag(side+20,top+14,(slot==EquipmentSlot.Accessory?"ACCESSORY "+(FocusAccessory+1):slot.ToString()).ToUpperInvariant()+" / PARTS",Orange);
            if(browsingParts&&Button(side+rightWidth-278,top+8,83,29,"BACK / B",false,true,11))browsingParts=false;
            if(Button(side+rightWidth-184,top+8,165,29,showIncompatible?"HIDE INCOMPATIBLE":"SHOW ALL",false,true,11))
            {showIncompatible=!showIncompatible;GatherEquipmentChoices();}
            GatherEquipmentChoices();
            if(!browsingParts)
            {
                Text(side+20,top+48,rightWidth-40,36,"A / CLICK SLOT TO BROWSE • X TO REMOVE",14,Muted);
                var installed=GarageManager.Installed(game.Save,vehicle.id,slot,FocusAccessory);
                Text(side+20,top+94,rightWidth-40,63,installed?installed.displayName:"Factory specification",25,Cream,true);
                Text(side+20,top+165,rightWidth-40,98,installed?installed.description:"No custom part installed. Original vehicle characteristics are active.",18);
                DrawEquipmentStats(side+20,top+290,rightWidth-40,GarageManager.StatsFor(vehicle,game.Save),null);
                return;
            }
            float listHeight=Mathf.Min(245,panelHeight*.39f);
            equipmentScroll=GUI.BeginScrollView(new Rect(side+18,top+46,rightWidth-36,listHeight),equipmentScroll,
                new Rect(0,0,rightWidth-63,equipmentChoices.Count*58));
            for(int row=0;row<equipmentChoices.Count;row++)
            {
                var part=ContentCatalog.Parts[equipmentChoices[row]];
                bool fits=GarageManager.Compatible(part,vehicle);
                string status=!fits?"INCOMPATIBLE":GarageManager.Installed(game.Save,vehicle.id,slot,FocusAccessory)==part?
                    "INSTALLED":game.Save.ownedParts.Contains(part.id)?"OWNED":game.Save.unlockedMission<part.unlockMission?
                    "LOCKED / JOB "+(part.unlockMission+1):"$"+part.cost;
                if(Button(0,row*58,rightWidth-65,53,part.displayName+"  /  "+part.compatibilityClass+"\n"+status,
                    selectedPart==equipmentChoices[row],true,13))selectedPart=equipmentChoices[row];
            }
            GUI.EndScrollView();
            if(equipmentChoices.Count==0)return;
            var selected=ContentCatalog.Parts[selectedPart];bool compatible=GarageManager.Compatible(selected,vehicle);
            float detailY=top+55+listHeight;
            Tag(side+20,detailY,selected.compatibilityClass.ToString().ToUpperInvariant()+" / "+selected.displayName.ToUpperInvariant(),Lime);
            Text(side+20,detailY+24,rightWidth-40,68,selected.description,15,Cream);
            string restriction=compatible?"FITS THIS VEHICLE":selected.compatibilityClass==PartCompatibility.CarSpecific?
                "ONLY: "+string.Join(", ",selected.compatibleVehicles):"CHASSIS: "+string.Join(", ",selected.compatibleChassis);
            Text(side+20,detailY+88,rightWidth-40,26,restriction,13,compatible?Muted:Orange,true);
            DrawEquipmentStats(side+20,detailY+118,rightWidth-40,GarageManager.StatsFor(vehicle,game.Save),CandidateStats(selected));
            bool unlocked=game.Save.unlockedMission>=selected.unlockMission;
            string action=!compatible?"INCOMPATIBLE":!unlocked?"LOCKED UNTIL JOB "+(selected.unlockMission+1):
                game.Save.ownedParts.Contains(selected.id)?"EQUIP / A":"BUY & EQUIP / $"+selected.cost;
            if(Button(side+20,top+panelHeight-55,rightWidth-40,42,action,true,compatible&&unlocked))BuyOrEquipCandidate(selected);
        }
        void DrawEquipmentStats(float x,float y,float w,VehicleStats current,VehicleStats projected)
        {
            if(current==null)return;
            Tag(x,y,projected==null?"ACTUAL VEHICLE STATS":"CURRENT → PROJECTED",Orange);
            string arrow(float a,float b,string format) => a.ToString(format)+(projected==null?"":" → "+b.ToString(format));
            Text(x,y+25,w,145,
                "POWER " +arrow(current.horsepower,projected?.horsepower??0,"0")+" HP    TORQUE "+arrow(current.torque,projected?.torque??0,"0")+" Nm\n"+
                "MASS "+arrow(current.mass,projected?.mass??0,"0")+" kg    TOP "+arrow(current.maxSpeed,projected?.maxSpeed??0,"0")+" km/h\n"+
                "GRIP "+arrow(current.grip,projected?.grip??0,"0.00")+"    TURN "+arrow(current.turnSpeed,projected?.turnSpeed??0,"0")+"\n"+
                "ARMOR/HEALTH "+arrow(current.maxHealth,projected?.maxHealth??0,"0")+"    DRIVE "+current.drivetrain+
                (projected==null?"":" → "+projected.drivetrain)+
                (showEquipmentDetails?"\nGEAR " +arrow(current.finalDrive,projected?.finalDrive??0,"0.00")+
                    "    COOLING "+arrow(current.cooling,projected?.cooling??0,"0.00")+
                    "    TRAVEL "+arrow(current.suspensionTravel,projected?.suspensionTravel??0,"0.00"):""),
                showEquipmentDetails?13:14,Cream);
        }
        void ChooseAppearance(int index)
        {
            if(index<5)
            {
                GarageManager.Loadout(game.Save,FocusVehicle.id).paintIndex=index;
                game.RefreshGarageVehicle();game.Notify(index==0?"Factory paint restored.":"Paint applied.");
            }
            else
            {
                game.Save.dogCosmetic=index-5;game.ReturnToGarage();station=2;
            }
            SaveSystem.Save(game.Save);
        }
        void DrawAppearance(float top,float panelHeight)
        {
            Rect(32,top,510,panelHeight,Ink);
            Tag(55,top+22,"PAINT / VISUAL ONLY",Orange);
            string[] paints={"FACTORY PAINT","SUNSET ORANGE","TURQUOISE","MATTE BLACK","DESERT CREAM"};
            int chosen=GarageManager.Loadout(game.Save,FocusVehicle.id).paintIndex;
            for(int i=0;i<paints.Length;i++)
                if(Button(55,top+65+i*65,460,54,paints[i],appearanceFocus==i||chosen==i))ChooseAppearance(i);
            Text(55,top+421,450,97,"Paint and visual fittings do not use a mechanical or accessory slot. The selected vehicle keeps its own finish.",17,Muted);
            float x=width-520;Rect(x,top,488,panelHeight,Ink);
            Tag(x+24,top+23,"SUZUKI / COSMETIC KIT",Lime);
            string[] looks={"FACTORY DOG","SAFETY INSPECTOR","DESERT DOGGLES","RECOVERY CAPTAIN"};
            for(int i=0;i<looks.Length;i++)
                if(Button(x+24,top+65+i*73,440,57,looks[i],appearanceFocus==i+5||game.Save.dogCosmetic==i))ChooseAppearance(i+5);
            Text(x+24,top+391,435,91,"Appearance stays separate from performance equipment. LB/RB switches tabs; D-pad chooses a look; A applies it.",17,Muted);
        }
    }
}
