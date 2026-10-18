using UnityEngine;

namespace MadeInArizona
{
    public sealed partial class GameUI
    {
        void DrawEquipmentIcon(VehiclePart part,EquipmentSlot slot,float x,float y,float size,string vehicleId)
        {
            var texture=EquipmentIllustrations.Icon(part,slot,vehicleId);
            if(texture)GUI.DrawTexture(new Rect(x,y,size,size),texture,ScaleMode.ScaleToFit);
        }

        void DrawEquipmentManual(float top,float panelHeight)
        {
            var vehicle=FocusVehicle;
            float left=32,manualWidth=width-560,side=width-500,rightWidth=468;
            Rect(left,top,manualWidth,panelHeight,Ink);
            Tag(left+20,top+14,"SERVICE MANUAL / "+vehicle.displayName.ToUpperInvariant(),Orange);
            Text(left+20,top+35,manualWidth-40,25,"FIG. 01 / PLAN VIEW    FRONT ←    SIX SYSTEMS / FOUR FLEXIBLE ACCESSORIES",12,Muted,true);
            float plateX=left+245,plateWidth=manualWidth-490;
            var manual=EquipmentIllustrations.Manual(vehicle);
            if(manual)GUI.DrawTexture(new Rect(plateX,top+68,plateWidth,374),manual,ScaleMode.StretchToFill);
            for(int row=0;row<3;row++)
            {
                float lineY=top+93+row*118+46;
                Rect(left+226,lineY,19,2,Muted);
                Rect(left+manualWidth-245,lineY,19,2,Muted);
            }
            for(int index=0;index<6;index++)
            {
                var card=EquipmentCardRect(index,top,manualWidth);
                EquipmentSlotButton(index,card.x,card.y,card.width,card.height);
            }
            Tag(left+20,top+443,"ACCESSORY RACK / ANY FOUR COMBINATIONS",Lime);
            for(int index=6;index<10;index++)
            {
                var card=EquipmentCardRect(index,top,manualWidth);
                EquipmentSlotButton(index,card.x,card.y,card.width,card.height);
            }
            float tuneY=top+panelHeight-87;
            Rect(left+18,tuneY,manualWidth-36,74,Panel);
            Tag(left+32,tuneY+5,"FINE TUNING",Orange);
            float drive=GUI.HorizontalSlider(new Rect(left+152,tuneY+17,170,22),game.Save.finalDriveTuning,.85f,1.2f);
            Text(left+32,tuneY+23,115,21,"FINAL DRIVE",12,Muted,true);
            float ride=GUI.HorizontalSlider(new Rect(left+455,tuneY+17,165,22),game.Save.rideHeightTuning,-.08f,.15f);
            Text(left+339,tuneY+23,112,21,"RIDE HEIGHT",12,Muted,true);
            game.Save.finalDriveTuning=drive;game.Save.rideHeightTuning=ride;
            Text(left+152,tuneY+48,170,18,drive.ToString("0.00")+":1",12,Cream,true);
            Text(left+455,tuneY+48,165,18,ride.ToString("+0.00;-0.00;0.00")+" M",12,Cream,true);
            if(Button(left+manualWidth-178,tuneY+15,160,45,"APPLY TUNE"))
            {GarageManager.Tune(drive,ride,game.Save);game.RefreshGarageVehicle();}

            Rect(side,top,rightWidth,panelHeight,Ink);
            var slot=FocusSlot;
            Tag(side+20,top+15,(slot==EquipmentSlot.Accessory?"ACCESSORY "+(FocusAccessory+1):slot.ToString()).ToUpperInvariant()+" / INVENTORY",Orange);
            if(browsingParts&&Button(side+rightWidth-278,top+8,83,29,"BACK / B",false,true,11))browsingParts=false;
            if(Button(side+rightWidth-184,top+8,165,29,showIncompatible?"HIDE INCOMPATIBLE":"SHOW ALL",false,true,11))
            {showIncompatible=!showIncompatible;GatherEquipmentChoices();}
            GatherEquipmentChoices();
            if(!browsingParts)
            {
                Text(side+20,top+49,rightWidth-40,32,"A / CLICK CARD TO BROWSE • X TO REMOVE",14,Muted);
                var installed=GarageManager.Installed(game.Save,vehicle.id,slot,FocusAccessory);
                DrawEquipmentIcon(installed,slot,side+20,top+98,62,vehicle.id);
                Text(side+96,top+97,rightWidth-116,58,installed?installed.displayName:"Stock "+slot,24,Cream,true);
                Text(side+20,top+176,rightWidth-40,88,installed?installed.description:
                    "Factory specification active. Select a part to compare its effect with this car's stock system.",18,Cream);
                DrawEquipmentStats(side+20,top+292,rightWidth-40,GarageManager.StatsFor(vehicle,game.Save),null);
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
                if(Button(0,row*58,rightWidth-65,53,"",selectedPart==equipmentChoices[row]))selectedPart=equipmentChoices[row];
                DrawEquipmentIcon(part,slot,7,row*58+6,41,vehicle.id);
                Text(57,row*58+7,rightWidth-128,23,part.displayName,15,
                    selectedPart==equipmentChoices[row]?Ink:Cream,true);
                Text(57,row*58+31,rightWidth-128,19,part.compatibilityClass.ToString().ToUpperInvariant()+" / "+status,11,
                    selectedPart==equipmentChoices[row]?Ink:fits?Muted:Orange,true);
            }
            GUI.EndScrollView();
            if(equipmentChoices.Count==0)return;
            var selected=ContentCatalog.Parts[selectedPart];bool compatible=GarageManager.Compatible(selected,vehicle);
            float detailY=top+55+listHeight;
            DrawEquipmentIcon(selected,slot,side+20,detailY,44,vehicle.id);
            Tag(side+73,detailY+3,selected.compatibilityClass.ToString().ToUpperInvariant()+" / "+selected.displayName.ToUpperInvariant(),Lime);
            Text(side+20,detailY+49,rightWidth-40,48,selected.description,15,Cream);
            string restriction=compatible?"FITS THIS VEHICLE":selected.compatibilityClass==PartCompatibility.CarSpecific?
                "ONLY: "+string.Join(", ",selected.compatibleVehicles):"CHASSIS: "+string.Join(", ",selected.compatibleChassis);
            Text(side+20,detailY+99,rightWidth-40,23,restriction,13,compatible?Muted:Orange,true);
            DrawEquipmentStats(side+20,detailY+125,rightWidth-40,GarageManager.StatsFor(vehicle,game.Save),CandidateStats(selected));
            bool unlocked=game.Save.unlockedMission>=selected.unlockMission;
            string action=!compatible?"INCOMPATIBLE":!unlocked?"LOCKED UNTIL JOB "+(selected.unlockMission+1):
                game.Save.ownedParts.Contains(selected.id)?"EQUIP / A":"BUY & EQUIP / $"+selected.cost;
            if(Button(side+20,top+panelHeight-55,rightWidth-40,42,action,true,compatible&&unlocked))BuyOrEquipCandidate(selected);
        }
    }
}
