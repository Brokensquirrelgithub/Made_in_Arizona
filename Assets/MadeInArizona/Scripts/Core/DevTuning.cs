using System;
using UnityEngine;
namespace MadeInArizona
{
    [Serializable]
    public sealed class DevTuning
    {
        public float steering=1.6f, acceleration=1.2f, grip=1f, propDamage=2f, nitro=1f;
        public float playerHealth=1f, enemyHealth=1f, playerDamage=1f, incomingDamage=1f;
        public float bloom=.75f, exposure=.45f, contrast=6f, saturation=8f, chromatic=.02f, motionBlur=.12f, depthOfField=0f, vignette=.08f;
        public float haze=.0012f, sunlight=1.25f, ambient=1.15f, shake=1f, cameraZoom=21f, ao=.30f, cameraLead=.12f;
        public float dynamicZoomOut=1.45f, dynamicZoomMargin=.16f, enemyCatchUp=2.2f;
        public float driftGrip=.9f, driftYaw=1.6f, driftKick=45f, driftSpeedLoss=.6f, driftThrottle=.9f, driftRecovery=.35f;
        // Sun reflections (Shaders/SunGlint.hlsl). The bloom threshold sits high so only HDR highlights glow.
        public float bloomThreshold=1.9f, specularPeak=60f, surfaceWear=1f, microNormal=1f;
        public float glint=1f, glintTolerance=1.2f, glintBloom=.7f, glintFade=5f;
        public float glintPaint=.8f, glintChrome=1f, glintGlass=1f, glintSigns=.6f, glintWater=.5f, glintMetal=.5f, glintPlastic=.15f;
        public int presentationVersion;
        static readonly DevTuning defaults=new DevTuning();
        public static DevTuning Current => GameManager.Instance?.Save?.settings?.dev ?? defaults;
        public void Clamp()
        {
            if (presentationVersion < 1)
            {
                // Update the old shipped look in existing saves without resetting custom sliders.
                if (Mathf.Approximately(bloom,1.15f)) bloom=defaults.bloom;
                if (Mathf.Approximately(exposure,.3f)) exposure=defaults.exposure;
                if (Mathf.Approximately(contrast,15f)) contrast=defaults.contrast;
                if (Mathf.Approximately(saturation,10f)) saturation=defaults.saturation;
                if (Mathf.Approximately(chromatic,.08f)) chromatic=defaults.chromatic;
                if (Mathf.Approximately(vignette,.2f)) vignette=defaults.vignette;
                if (Mathf.Approximately(haze,.002f)) haze=defaults.haze;
                if (Mathf.Approximately(ao,.45f)) ao=defaults.ao;
                presentationVersion=1;
            }
            if (presentationVersion < 2)
            {
                // Reflection controls arrived with version 2: older saves carry zeros for them, and the old
                // 0.95 bloom threshold let every sunlit surface glow.
                bloomThreshold=defaults.bloomThreshold; specularPeak=defaults.specularPeak; surfaceWear=defaults.surfaceWear; microNormal=defaults.microNormal;
                glint=defaults.glint; glintTolerance=defaults.glintTolerance; glintBloom=defaults.glintBloom; glintFade=defaults.glintFade;
                glintPaint=defaults.glintPaint; glintChrome=defaults.glintChrome; glintGlass=defaults.glintGlass; glintSigns=defaults.glintSigns;
                glintWater=defaults.glintWater; glintMetal=defaults.glintMetal; glintPlastic=defaults.glintPlastic;
                presentationVersion=2;
            }
            // Saves from before the nitro slider existed carry no value for it.
            if (nitro<=0) nitro=defaults.nitro;
            foreach(var control in DevControl.All)
            {
                float value=(float)control.field.GetValue(this);
                if(float.IsNaN(value)||float.IsInfinity(value)) value=(float)control.field.GetValue(defaults);
                control.field.SetValue(this,Mathf.Clamp(value,control.min,control.max));
            }
        }
        public static void Apply()
        {
            Current.Clamp();
            foreach(var vehicle in VehicleController.Active) if(vehicle&&vehicle.Damage) vehicle.Damage.ApplyHealthTuning();
            DevVisuals.Apply();
        }
    }
    public sealed class DevControl
    {
        public readonly string label,group;
        public readonly float min,max;
        public readonly System.Reflection.FieldInfo field;
        DevControl(string group,string name,string label,float min,float max)
        { this.group=group;this.label=label;this.min=min;this.max=max;field=typeof(DevTuning).GetField(name); }
        public static readonly DevControl[] All={
            new DevControl("DRIVING","steering","Steering agility",.5f,3.5f),
            new DevControl("DRIVING","acceleration","Acceleration",.5f,3f),
            new DevControl("DRIVING","grip","Lateral tire grip",.3f,2f),
            new DevControl("DRIVING","nitro","Nitro thrust (1 = default, 4x the original boost)",.25f,2f),
            new DevControl("DRIVING","propDamage","Impact damage to props",.5f,5),
            new DevControl("COMBAT","playerHealth","Player health capacity",.25f,10),
            new DevControl("COMBAT","enemyHealth","Enemy health capacity",.25f,5),
            new DevControl("COMBAT","playerDamage","Player weapon damage",.1f,5),
            new DevControl("COMBAT","incomingDamage","Damage received by player",0,3),
            new DevControl("COMBAT","enemyCatchUp","Off-screen enemy catch-up pace (1 = normal)",1,4),
            new DevControl("DRIFT","driftGrip","Sideways grip while drifting (lower slides more)",.1f,6),
            new DevControl("DRIFT","driftYaw","Rotation rate while drifting",1,3),
            new DevControl("DRIFT","driftKick","Entry flick when drift starts (deg/s)",0,160),
            new DevControl("DRIFT","driftSpeedLoss","Speed scrubbed while drifting (m/s²)",0,5),
            new DevControl("DRIFT","driftThrottle","Engine drive while drifting",0,1.5f),
            new DevControl("DRIFT","driftRecovery","Grip recovery after release (s)",.05f,1.5f),
            new DevControl("CAMERA","cameraZoom","Camera distance / orthographic size",13,32),
            new DevControl("CAMERA","dynamicZoomOut","Dynamic zoom maximum pull-back",1,2.2f),
            new DevControl("CAMERA","dynamicZoomMargin","Dynamic zoom screen-edge margin",.05f,.35f),
            new DevControl("CAMERA","shake","Camera shake multiplier",0,3),
            new DevControl("CAMERA","cameraLead","Look-ahead toward the direction of travel (0 = off)",0,.4f),
            new DevControl("CAMERA","motionBlur","Camera motion blur",0,1),
            new DevControl("CAMERA","chromatic","Chromatic aberration",0,1),
            new DevControl("CAMERA","depthOfField","Depth of field strength (0 disables)",0,1),
            new DevControl("CAMERA","vignette","Lens vignette",0,.6f),
            new DevControl("LIGHT & COLOR","bloom","Bloom glow",0,3),
            new DevControl("LIGHT & COLOR","exposure","Exposure (stops)",-2,2),
            new DevControl("LIGHT & COLOR","contrast","Contrast",-40,50),
            new DevControl("LIGHT & COLOR","saturation","Saturation",-70,60),
            new DevControl("LIGHT & COLOR","sunlight","Sun intensity multiplier",.2f,3),
            new DevControl("LIGHT & COLOR","ambient","Ambient light multiplier",.2f,3),
            new DevControl("LIGHT & COLOR","haze","Environment haze density",0,.02f),
            new DevControl("LIGHT & COLOR","ao","Ambient occlusion",0,1),
            new DevControl("REFLECTIONS","bloomThreshold","Bloom threshold (higher: only the brightest HDR pixels glow)",.6f,3.5f),
            new DevControl("REFLECTIONS","specularPeak","Sun specular HDR ceiling",4,300),
            new DevControl("REFLECTIONS","surfaceWear","Dust, scratches, fingerprints and oxidation",0,2),
            new DevControl("REFLECTIONS","microNormal","Micro-surface detail (fragments highlights)",0,2.5f),
            new DevControl("REFLECTIONS","glint","Sun glint intensity (0 = off)",0,3),
            new DevControl("REFLECTIONS","glintTolerance","Glint angular tolerance (degrees)",.2f,6),
            new DevControl("REFLECTIONS","glintBloom","Glint bloom contribution",0,1),
            new DevControl("REFLECTIONS","glintFade","Glint fade sharpness (higher: briefer flash)",1,16),
            new DevControl("REFLECTIONS","glintPaint","Glints on car paint",0,1),
            new DevControl("REFLECTIONS","glintChrome","Glints on chrome and polished metal",0,1),
            new DevControl("REFLECTIONS","glintGlass","Glints on glass",0,1),
            new DevControl("REFLECTIONS","glintSigns","Glints on signs and reflectors",0,1),
            new DevControl("REFLECTIONS","glintWater","Glints on water",0,1),
            new DevControl("REFLECTIONS","glintMetal","Glints on raw metal and debris",0,1),
            new DevControl("REFLECTIONS","glintPlastic","Glints on plastic",0,1)
        };
    }
}
