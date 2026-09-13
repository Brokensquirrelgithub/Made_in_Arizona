using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
namespace MadeInArizona
{
    [Serializable]
    public sealed class GameSettings
    {
        public float master=.8f, music=.48f, engines=.62f, weapons=.82f, dialogue=.8f, environment=.5f, shake=.6f, uiScale=1;
        public int quality=2, difficulty=1, aimAssist=1, width=1600, height=900, windowMode=2;
        public bool subtitles=true, fullscreen=false;
        public string bindingOverrides="";
        public DevTuning dev=new DevTuning();
        public void Clamp()
        {
            if(dev==null)dev=new DevTuning(); dev.Clamp();
            master=Unit(master,.8f); music=Unit(music,.48f); engines=Unit(engines,.62f); weapons=Unit(weapons,.82f);
            dialogue=Unit(dialogue,.8f); environment=Unit(environment,.5f); shake=Unit(shake,.6f);
            uiScale=Finite(uiScale)?Mathf.Clamp(uiScale,.75f,1.5f):1;
            quality=Mathf.Clamp(quality,0,3); difficulty=Mathf.Clamp(difficulty,0,2); aimAssist=Mathf.Clamp(aimAssist,0,2);
            width=Mathf.Clamp(width,960,7680); height=Mathf.Clamp(height,540,4320); windowMode=Mathf.Clamp(windowMode,0,2);
            if(bindingOverrides==null || bindingOverrides.Length>200000) bindingOverrides="";
        }
        static bool Finite(float f) { return !float.IsNaN(f)&&!float.IsInfinity(f); }
        static float Unit(float f,float fallback) { return Finite(f)?Mathf.Clamp01(f):fallback; }
    }
    [Serializable]
    public sealed class SaveData
    {
        public int version=1, money=1100, salvage, reputation, selectedVehicle, selectedDriver, unlockedMission, dogCosmetic;
        public List<int> completedMissions=new List<int>(), bestScores=new List<int>();
        public List<string> ownedParts=new List<string>(), installedParts=new List<string>(), ownedVehicles=new List<string>{"thimble","juniper"}, collectibles=new List<string>(), achievements=new List<string>();
        public float finalDriveTuning=1, rideHeightTuning;
        public GameSettings settings=new GameSettings();
        public void Normalize()
        {
            ContentCatalog.EnsureLoaded();
            money=Mathf.Clamp(money,0,100000000); salvage=Mathf.Clamp(salvage,0,1000000); reputation=Mathf.Clamp(reputation,0,1000000);
            selectedVehicle=Mathf.Clamp(selectedVehicle,0,ContentCatalog.Vehicles.Length-1); selectedDriver=Mathf.Clamp(selectedDriver,0,ContentCatalog.Drivers.Length-1);
            unlockedMission=Mathf.Clamp(unlockedMission,0,ContentCatalog.Missions.Length-1); dogCosmetic=Mathf.Clamp(dogCosmetic,0,3);
            if(completedMissions==null)completedMissions=new List<int>(); completedMissions.RemoveAll(i=>i<0||i>=ContentCatalog.Missions.Length);
            completedMissions=new List<int>(new HashSet<int>(completedMissions));
            if(bestScores==null)bestScores=new List<int>(); while(bestScores.Count<ContentCatalog.Missions.Length)bestScores.Add(0);
            if(bestScores.Count>ContentCatalog.Missions.Length)bestScores.RemoveRange(ContentCatalog.Missions.Length,bestScores.Count-ContentCatalog.Missions.Length);
            for(int i=0;i<bestScores.Count;i++)bestScores[i]=Mathf.Clamp(bestScores[i],0,100000000);
            ownedParts=Clean(ownedParts); installedParts=Clean(installedParts); ownedVehicles=Clean(ownedVehicles); collectibles=Clean(collectibles); achievements=Clean(achievements);
            ownedParts.RemoveAll(id=>!Array.Exists(ContentCatalog.Parts,p=>p.id==id)); installedParts.RemoveAll(id=>!ownedParts.Contains(id));
            if(!ownedVehicles.Contains("thimble"))ownedVehicles.Add("thimble"); if(!ownedVehicles.Contains("juniper"))ownedVehicles.Add("juniper");
            if(!ownedVehicles.Contains(ContentCatalog.Vehicles[selectedVehicle].id))selectedVehicle=0;
            if(float.IsNaN(finalDriveTuning)||float.IsInfinity(finalDriveTuning))finalDriveTuning=1;
            if(float.IsNaN(rideHeightTuning)||float.IsInfinity(rideHeightTuning))rideHeightTuning=0;
            finalDriveTuning=Mathf.Clamp(finalDriveTuning,.85f,1.2f); rideHeightTuning=Mathf.Clamp(rideHeightTuning,-.08f,.15f);
            if(settings==null)settings=new GameSettings(); settings.Clamp();
        }
        static List<string> Clean(List<string> list)
        {
            if(list==null)return new List<string>();
            list.RemoveAll(s=>string.IsNullOrEmpty(s)||s.Length>128); if(list.Count>2048)list.RemoveRange(2048,list.Count-2048);
            return new List<string>(new HashSet<string>(list));
        }
    }
    public static class SaveSystem
    {
        [Serializable] sealed class Envelope { public int format=1; public string payload, checksum; }
        public static string DirectoryPath => SmokeTestRunner.Active ? System.IO.Path.Combine(Application.temporaryCachePath, "IntegrationTestSave") : Application.persistentDataPath;
        public static string Path { get { return System.IO.Path.Combine(DirectoryPath,"made-in-arizona.save.json"); } }
        public static string LastError { get; private set; }
        public static bool RecoveredBackup { get; private set; }
        static bool testInitialized;
        public static SaveData Load()
        {
            LastError=null; RecoveredBackup=false;
            if (SmokeTestRunner.Active && !testInitialized) { testInitialized = true; var fresh = new SaveData(); fresh.Normalize(); return fresh; }
            SaveData data;
            if(TryRead(Path,out data))return data;
            if(TryRead(Path+".bak",out data)) { RecoveredBackup=true; return data; }
            if(TryRead(Path+".tmp",out data)) { RecoveredBackup=true; return data; }
            data=new SaveData(); data.Normalize(); return data;
        }
        static bool TryRead(string path,out SaveData data)
        {
            data=null; if(!File.Exists(path))return false;
            try
            {
                if(new FileInfo(path).Length>2097152)throw new InvalidDataException("Save exceeds size limit.");
                string json=File.ReadAllText(path,Encoding.UTF8); var env=JsonUtility.FromJson<Envelope>(json);
                if(env==null||env.format!=1||string.IsNullOrEmpty(env.payload)||env.checksum!=Hash(env.payload))throw new InvalidDataException("Save checksum mismatch.");
                data=JsonUtility.FromJson<SaveData>(env.payload); if(data==null||data.version!=1)throw new InvalidDataException("Unsupported save version.");
                data.Normalize(); return true;
            }
            catch(Exception e) { LastError="Could not read save: "+e.Message; data=null; return false; }
        }
        public static void Save(SaveData data)
        {
            if(data==null)return;
            try
            {
                data.Normalize(); Directory.CreateDirectory(DirectoryPath);
                string payload=JsonUtility.ToJson(data); string json=JsonUtility.ToJson(new Envelope{payload=payload,checksum=Hash(payload)},true);
                byte[] bytes=Encoding.UTF8.GetBytes(json); string temp=Path+".tmp";
                using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)) { stream.Write(bytes,0,bytes.Length); stream.Flush(true); }
                if(File.Exists(Path))
                {
                    try { File.Replace(temp,Path,Path+".bak"); }
                    catch(PlatformNotSupportedException) { BackupMove(temp); }
                    catch(IOException) { BackupMove(temp); }
                }
                else File.Move(temp,Path);
                LastError=null;
            }
            catch(Exception e) { LastError="Could not save progress: "+e.Message; Debug.LogWarning(LastError); }
        }
        static void BackupMove(string temp) { File.Copy(Path,Path+".bak",true); File.Delete(Path); File.Move(temp,Path); }
        static string Hash(string value)
        { using(var sha=SHA256.Create()) { return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(value))); } }
    }
}
