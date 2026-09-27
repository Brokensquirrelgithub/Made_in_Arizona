using UnityEngine;

namespace MadeInArizona
{
    public static class SpawnManager
    {
        public const int EnemyCountMultiplier = 4;
        public const float EnemyHealthMultiplier = .2f;
        public static int EnemyCount(int baseline) => baseline * EnemyCountMultiplier;

        public static VehicleController Spawn(Vector3 position, int archetype, VehicleController target, EnemyFaction faction = EnemyFaction.Sunsprawl)
        {
            ContentCatalog.EnsureLoaded();
            if (ContentCatalog.Vehicles.Length == 0) return null;
            int[] roster = { 6, 5, 3, 0, 4, 0, 7, 7 };
            int type = Mathf.Clamp(archetype, 0, roster.Length - 1);
            var definition = ContentCatalog.Vehicles[roster[type] % ContentCatalog.Vehicles.Length];
            var stats = VehicleStats.From(definition);
            int difficulty = GameManager.Instance != null && GameManager.Instance.Save != null ? GameManager.Instance.Save.settings.difficulty : 1;
            stats.maxHealth *= type == 7 ? 5f : type == 2 || type == 6 ? 1.2f : type == 5 ? .3f : .7f;
            stats.maxHealth *= difficulty == 0 ? .78f : difficulty == 2 ? 1.15f : 1;
            stats.maxSpeed *= type == 0 ? 1.05f : type == 7 ? .62f : .8f;
            stats.horsepower *= type == 7 ? 2.8f : 1;
            stats.torque *= type == 7 ? 3 : 1;
            if (faction == EnemyFaction.SnowbirdConvoy) { stats.maxSpeed *= .76f; stats.maxHealth *= 1.25f; }
            if (faction == EnemyFaction.CarOtaku) { stats.maxSpeed *= 1.3f; stats.horsepower *= 1.25f; stats.maxHealth *= .85f; }
            if (type == 7) { stats.mass *= 2.6f; stats.trackWidth *= 1.8f; stats.wheelbase *= 1.8f; stats.springStiffness *= 2.6f; stats.damping *= 2.6f; stats.turnSpeed = 38; }
            var go = new GameObject(FactionRules.Name(faction) + (type == 7 ? " mobile command" : " · " + type));
            if (GameManager.Instance != null && GameManager.Instance.World != null) go.transform.SetParent(GameManager.Instance.World.transform);
            go.transform.position = position + Vector3.up * .2f;
            if (target != null)
            {
                Vector3 facing = target.transform.position - position; facing.y = 0;
                if (facing.sqrMagnitude > .01f) go.transform.rotation = Quaternion.LookRotation(facing);
            }
            var controller = go.AddComponent<VehicleController>();
            controller.Initialize(definition, stats, false, faction);
            controller.Weapons.ConfigureEnemyPrimary(FactionRules.PrimaryWeapon(faction, type));
            var ai = go.AddComponent<EnemyAI>(); ai.Initialize(target, type, faction);
            if (type == 7)
            {
                controller.Visual.localScale = Vector3.one * 1.8f;
                AddWeakPoint(controller, "Radiator", new Vector3(0, .6f, stats.wheelbase * .5f + .65f), new Vector3(1.6f, .8f, .38f));
                AddWeakPoint(controller, "Wheels", new Vector3(-stats.trackWidth * .6f, .7f, 0), new Vector3(.48f, .85f, 1.6f));
                AddWeakPoint(controller, "Transmission", new Vector3(stats.trackWidth * .6f, .7f, 0), new Vector3(.48f, .85f, 1.6f));
                AddWeakPoint(controller, "Weapon", new Vector3(-1.3f, 1, -stats.wheelbase * .5f - .55f), new Vector3(1, 1.2f, .7f));
                AddWeakPoint(controller, "Weapon", new Vector3(1.3f, 1, -stats.wheelbase * .5f - .55f), new Vector3(1, 1.2f, .7f));
            }
            return controller;
        }
        static void AddWeakPoint(VehicleController vehicle, string component, Vector3 position, Vector3 scale)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            // On the hull so the solid panels tilt with the slope instead of plowing into climbs.
            part.name = "Exposed " + component; part.transform.SetParent(vehicle.Hull.transform, false);
            part.transform.localPosition = position; part.transform.localScale = scale;
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            material.name = "Boss component warning cyan"; material.SetColor("_BaseColor", new Color(.08f, .68f, .75f));
            material.SetColor("_EmissionColor", new Color(.1f, .6f, .8f)); material.EnableKeyword("_EMISSION");
            part.GetComponent<Renderer>().sharedMaterial = material;
            part.AddComponent<BossWeakPoint>().Initialize(vehicle, component, vehicle.Damage.MaxHealth * .075f);
        }
    }
}
