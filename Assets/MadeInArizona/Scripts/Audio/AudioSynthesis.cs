using System;
using UnityEngine;
namespace MadeInArizona
{
    /// <summary>Deterministic original PCM assets. No samples, recordings or external melodies.</summary>
    public static class AudioSynthesis
    {
        public const int Rate=22050;
        static float Wave(float phase){return Mathf.Sin(phase*6.2831853f);}
        static float Noise(ref uint state){state=state*1664525u+1013904223u;return ((state>>8)&65535)/32767.5f-1;}
        // The generated effects can have several correlated layers at their attack.
        // A soft limit keeps that impact while guaranteeing PCM stays below full scale.
        static float SoftLimit(float sample){return (float)Math.Tanh(sample);}
        static AudioClip Clip(string name,float[] samples)
        {var clip=AudioClip.Create(name,samples.Length,1,Rate,false);clip.SetData(samples,0);return clip;}
        public static AudioClip Engine()
        {
            var samples=new float[Rate];uint seed=1703;
            for(int i=0;i<samples.Length;i++)
            {
                float t=i/(float)Rate;
                float firing=.46f*Wave(t*82)+.20f*Wave(t*164)+.10f*Wave(t*246)+.045f*Noise(ref seed);
                // A restrained crank-order layer gives the engine weight on small speakers
                // and subwoofers without turning the firing texture into a drone.
                float body=.16f*Wave(t*41)+.055f*Wave(t*61.5f);
                samples[i]=SoftLimit((firing+body)*(.8f+.2f*Wave(t*41)));
            }
            return Clip("Original • three-cylinder firing loop",samples);
        }
        public static AudioClip Exhaust()
        {
            var samples=new float[Rate];uint seed=3080;float low=0;
            for(int i=0;i<samples.Length;i++) {
                float t=i/(float)Rate;low=Mathf.Lerp(low,Noise(ref seed),.13f);
                float pulse=Mathf.Pow(.5f+.5f*Wave(t*64),5);
                samples[i]=(float)Math.Tanh((Wave(t*32)*.36f+Wave(t*64)*.24f+Wave(t*128)*.12f+low*pulse*.8f)*1.6f)*.7f;
            }
            return Clip("Original • loaded exhaust pulse",samples);
        }
        public static AudioClip Mechanical(bool release)
        {
            var samples=new float[Mathf.RoundToInt(Rate*(release?.48f:.17f))];uint seed=3081;float low=0;
            for(int i=0;i<samples.Length;i++) {
                float t=i/(float)Rate,n=Noise(ref seed);low=Mathf.Lerp(low,n,.16f);
                samples[i]=release?(n-low)*Mathf.Exp(-t*9)*Mathf.Min(1,t*100)*.55f:
                    (low*.7f+Wave(94*t)*.35f)*Mathf.Exp(-t*35);
            }
            return Clip(release?"Original • compressor bypass hiss":"Original • loaded gear engagement",samples);
        }
        public static AudioClip Wind()
        {
            var samples=new float[Rate*3];uint seed=4919;float low=0;
            for(int i=0;i<samples.Length;i++){low=Mathf.Lerp(low,Noise(ref seed),.025f);samples[i]=low*2;}
            int fade=500;for(int i=0;i<fade;i++){float mix=i/(float)fade;samples[i]*=mix;samples[samples.Length-1-i]*=mix;}
            return Clip("Original • desert air and road texture",samples);
        }
        public static AudioClip Turbo()
        {
            var samples=new float[Rate];uint seed=44;
            for(int i=0;i<samples.Length;i++){float t=i/(float)Rate;samples[i]=Wave(t*610)*.35f+Wave(t*915)*.15f+Noise(ref seed)*.045f;}
            return Clip("Original • compressor and gear whine",samples);
        }
        public static AudioClip Shot(int kind)
        {
            float length=kind==1?.7f:kind==2?.46f:.23f;
            var samples=new float[Mathf.RoundToInt(Rate*length)];uint seed=(uint)(9283+kind*439);
            for(int i=0;i<samples.Length;i++)
            {
                float t=i/(float)Rate,n=Noise(ref seed);
                float report=n*Mathf.Exp(-t*(kind==1?12:kind==2?24:58));
                float thump=Wave((kind==2?110:160)*t-40*t*t)*Mathf.Exp(-t*30)*.46f;
                float bodyHz=kind==2?72:92;
                float body=Wave(bodyHz*t-16*t*t)*Mathf.Exp(-t*(kind==1?13:20))*Mathf.Min(1,t*150)*.42f;
                float mech=n*Mathf.Exp(-Mathf.Abs(t-.042f)*230)*.25f;
                float tail=n*Mathf.Exp(-t*7)*.13f;
                // The low layer arrives just after the report, retaining a crisp initial hit.
                samples[i]=SoftLimit((report+thump+body+mech+tail)*.62f);
            }
            return Clip("Original • weapon report "+kind,samples);
        }
        public static AudioClip Explosion(int size)
        {
            float seconds=1.2f+size*.65f;var samples=new float[Mathf.RoundToInt(Rate*seconds)];uint seed=(uint)(3781+size*41);float low=0;
            for(int i=0;i<samples.Length;i++)
            {
                float t=i/(float)Rate,n=Noise(ref seed);low=Mathf.Lerp(low,n,.045f);
                float snap=n*Mathf.Exp(-t*45)*.5f;
                float pressure=Wave(53*t-7*t*t)*Mathf.Exp(-t*(3.6f-size*.55f))*.48f;
                float sub=Wave((38+size*4)*t-4*t*t)*Mathf.Exp(-t*(4.2f-size*.45f))*Mathf.Min(1,t*95)*(.34f+size*.09f);
                float roar=low*Mathf.Exp(-t*(2.7f-size*.4f))*1.15f;
                float debris=n*Mathf.Exp(-Mathf.Abs(t-.2f)*6)*.11f;
                // Keep the broadband snap above the new low pressure wave for definition.
                samples[i]=SoftLimit((snap+pressure+sub+roar+debris)*.76f);
            }
            return Clip("Original • layered blast "+size,samples);
        }
        public static AudioClip Chirp(bool radio)
        {
            var samples=new float[Rate/6];uint seed=19;
            for(int i=0;i<samples.Length;i++){float t=i/(float)Rate;float f=radio?480:740;samples[i]=(Wave(f*t+500*t*t)*.45f+(radio?Noise(ref seed)*.12f:0))*Mathf.Sin(Mathf.PI*i/samples.Length)*Mathf.Exp(-t*12);}
            return Clip(radio?"Original • radio squelch":"Original • garage switch",samples);
        }
        static float Hz(int midi){return 440*Mathf.Pow(2,(midi-69)/12f);}
        public static AudioClip Music(bool combat)
        {
            float bpm=combat?174:96,beat=60/bpm;int bars=combat?16:8;
            var samples=new float[Mathf.RoundToInt(Rate*beat*4*bars)];uint seed=combat?73193u:83117u;
            int[] garageNotes={0,7,12,7};int[] roots={50,57,54,55};int[] melody={74,78,81,78,76,81,83,81,78,76,74,81,85,83,81,78};
            float kickPhase=0;
            for(int i=0;i<samples.Length;i++)
            {
                float t=i/(float)Rate,b=t/beat;int beatIndex=(int)b,bar=beatIndex/4,root=roots[(bar/2)%4];float phase=b-beatIndex,bt=phase*beat;
                float n=Noise(ref seed);float signal=0;
                if(combat)
                {
                    float kickHz=45+135*Mathf.Exp(-bt*40);kickPhase+=kickHz/Rate;
                    signal+=Wave(kickPhase)*Mathf.Exp(-bt*15)*.48f;
                    if((beatIndex%4)==1||(beatIndex%4)==3)signal+=n*Mathf.Exp(-bt*22)*.2f+Wave(bt*185)*Mathf.Exp(-bt*28)*.1f;
                    float hatTime=(b*2-Mathf.Floor(b*2))*beat*.5f;signal+=n*Mathf.Exp(-hatTime*95)*.065f;
                    float offbeat=Mathf.Max(0,(phase-.5f)*beat);
                    if(phase>.5f)signal+=(Wave(t*Hz(root))+.18f*Wave(t*Hz(root)*2))*Mathf.Exp(-offbeat*12)*.15f;
                    int note=melody[((int)(b*2)+bar/4*3)%melody.Length];float noteT=(b*2-Mathf.Floor(b*2))*beat*.5f;
                    float duck=.35f+.65f*Mathf.Clamp01(bt/.08f);
                    signal+=(Wave(t*Hz(note))*.075f+Wave(t*Hz(note)*1.004f)*.055f+Wave(t*Hz(note)*2)*.018f)*Mathf.Exp(-noteT*4)*duck;
                    signal+=(Wave(t*Hz(root+12))+Wave(t*Hz(root+19))+Wave(t*Hz(root+24)))*.017f*duck;
                }
                else
                {
                    if(beatIndex%2==0){kickPhase+=(48+50*Mathf.Exp(-bt*35))/Rate;signal+=Wave(kickPhase)*Mathf.Exp(-bt*19)*.22f;}
                    if(beatIndex%4==1||beatIndex%4==3)signal+=n*Mathf.Exp(-bt*40)*.09f;
                    float sub=(b*2-Mathf.Floor(b*2))*beat*.5f;signal+=n*Mathf.Exp(-sub*150)*.024f;
                    signal+=Wave(t*Hz(root-12))*Mathf.Exp(-bt*5)*.13f;
                    int note=root+12+garageNotes[beatIndex%4];
                    signal+=(Wave(t*Hz(note))+.3f*Wave(t*Hz(note)*2)+.12f*Wave(t*Hz(note)*3))*Mathf.Exp(-bt*5)*.15f;
                    signal+=Wave(t*Hz(root+19))*.015f;
                }
                samples[i]=Mathf.Clamp(signal,-.92f,.92f);
            }
            int edge=Rate/50;for(int i=0;i<edge;i++){float gain=i/(float)edge;samples[i]*=gain;samples[samples.Length-1-i]*=gain;}
            return Clip(combat?"Original soundtrack • Invoice at 174 BPM":"Original soundtrack • Last Bay on the Left",samples);
        }
    }
}
