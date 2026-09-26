using UnityEngine;

namespace MadeInArizona
{
    /// <summary>Loadout and drop rules are keyed by weapon ID so authored weapon assets can still tune damage.</summary>
    public static class WeaponRules
    {
        public static bool GarageWeapon(string id) => id == "riveter" || id == "sweeper" || id == "carbine" || id == "shredder" || id == "pothole";
        public static int ScrapCost(string id) => id == "sweeper" ? 12 : id == "carbine" ? 20 : id == "shredder" ? 14 : id == "pothole" ? 18 : 0;
        /// <summary>
        /// Rounds in one enemy drop and the most a player can carry, roughly Halo's limits with a little extra:
        /// a drop is about half a full load, so two matching drops fill the weapon.
        /// </summary>
        public static int PickupAmmo(string id) => AmmoLimits(id).x;
        public static int MaxAmmo(string id) => AmmoLimits(id).y;
        static Vector2Int AmmoLimits(string id)
        {
            switch (id)
            {
                case "boomstick": return new Vector2Int(12, 24);  // shotgun
                case "minigun": return new Vector2Int(100, 200);  // 20 rounds/s: ten seconds of fire
                case "sniper": return new Vector2Int(6, 14);
                case "invoice": return new Vector2Int(4, 8);      // rocket launcher
                case "grenade": return new Vector2Int(6, 12);
                case "mines": return new Vector2Int(4, 8);
                case "mortar": return new Vector2Int(3, 6);
                case "cluster": return new Vector2Int(3, 6);
                default: return new Vector2Int(0, 0);
            }
        }
        public static WeaponDefinition Find(string id)
        {
            ContentCatalog.EnsureLoaded();
            foreach (var weapon in ContentCatalog.Weapons) if (weapon && weapon.id == id) return weapon;
            return null;
        }
        public static string EnemyDrop(EnemyFaction faction, int archetype)
        {
            if (faction == EnemyFaction.CourtesyCompliance)
            {
                if (archetype == 3 || archetype == 7) return Random.value < .28f ? "mortar" : "grenade";
                return archetype == 1 || archetype == 4 ? (Random.value < .6f ? "grenade" : "mines") : "mines";
            }
            if (faction == EnemyFaction.RoadScavengers)
            {
                if (archetype == 0 || archetype == 3 || archetype == 6 || archetype == 7)
                    return Random.value < .68f ? "minigun" : "boomstick";
                return archetype == 4 ? "grenade" : "boomstick";
            }
            if (faction == EnemyFaction.OpenHouseRealty)
            {
                if (archetype == 3 || archetype == 4 || archetype == 7)
                    return Random.value < .22f ? "cluster" : "invoice";
                return Random.value < .55f ? "mines" : "grenade";
            }
            if (faction == EnemyFaction.SnowbirdConvoy)
            {
                if (archetype == 3 || archetype == 7) return Random.value < .22f ? "minigun" : "mines";
                return Random.value < .72f ? "mines" : "boomstick";
            }
            if (faction == EnemyFaction.CarOtaku)
            {
                if (archetype == 3) return Random.value < .28f ? "sniper" : "minigun";
                if (archetype == 4 || archetype == 7) return Random.value < .24f ? "cluster" : "minigun";
                return Random.value < .7f ? "minigun" : "boomstick";
            }
            if (archetype == 3) return Random.value < .3f ? "sniper" : "invoice";
            if (archetype == 4 || archetype == 7) return Random.value < .3f ? "cluster" : "invoice";
            return "invoice";
        }
    }
}
