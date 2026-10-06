using System.Collections.Generic;
using UnityEngine;
namespace MadeInArizona
{
    /// <summary>Pooled weapon recordings with synthesis fallback, layered engines and explosion ducking.</summary>
    public sealed class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }
        /// <summary>Master output; automated test runs (-miaSmokeTest) are always silent so they never play through the speakers.</summary>
        public static float OutputVolume(float master) => SmokeTestRunner.Active ? 0 : master;
        readonly List<AudioClip> clips=new List<AudioClip>();
        AudioClip[] shots,blasts,hurts,hitMarkers;AudioClip killConfirm,weaponPickup;int hitCursor;float hitAt;AudioClip ui,radio,shift,release,backfire,ordnanceBlast,nitroIgnite,repair,objective;
        AudioSource whine,road,wind,heartbeat,nitro,drift,radioSource,uiSource;
        // Engine bank: [layer] on-load and overrun loops, crossfaded by RPM and throttle like recorded car audio.
        AudioSource[] engineOn,engineOff,engineBOn,engineBOff;
        struct EngineShape
        {
            public float pulse,pressure,attack,decay,body,rasp,saturation;
            public static EngineShape From(DevTuning tuning) => new EngineShape
            {pulse=tuning.enginePulseVariation,pressure=tuning.enginePulsePressure,attack=tuning.enginePulseAttack,
                decay=tuning.enginePulseDecay,body=tuning.engineBody,rasp=tuning.engineRasp,saturation=tuning.engineSaturation};
            public bool Matches(EngineShape other) => pulse==other.pulse&&pressure==other.pressure&&attack==other.attack
                &&decay==other.decay&&body==other.body&&rasp==other.rasp&&saturation==other.saturation;
        }
        EngineShape generatedShape,targetShape;
        float engineShapeChangedAt,engineModeBlend,physicalBlend;
        /// <summary>Physical engine voice (dev model C), rendered on the audio thread for the player's own engine layout.</summary>
        EngineVoiceSource physicalEngine;
        public EngineVoiceSource PhysicalEngine => physicalEngine;
        AudioSource[] pool;int cursor,previousGear,hurtCursor;float duck=1,lastThrottle,blowoffAt,loadMix,crackleUntil,crackleAt,hurtAt,nitroLevel,nitroOffAt=-10;
        bool wasBoosting;
        MusicManager music;
        void Awake()
        {
            Instance=this;
            shift=Keep(AudioSynthesis.Mechanical(false));release=Keep(AudioSynthesis.Mechanical(true));backfire=Keep(AudioSynthesis.Backfire());
            var engineTuning=DevTuning.ForCar(GameManager.Instance?.Player);
            generatedShape=targetShape=EngineShape.From(engineTuning);
            engineModeBlend=engineTuning.enginePreserveEdges?1:0;
            physicalBlend=engineTuning.enginePhysical?1:0;
            var physicalObject=new GameObject("Physical engine");physicalObject.transform.SetParent(transform,false);
            physicalObject.AddComponent<AudioSource>();physicalEngine=physicalObject.AddComponent<EngineVoiceSource>();
            int layers=AudioSynthesis.EngineLayerHz.Length;
            engineOn=new AudioSource[layers];engineOff=new AudioSource[layers];engineBOn=new AudioSource[layers];engineBOff=new AudioSource[layers];
            for(int i=0;i<layers;i++)
            {
                var aOn=AudioSynthesis.EngineLayer(i,true,engineTuning);
                var aOff=AudioSynthesis.EngineLayer(i,false,engineTuning);
                var bOn=AudioSynthesis.EngineLayer(i,true,engineTuning,true);
                var bOff=AudioSynthesis.EngineLayer(i,false,engineTuning,true);
                AudioSynthesis.MatchEngineLevel(aOn,bOn);AudioSynthesis.MatchEngineLevel(aOff,bOff);
                engineOn[i]=Loop(aOn,70);engineOff[i]=Loop(aOff,75);
                engineBOn[i]=Loop(bOn,70);engineBOff[i]=Loop(bOff,75);
                engineBOn[i].timeSamples=engineOn[i].timeSamples;engineBOff[i].timeSamples=engineOff[i].timeSamples;
            }
            hurts=new AudioClip[3];for(int i=0;i<hurts.Length;i++)hurts[i]=Keep(AudioSynthesis.Hurt(i));heartbeat=Loop(AudioSynthesis.Heartbeat(),20);
            whine=Loop(AudioSynthesis.Turbo(),90);nitro=Loop(AudioSynthesis.NitroRoar(),65);nitroIgnite=Keep(AudioSynthesis.NitroIgnite());road=Loop(AudioSynthesis.Wind(),140);wind=Loop(AudioSynthesis.Wind(),160);drift=Loop(AudioSynthesis.DriftScrub(),95);
            shots=new AudioClip[3];blasts=new AudioClip[3];for(int i=0;i<3;i++){shots[i]=Keep(AudioSynthesis.Shot(i));blasts[i]=Keep(AudioSynthesis.Explosion(i));}
            var weaponAudio=Resources.Load<WeaponAudioBank>("Audio/Weapons/WeaponAudioBank");
            if(weaponAudio)ordnanceBlast=weaponAudio.ordnanceExplosion;
            ui=Keep(AudioSynthesis.Chirp(false));radio=Keep(AudioSynthesis.Chirp(true));repair=Keep(AudioSynthesis.Repair());objective=Keep(AudioSynthesis.ObjectiveChime());
            hitMarkers=new AudioClip[3];for(int i=0;i<hitMarkers.Length;i++)hitMarkers[i]=Keep(AudioSynthesis.HitMarker(i));killConfirm=Keep(AudioSynthesis.KillConfirm());weaponPickup=Keep(AudioSynthesis.WeaponPickup());
            CombatFeedback.HitConfirmed+=PlayHitMarker;CombatFeedback.KillConfirmed+=PlayKillConfirm;
            uiSource=gameObject.AddComponent<AudioSource>();radioSource=gameObject.AddComponent<AudioSource>();uiSource.spatialBlend=0;radioSource.spatialBlend=0;
            pool=new AudioSource[28];
            for(int i=0;i<pool.Length;i++){var child=new GameObject("Pooled effect "+i);child.transform.SetParent(transform);pool[i]=child.AddComponent<AudioSource>();pool[i].playOnAwake=false;pool[i].spatialBlend=.5f;pool[i].minDistance=16;pool[i].maxDistance=160;pool[i].rolloffMode=AudioRolloffMode.Linear;pool[i].dopplerLevel=0;}
            music=gameObject.AddComponent<MusicManager>();
        }
        AudioClip Keep(AudioClip clip){clips.Add(clip);return clip;}
        AudioSource Loop(AudioClip clip,int priority)
        {var source=gameObject.AddComponent<AudioSource>();source.clip=Keep(clip);source.spatialBlend=0;source.priority=priority;source.loop=true;source.volume=0;source.Play();return source;}
        void RefreshEngineShape(DevTuning tuning)
        {
            var shape=EngineShape.From(tuning);
            if(!shape.Matches(targetShape)){targetShape=shape;engineShapeChangedAt=Time.unscaledTime;}
            if(shape.Matches(generatedShape)||Time.unscaledTime-engineShapeChangedAt<.25f)return;
            // Render only when the user pauses on a value, never for every slider movement or audio frame.
            var on=new AudioClip[engineOn.Length];var off=new AudioClip[engineOff.Length];
            var bOn=new AudioClip[engineBOn.Length];var bOff=new AudioClip[engineBOff.Length];
            for(int i=0;i<on.Length;i++)
            {
                on[i]=AudioSynthesis.EngineLayer(i,true,tuning);off[i]=AudioSynthesis.EngineLayer(i,false,tuning);
                bOn[i]=AudioSynthesis.EngineLayer(i,true,tuning,true);bOff[i]=AudioSynthesis.EngineLayer(i,false,tuning,true);
                AudioSynthesis.MatchEngineLevel(on[i],bOn[i]);AudioSynthesis.MatchEngineLevel(off[i],bOff[i]);
            }
            for(int i=0;i<on.Length;i++)
            {
                SwapEngineClip(engineOn[i],on[i]);SwapEngineClip(engineOff[i],off[i]);
                SwapEngineClip(engineBOn[i],bOn[i]);SwapEngineClip(engineBOff[i],bOff[i]);
            }
            generatedShape=shape;
        }
        void SwapEngineClip(AudioSource source,AudioClip replacement)
        {
            var previous=source.clip;
            int sample=source.timeSamples;
            source.Stop();source.clip=Keep(replacement);
            source.timeSamples=Mathf.Clamp(sample,0,replacement.samples-1);
            source.Play();
            clips.Remove(previous);if(previous)Destroy(previous);
        }
        GameSettings Settings { get { return GameManager.Instance?.Save?.settings; } }
        void Update()
        {
            var game=GameManager.Instance;if(game==null||Settings==null)return;
            duck=Mathf.MoveTowards(duck,1,Time.unscaledDeltaTime*1.5f);if(music)music.Duck=duck;
            bool active=game.State==GameState.Playing;var player=game.Player;
            float gain=Settings.engines*duck;
            var tuning=DevTuning.ForCar(player);
            RefreshEngineShape(tuning);
            engineModeBlend=Mathf.MoveTowards(engineModeBlend,tuning.enginePreserveEdges?1:0,Time.unscaledDeltaTime*20);
            physicalBlend=Mathf.MoveTowards(physicalBlend,tuning.enginePhysical?1:0,Time.unscaledDeltaTime*20);
            if(player&&player.Damage!=null)
            {
                float rpm=Mathf.Clamp01((player.RPM-850)/6200),load=player.Throttle;
                float presence=player.Damage.IsDead?0:active?1:.3f;
                UpdateEngineBank(rpm,load,presence*(1-physicalBlend),gain);
                UpdatePhysicalEngine(player,presence,gain);
                if(active&&!player.Damage.IsDead)UpdateCrackle(player,rpm,load,gain);
                if(active&&player.Gear!=previousGear){previousGear=player.Gear;PlayAt(shift,player.transform.position,.24f*gain*tuning.shiftLevel,Random.Range(.9f,1.1f),70);}
                float boost=active&&player.Boosting&&!player.Damage.IsDead?1:0;
                UpdateNitro(player,active,boost>0,rpm,gain);
                // A turbocharged physical voice whistles itself, following its own shaft speed; the generic whine steps aside.
                var voice=physicalEngine.Voice;bool voiceTurbo=voice!=null&&voice.Turbocharged;
                float genericWhine=1-(voiceTurbo?physicalBlend:0);
                whine.pitch=.55f+rpm*1.6f;whine.volume=active?gain*(load*rpm*.027f+boost*.027f)*tuning.turboWhineLevel*genericWhine:0;
                road.pitch=.6f+player.SpeedKph/80;
                float rough=WorldBuilder.SurfaceAt(player.transform.position)==SurfaceKind.Asphalt?.12f:.36f;
                road.volume=active?Settings.environment*(Mathf.Clamp01(player.SpeedKph/80)*rough+player.DriftAmount*.13f)*duck:0;
                var tyres = player.GetComponent<TireEffects>();
                float scrub = active && tyres && tyres.IsSkidding ? Mathf.Clamp01(player.SideSlip * 1.2f + player.WheelSpin * .5f) : 0;
                drift.volume = Mathf.MoveTowards(drift.volume, Settings.environment * scrub * .42f * duck, Time.unscaledDeltaTime * 3);
                drift.pitch = pavedScrub(player) ? 1.15f : .82f;
                if(active&&lastThrottle>.7f&&load<.3f&&Time.time>blowoffAt)
                {
                    // With the turbo voice the blow-off valve only vents real boost: louder the harder the shaft was spinning.
                    float vent=voiceTurbo&&physicalBlend>.5f?Mathf.InverseLerp(.2f,.8f,voice.Spool)*1.6f:1;
                    blowoffAt=Time.time+.5f;
                    if(vent>0)PlayAt(release,player.transform.position,.18f*gain*tuning.exhaustPopLevel*vent,1+rpm*.3f,75);
                }
                lastThrottle=load;
            }
            else{UpdateEngineBank(0,0,0,0);UpdatePhysicalEngine(null,0,0);UpdateNitro(null,false,false,0,0);whine.volume=0;road.volume=0;drift.volume=0;}
            UpdateHeartbeat(active?player:null);
            wind.volume=Settings.environment*(active?.08f:.035f)*duck;
            AudioListener.volume=OutputVolume(Settings.master);
        }
        static bool pavedScrub(VehicleController player) => player.Surface == SurfaceKind.Asphalt || player.Surface == SurfaceKind.Oil;
        /// <summary>
        /// Equal-power crossfade between the neighbouring RPM layers (in log-frequency, as pitch is heard) and
        /// between on-load and overrun loops. Each layer is only pitch-shifted around its recorded rate.
        /// </summary>
        void UpdateEngineBank(float rpm,float load,float presence,float gain)
        {
            var tuning=DevTuning.ForCar(GameManager.Instance?.Player);
            var rates=AudioSynthesis.EngineLayerHz;
            float firing=40+rpm*190,position=0;
            float octave=Mathf.Log(firing,2);
            if(firing<=rates[0])position=0;
            else if(firing>=rates[rates.Length-1])position=rates.Length-1;
            else for(int i=0;i<rates.Length-1;i++)if(firing<rates[i+1]){position=i+Mathf.InverseLerp(Mathf.Log(rates[i],2),Mathf.Log(rates[i+1],2),octave);break;}
            loadMix=Mathf.MoveTowards(loadMix,Mathf.Clamp01(load*1.4f),Time.unscaledDeltaTime*(load>loadMix?6f:3.5f));
            float onGain=Mathf.Sin(loadMix*Mathf.PI*.5f),offGain=Mathf.Cos(loadMix*Mathf.PI*.5f);
            // Louder and meatier under load; the overrun layer keeps a lighter burble between shifts.
            float level=presence*gain*(.13f+.21f*loadMix+.08f*rpm);
            for(int i=0;i<rates.Length;i++)
            {
                float distance=Mathf.Abs(position-i),weight=distance>=1?0:Mathf.Cos(distance*Mathf.PI*.5f);
                float pitch=Mathf.Clamp(firing/rates[i]*tuning.enginePitch,.3f,3f);
                engineOn[i].pitch=pitch;engineOff[i].pitch=pitch;
                engineBOn[i].pitch=pitch;engineBOff[i].pitch=pitch;
                float onVolume=level*weight*onGain*tuning.engineLoadLevel;
                float offVolume=level*weight*offGain*.85f*tuning.engineOverrunLevel;
                engineOn[i].volume=onVolume*(1-engineModeBlend);engineOff[i].volume=offVolume*(1-engineModeBlend);
                engineBOn[i].volume=onVolume*engineModeBlend;engineBOff[i].volume=offVolume*engineModeBlend;
            }
        }
        /// <summary>
        /// Drives the physical voice with the player's engine layout. The game's 850–7200 tachometer is mapped onto
        /// that engine's own idle–redline range, so a diesel tops out near 3300 rpm and the bike engine near 11,500.
        /// </summary>
        /// <summary>
        /// Mix level of the physical voice against the rest of the game (-6 dB after the first playtest found it too loud).
        /// Applied after the voice's own limiter, so it changes loudness without changing the saturation character.
        /// </summary>
        const float PhysicalMixLevel=.5f;
        void UpdatePhysicalEngine(VehicleController player,float presence,float gain)
        {
            if(player&&player.Definition)physicalEngine.Bind(player.Definition.Engine,player.Stats!=null&&player.Stats.turbocharged);
            var engine=physicalEngine.Layout;if(engine==null)return;
            var tuning=DevTuning.ForCar(player);
            float revs=player?Mathf.Clamp01((player.RPM-850)/(7200-850)):0;
            float rpm=Mathf.Lerp(engine.idleRpm,engine.redlineRpm,revs)*tuning.enginePitch;
            physicalEngine.Drive(rpm,player?player.Throttle:0,presence*gain*physicalBlend*PhysicalMixLevel,tuning);
        }
        /// <summary>
        /// Nitro: a whump as the charge lights, a roaring burn that climbs in pitch with road speed while held, and a
        /// hiss of purged gas when it cuts. Brief flickers of the button do not retrigger the ignition.
        /// </summary>
        void UpdateNitro(VehicleController player,bool active,bool boosting,float rpm,float gain)
        {
            if(player&&boosting&&!wasBoosting&&Time.time-nitroOffAt>.3f)PlayAt(nitroIgnite,player.transform.position-player.transform.forward*2,.6f*gain,Random.Range(.95f,1.05f),40);
            if(player&&active&&!boosting&&wasBoosting)PlayAt(release,player.transform.position-player.transform.forward*2,.24f*gain*DevTuning.ForCar(player).exhaustPopLevel,.85f,75);
            if(boosting)nitroOffAt=Time.time;
            wasBoosting=boosting;
            nitroLevel=Mathf.MoveTowards(nitroLevel,boosting?1:0,Time.unscaledDeltaTime*(boosting?10:4));
            nitro.volume=nitroLevel*gain*.5f*DevTuning.ForCar(player).nitroRoarLevel;
            if(player)nitro.pitch=.88f+Mathf.Clamp01(player.SpeedKph/140)*.32f+rpm*.08f;
        }
        /// <summary>Lifting off at high RPM dumps unburnt fuel into the exhaust: a short run of irregular pops.</summary>
        void UpdateCrackle(VehicleController player,float rpm,float load,float gain)
        {
            if(lastThrottle>.6f&&load<.2f&&rpm>.4f)crackleUntil=Time.time+Mathf.Lerp(.35f,.9f,rpm);
            if(load>.45f)crackleUntil=0;
            if(Time.time>crackleUntil||Time.time<crackleAt)return;
            crackleAt=Time.time+Random.Range(.045f,.16f);
            PlayAt(backfire,player.transform.position-player.transform.forward*2,Random.Range(.12f,.3f)*gain*DevTuning.ForCar(player).exhaustPopLevel,Random.Range(.8f,1.25f),72);
        }
        /// <summary>Metal hull impact for the player, scaled by the share of health lost. Rapid fire is rate limited.</summary>
        public void PlayHurt(float healthFraction)
        {
            if(Settings==null||hurts==null||Time.unscaledTime<hurtAt)return;
            hurtAt=Time.unscaledTime+.11f;
            float strength=Mathf.Clamp01(.35f+healthFraction*6);
            if(uiSource)uiSource.PlayOneShot(hurts[hurtCursor++%hurts.Length],Mathf.Clamp01(Settings.weapons*(.45f+strength*.5f)));
        }
        void UpdateHeartbeat(VehicleController player)
        {
            float target=0,pitch=1;
            if(player&&player.Damage!=null&&!player.Damage.IsDead)
            {
                float health=player.Damage.Health/Mathf.Max(1,player.Damage.MaxHealth);
                float danger=Mathf.InverseLerp(.35f,.08f,health);
                target=health<.35f?Mathf.Lerp(.28f,.6f,danger):0;pitch=Mathf.Lerp(1f,1.45f,danger);
            }
            heartbeat.pitch=pitch;
            // Rides the weapons bus (the combat mix), with a floor so the warning survives a low weapons slider.
            heartbeat.volume=Mathf.MoveTowards(heartbeat.volume,target*Mathf.Max(.35f,Settings.weapons),Time.unscaledDeltaTime*.8f);
        }
        void PlayAt(AudioClip clip,Vector3 position,float volume,float pitch,int priority)
        {
            if(pool==null)return;AudioSource source=pool[cursor++%pool.Length];source.Stop();source.transform.position=position;source.clip=clip;source.volume=Mathf.Clamp01(volume);source.pitch=pitch;source.priority=priority;source.Play();
        }
        public void PlayShot(Vector3 position,int weapon)
        {if(Settings==null)return;int kind=Mathf.Clamp(weapon,0,2);PlayAt(shots[kind],position,Settings.weapons*(kind==0?.21f:.45f)*duck,Random.Range(.93f,1.06f),kind==0?85:60);}
        public void PlayShot(Vector3 position,WeaponDefinition weapon)
        {
            if (Settings == null || !weapon) return;
            var recordings = weapon.fireSounds;
            if (recordings != null && recordings.Length > 0)
            {
                var clip = recordings[Random.Range(0, recordings.Length)];
                if (clip)
                {
                    float gain=weapon.fireRate>=10?.25f:weapon.fireRate>=4?.4f:.55f;
                    PlayAt(clip, position, Settings.weapons * gain * duck, Random.Range(.97f, 1.03f), 60);
                    return;
                }
            }
            PlayShot(position, weapon.blastRadius > 0 ? 1 : weapon.id == "sweeper" || weapon.id == "boomstick" ? 2 : 0);
        }
        public void PlayExplosion(Vector3 position,float strength,ExplosionKind kind=ExplosionKind.Vehicle)
        {
            if(Settings==null)return;int size=strength>=10?2:strength>=5?1:0;
            var clip=ordnanceBlast&&(kind==ExplosionKind.Grenade||kind==ExplosionKind.Rocket||kind==ExplosionKind.Ammunition)?ordnanceBlast:blasts[size];
            PlayAt(clip,position,Settings.weapons*Mathf.Clamp(.3f+strength*.055f,.35f,.93f),Random.Range(.85f,1.04f),25);
            if(size>0)duck=Mathf.Min(duck,size==2?.3f:.55f);
        }
        /// <summary>Repair pickup: a soft ratchet, well under the hurt clang (which plays at 0.45-0.95).</summary>
        public void PlayRepair(){if(Settings==null||!uiSource||!repair)return;uiSource.PlayOneShot(repair,Mathf.Clamp01(Settings.weapons*.28f));}
        /// <summary>Objective progress: checkpoints passed and optional objectives completed.</summary>
        public void PlayObjective(){if(Settings==null||!uiSource||!objective)return;uiSource.PlayOneShot(objective,Mathf.Clamp01(Settings.environment*.45f));}
        public void PlayUI(){if(Settings==null||!uiSource)return;uiSource.PlayOneShot(ui,Settings.environment*.5f);}
        public void PlayWeaponPickup(){if(Settings==null||!uiSource||!weaponPickup)return;uiSource.PlayOneShot(weaponPickup,Mathf.Clamp01(Settings.weapons*.72f));}
        public void PlayRadio(){if(Settings==null||!radioSource)return;radioSource.PlayOneShot(radio,Settings.dialogue*.32f);}
        public void SetCombat(bool enabled){if(music)music.SetCombat(enabled);}
        /// <summary>Player hit confirmation (2D, like a shooter hitmarker). Rate limited so rapid fire ticks, not buzzes.</summary>
        void PlayHitMarker(float damage,Vector3 point)
        {
            if(Settings==null||!uiSource||hitMarkers==null||!CombatFeedback.LastHitByPlayer||Time.unscaledTime<hitAt)return;
            hitAt=Time.unscaledTime+.05f;
            uiSource.PlayOneShot(hitMarkers[hitCursor++%hitMarkers.Length],Mathf.Clamp01(Settings.weapons*Mathf.Lerp(.58f,.86f,Mathf.Clamp01(damage/40))));
        }
        void PlayKillConfirm(Vector3 point)
        {
            if(Settings==null||!uiSource||!killConfirm||!CombatFeedback.LastHitByPlayer)return;
            hitAt=Time.unscaledTime+.12f; // the kill sound replaces the final hit tick
            uiSource.PlayOneShot(killConfirm,Mathf.Clamp01(Settings.weapons*.75f));
        }
        void OnDestroy(){CombatFeedback.HitConfirmed-=PlayHitMarker;CombatFeedback.KillConfirmed-=PlayKillConfirm;foreach(var clip in clips)if(clip)Destroy(clip);if(Instance==this)Instance=null;}
    }
}
