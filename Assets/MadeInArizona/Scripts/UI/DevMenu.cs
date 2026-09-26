using UnityEngine;
namespace MadeInArizona
{
    public sealed partial class GameUI
    {
        bool devMenu, devDirty;
        int devTab;
        Vector2 devScroll;
        float devSaveAt;
        readonly string[] devTabs={"DRIVING","DRIFT","COMBAT","CAMERA","LIGHT & COLOR"};
        float StylesRadioHeight(string message,float w) => Style(14,Cream).CalcHeight(new GUIContent(message),w)+5;
        public void OpenDevMenu() { if(game.IsPlaying)game.Pause();devMenu=true;settings=false; }
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
            float w=720,h=Mathf.Min(height-40,820),x=width-w-20,y=20;
            Rect(x,y,w,h,new Color(.055f,.081f,.09f,1));
            Text(x+24,y+20,w-48,38,"LIVE DEV TUNING",27,Cream,true);
            Text(x+24,y+60,w-48,42,"Mouse controls • changes apply immediately and save automatically. Resume to test handling.",14,Muted);
            float tabW=(w-40)/devTabs.Length;
            for(int i=0;i<devTabs.Length;i++) if(Button(x+20+i*tabW,y+110,tabW-8,36,devTabs[i],devTab==i)) {devTab=i;devScroll=Vector2.zero;}
            int count=0;foreach(var c in DevControl.All)if(c.group==devTabs[devTab])count++;
            devScroll=GUI.BeginScrollView(new Rect(x+20,y+160,w-40,h-252),devScroll,new Rect(0,0,w-62,count*68));
            int row=0;
            foreach(var control in DevControl.All)
            {
                if(control.group!=devTabs[devTab])continue;
                float yy=row++*68,value=(float)control.field.GetValue(DevTuning.Current);
                Text(4,yy,540,24,control.label,16,Cream);
                Text(565,yy,70,24,value.ToString(control.max<=.02f?"0.0000":"0.00"),15,Lime,true);
                float changed=GUI.HorizontalSlider(new Rect(4,yy+31,635,24),value,control.min,control.max);
                if(!Mathf.Approximately(value,changed))
                {
                    control.field.SetValue(DevTuning.Current,changed);DevTuning.Apply();devDirty=true;devSaveAt=Time.unscaledTime+.35f;
                }
            }
            GUI.EndScrollView();
            Text(x+24,y+h-85,w-48,23,SaveSystem.LastError!=null?SaveSystem.LastError:devDirty?"Saving…":"Saved locally • health changes preserve current health percentage",12,Muted);
            if(Button(x+20,y+h-53,210,36,"RESET ALL DEFAULTS")) {game.Save.settings.dev=new DevTuning();DevTuning.Apply();devDirty=true;SaveDev();}
            if(Button(x+242,y+h-53,210,36,"BACK TO PAUSE")){SaveDev();devMenu=false;}
            if(Button(x+464,y+h-53,236,36,"SAVE & RESUME",true)){SaveDev();devMenu=false;game.Resume();}
        }
    }
}
