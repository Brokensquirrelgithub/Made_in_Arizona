using UnityEngine;

namespace MadeInArizona
{
    /// <summary>Loadout and drop rules are keyed by weapon ID so authored weapon assets can still tune damage.</summary>
    public static class WeaponRules
    {
        public static bool GarageWeapon(string id) => id == "riveter" || id == "sweeper" || id == "carbine";
        public static int ScrapCost(string id) => id == "sweeper" ? 12 : id == "carbine" ? 20 : 0;
        public static int PickupAmmo(string id)
        {
            switch (id)
            {
                case "invoice": return 4;
                case "grenade": return 10;
                case "mortar": return 3;
                case "mines": return 6;
                case "minigun": return 120;
                case "sniper": return 5;
                case "cluster": return 3;
                case "boomstick": return 14;
                default: return 0;
            }
        }
        public static string DropHint(EnemyFaction faction, int archetype)
        {
            if (faction == EnemyFaction.CourtesyCompliance)
            {
                if (archetype == 3 || archetype == 7) return "MORTAR / GRENADES";
                return archetype == 1 || archetype == 4 ? "GRENADES / MINES" : "MINES / GRENADES";
            }
            if (faction == EnemyFaction.RoadScavengers)
                return archetype == 0 || archetype == 3 || archetype == 6 || archetype == 7 ? "MINIGUN / SHOTGUN" : "SHOTGUN / GRENADES";
            if (faction == EnemyFaction.OpenHouseRealty)
                return archetype == 3 || archetype == 4 || archetype == 7 ? "CLUSTER / ROCKET" : "MINES / GRENADES";
            if (faction == EnemyFaction.SnowbirdConvoy)
                return archetype == 3 || archetype == 7 ? "MINIGUN / MINES" : "MINES / SHOTGUN";
            if (faction == EnemyFaction.CarOtaku)
                return archetype == 3 ? "SNIPER / MINIGUN" : archetype == 4 || archetype == 7 ? "CLUSTER / MINIGUN" : "MINIGUN / SHOTGUN";
            if (archetype == 3) return "SNIPER / ROCKET";
            if (archetype == 4 || archetype == 7) return "CLUSTER / ROCKET";
            return "ROCKET";
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
