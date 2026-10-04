using UnityEngine;

namespace MadeInArizona
{
    /// <summary>Loadout and drop rules are keyed by weapon ID so authored weapon assets can still tune damage.</summary>
    public static class WeaponRules
    {
        /// <summary>Scrap weapons bought in the garage: core sandbox roles, then oddball hybrids.</summary>
        static readonly string[] Garage = {
            "riveter", "sweeper", "carbine", "shredder", "pothole", "needler", "zapper", "torch", "railgun", "aircannon",
            "sprinkler", "pinata", "shopvac", "deathray", "crossbow", "bowling", "boomerang", "harpoon", "firecracker", "sentry", "scattermortar" };
        /// <summary>Oddballs splice two firing modes into one weapon (grenade + flamethrower, rocket + cluster bomb…).</summary>
        static readonly string[] Oddballs = { "sprinkler", "pinata", "shopvac", "deathray", "crossbow", "bowling", "boomerang", "harpoon", "firecracker", "sentry", "scattermortar" };
        public static string[] GarageIds => Garage;
        public static bool GarageWeapon(string id) => System.Array.IndexOf(Garage, id) >= 0;
        public static bool Oddball(string id) => System.Array.IndexOf(Oddballs, id) >= 0;
        /// <summary>Scrap price in the garage (ten times the original prices; the starter nail gun is free).</summary>
        public static int ScrapCost(string id)
        {
            switch (id)
            {
                case "sweeper": return 120;
                case "shredder": return 140;
                case "pothole": return 180;
                case "carbine": return 200;
                case "aircannon": return 200;
                case "zapper": return 220;
                case "needler": return 240;
                case "boomerang": return 240;
                case "torch": return 260;
                case "bowling": return 260;
                case "harpoon": return 260;
                case "crossbow": return 280;
                case "sprinkler": return 300;
                case "firecracker": return 300;
                case "railgun": return 320;
                case "pinata": return 340;
                case "deathray": return 360;
                case "shopvac": return 380;
                case "sentry": return 400;
                case "scattermortar": return 360;
                default: return 0;
            }
        }
        /// <summary>Weapons without imported recordings borrow the clips of the closest-sounding weapon.</summary>
        public static string SoundDonor(string id)
        {
            switch (id)
            {
                case "needler": case "zapper": return "carbine";
                case "torch": case "sentry": return "riveter";
                case "deathray": return "minigun";
                case "railgun": case "crossbow": case "harpoon": return "sniper";
                case "aircannon": case "firecracker": return "boomstick";
                case "sprinkler": return "grenade";
                case "shopvac": case "bowling": case "scattermortar": return "mortar";
                case "pinata": return "invoice";
                case "boomerang": return "shredder";
                case "gokart": return "mines";
                default: return "riveter";
            }
        }
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
                case "mines": return new Vector2Int(4, 8);        // enemy-only road charges
                case "gokart": return new Vector2Int(3, 6);       // seeking go-kart bombs
                case "mortar": return new Vector2Int(3, 6);
                case "cluster": return new Vector2Int(3, 6);
                case "scattermortar": return new Vector2Int(4, 8);
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
                if (archetype == 3 || archetype == 7) return Random.value < .18f ? "scattermortar" : Random.value < .28f ? "mortar" : "grenade";
                return archetype == 1 || archetype == 4 ? (Random.value < .6f ? "grenade" : "gokart") : "gokart";
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
                return Random.value < .55f ? "gokart" : "grenade";
            }
            if (faction == EnemyFaction.SnowbirdConvoy)
            {
                if (archetype == 3 || archetype == 7) return Random.value < .22f ? "minigun" : "gokart";
                return Random.value < .72f ? "gokart" : "boomstick";
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
