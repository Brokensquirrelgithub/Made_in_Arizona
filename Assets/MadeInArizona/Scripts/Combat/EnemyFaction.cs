using UnityEngine;

namespace MadeInArizona
{
    public enum EnemyFaction { Sunsprawl, CourtesyCompliance, RoadScavengers, OpenHouseRealty, SnowbirdConvoy, CarOtaku }

    /// <summary>Faction identity, combat loadouts, and encounter placement shared by spawns and UI.</summary>
    public static class FactionRules
    {
        public const int Count = 6;
        public static string Name(EnemyFaction faction)
        {
            switch (faction)
            {
                case EnemyFaction.CourtesyCompliance: return "COURTESY COMPLIANCE";
                case EnemyFaction.RoadScavengers: return "ROAD SCAVENGERS";
                case EnemyFaction.OpenHouseRealty: return "OPEN HOUSE REALTY";
                case EnemyFaction.SnowbirdConvoy: return "SNOWBIRD CONVOY";
                case EnemyFaction.CarOtaku: return "CAR OTAKU CLUB";
                default: return "SUNSPRAWL SECURITY";
            }
        }
        public static string Tag(EnemyFaction faction)
        {
            switch (faction)
            {
                case EnemyFaction.CourtesyCompliance: return "HOA";
                case EnemyFaction.RoadScavengers: return "SCRAP";
                case EnemyFaction.OpenHouseRealty: return "OPEN";
                case EnemyFaction.SnowbirdConvoy: return "RV";
                case EnemyFaction.CarOtaku: return "OTK";
                default: return "SUN";
            }
        }
        public static Color Paint(EnemyFaction faction)
        {
            switch (faction)
            {
                case EnemyFaction.CourtesyCompliance: return new Color(.69f, .62f, .43f);
                case EnemyFaction.RoadScavengers: return new Color(.21f, .40f, .35f);
                case EnemyFaction.OpenHouseRealty: return new Color(.82f, .38f, .34f);
                case EnemyFaction.SnowbirdConvoy: return new Color(.72f, .79f, .75f);
                case EnemyFaction.CarOtaku: return new Color(.36f, .23f, .60f);
                default: return new Color(.59f, .17f, .105f);
            }
        }
        public static Color Accent(EnemyFaction faction)
        {
            switch (faction)
            {
                case EnemyFaction.CourtesyCompliance: return new Color(1, .77f, .27f);
                case EnemyFaction.RoadScavengers: return new Color(.37f, .95f, .72f);
                case EnemyFaction.OpenHouseRealty: return new Color(1, .73f, .45f);
                case EnemyFaction.SnowbirdConvoy: return new Color(.43f, .82f, 1);
                case EnemyFaction.CarOtaku: return new Color(.96f, .39f, 1);
                default: return new Color(1, .36f, .17f);
            }
        }
        public static string PrimaryWeapon(EnemyFaction faction, int archetype)
        {
            if (archetype == 5) return "riveter";
            switch (faction)
            {
                case EnemyFaction.CourtesyCompliance:
                    return archetype == 2 || archetype == 6 ? "sweeper" : "carbine";
                case EnemyFaction.RoadScavengers:
                    return archetype == 0 || archetype == 3 || archetype == 6 || archetype == 7 ? "minigun" : "boomstick";
                case EnemyFaction.OpenHouseRealty:
                    return archetype == 0 || archetype == 2 || archetype == 6 ? "sweeper" : "carbine";
                case EnemyFaction.SnowbirdConvoy:
                    return archetype == 3 || archetype == 7 ? "minigun" : "sweeper";
                case EnemyFaction.CarOtaku:
                    return archetype == 3 || archetype == 4 || archetype == 7 ? "carbine" : "minigun";
                default:
                    return archetype == 1 || archetype == 3 || archetype == 4 || archetype == 7 ? "carbine" : "riveter";
            }
        }
        public static EnemyFaction ForMission(int missionIndex, int archetype, int wave)
        {
            if (missionIndex < 0) return (EnemyFaction)((wave > 0 ? 3 : 0) + Mathf.Abs(archetype) % 3);
            switch (missionIndex)
            {
                case 2: case 7: return EnemyFaction.CourtesyCompliance;
                case 4: case 9: return EnemyFaction.RoadScavengers;
                case 1: case 11: return EnemyFaction.OpenHouseRealty;
                case 5: case 6: return EnemyFaction.SnowbirdConvoy;
                case 8: case 12: return EnemyFaction.CarOtaku;
                case 0: return wave > 0 ? EnemyFaction.RoadScavengers : EnemyFaction.Sunsprawl;
                default: return EnemyFaction.Sunsprawl;
            }
        }
        public static EnemyFaction ForHideout(string id)
        {
            int hash = 0;
            if (id != null) foreach (char c in id) hash = (hash * 31 + c) & 0x7fffffff;
            return (EnemyFaction)(hash % Count);
        }
    }
}
