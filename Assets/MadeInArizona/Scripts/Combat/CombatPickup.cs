using System.Collections.Generic;
using UnityEngine;

namespace MadeInArizona
{
    public enum PickupKind { Health, Nitro, Scrap, Weapon }

    /// <summary>
    /// Physical rewards from destroyed hostile vehicles. Supplies collect on contact; weapons require a swap when occupied.
    /// A drop of the field weapon already equipped is pulled to the player and merged into its ammo.
    /// </summary>
    public sealed class CombatPickup : MonoBehaviour
    {
        static readonly List<CombatPickup> active = new List<CombatPickup>();
        public static IEnumerable<CombatPickup> Active => active;
        public PickupKind Kind { get; private set; }
        public WeaponDefinition Weapon { get; private set; }
        public int Amount { get; private set; }
        const float MagnetRadius = 14, MagnetCollect = 2.2f;
        float availableAt, magnetSpeed;
        Vector3 basePosition;
        const float SupplyMagnetRadius = 12f;
        public static CombatPickup NearbyWeapon(VehicleController player)
        {
            if (!player) return null;
            CombatPickup closest = null; float nearest = 16;
            foreach (var pickup in active)
            {
                if (!pickup || pickup.Kind != PickupKind.Weapon || Time.time < pickup.availableAt || pickup.IsAmmoFor(player)) continue;
                Vector3 delta = pickup.transform.position - player.transform.position; delta.y = 0;
                if (delta.sqrMagnitude < nearest) { nearest = delta.sqrMagnitude; closest = pickup; }
            }
            return closest;
        }
        public static void DropFromEnemy(VehicleController vehicle, int archetype, EnemyFaction faction)
        {
            if (!vehicle || !GameManager.Instance || !GameManager.Instance.IsPlaying) return;
            bool boss = archetype == 7;
            bool health = boss || Random.value < .8f;
            bool nitro = boss || Random.value < .65f;
            bool scrap = boss || Random.value < .65f;
            Vector3 origin = vehicle.transform.position;
            if (health) Create(PickupKind.Health, origin + new Vector3(-2, 0, -1), null, boss ? 120 : 55);
            if (nitro) Create(PickupKind.Nitro, origin + new Vector3(2, 0, -1), null, boss ? 75 : 38);
            if (scrap) Create(PickupKind.Scrap, origin + new Vector3(0, 0, 2), null, boss ? 8 : Random.Range(1, 4));
            // Every hostile drops a weapon from its crew's arsenal.
            var drop = WeaponRules.Find(WeaponRules.EnemyDrop(faction, archetype));
            if (drop) Create(PickupKind.Weapon, origin + new Vector3(0, 0, -2), drop, WeaponRules.PickupAmmo(drop.id));
        }
        public static CombatPickup Create(PickupKind kind, Vector3 position, WeaponDefinition weapon, int amount)
        {
            var go = GameObject.CreatePrimitive(kind == PickupKind.Weapon ? PrimitiveType.Cube : PrimitiveType.Cylinder);
            go.name = kind == PickupKind.Weapon ? "Field weapon • " + weapon.displayName : kind + " pickup";
            Destroy(go.GetComponent<Collider>());
            if (GameManager.Instance != null && GameManager.Instance.World != null) go.transform.SetParent(GameManager.Instance.World.transform);
            if (Physics.Raycast(position + Vector3.up * 6, Vector3.down, out var hit, 16, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                position.y = hit.point.y;
            go.transform.position = position + Vector3.up * .8f;
            go.transform.localScale = kind == PickupKind.Weapon ? new Vector3(1.5f, .44f, .81f) : new Vector3(1.1f, .34f, 1.1f);
            var color = kind == PickupKind.Health ? new Color(.25f, 1, .43f) : kind == PickupKind.Nitro ? new Color(.22f, .8f, 1) :
                kind == PickupKind.Scrap ? new Color(1, .75f, .24f) : weapon.projectileColor;
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            material.color = color; material.SetColor("_BaseColor", color);
            material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", color * 2);
            go.GetComponent<Renderer>().sharedMaterial = material;
            var pickup = go.AddComponent<CombatPickup>();
            pickup.Kind = kind; pickup.Weapon = weapon; pickup.Amount = amount;
            pickup.basePosition = go.transform.position; pickup.availableAt = Time.time + .35f;
            return pickup;
        }
        bool IsAmmoFor(VehicleController player) =>
            Kind == PickupKind.Weapon && Weapon && player && player.Weapons && player.Weapons.FieldWeapon && player.Weapons.FieldWeapon.id == Weapon.id;
        /// <summary>Pulls a matching weapon drop toward the player, accelerating, and merges it on arrival.</summary>
        bool MagnetToAmmo(VehicleController player)
        {
            Vector3 delta = player.transform.position + Vector3.up * .8f - basePosition;
            Vector3 flat = new Vector3(delta.x, 0, delta.z);
            if (magnetSpeed <= 0 && flat.sqrMagnitude > MagnetRadius * MagnetRadius) return false;
            magnetSpeed = Mathf.Max(8, magnetSpeed + 60 * Time.deltaTime);
            float step = magnetSpeed * Time.deltaTime;
            if (delta.magnitude <= Mathf.Max(MagnetCollect, step))
            {
                player.Weapons.AddFieldAmmo(Amount);
                GameManager.Instance.Notify("AMMO • " + Weapon.displayName + " +" + Amount + " / " + player.Weapons.FieldAmmo);
                AudioManager.Instance?.PlayUI();
                Destroy(gameObject);
                return true;
            }
            basePosition += delta.normalized * step;
            transform.position = basePosition;
            return true;
        }
        void OnEnable() { if (!active.Contains(this)) active.Add(this); }
        void OnDisable() { active.Remove(this); }
        void OnDestroy() { var renderer = GetComponent<Renderer>(); if (renderer && renderer.sharedMaterial) Destroy(renderer.sharedMaterial); }
        void Update()
        {
            transform.position = basePosition + Vector3.up * (.17f * Mathf.Sin(Time.time * 3));
            transform.Rotate(Vector3.up, 85 * Time.deltaTime);
            var game = GameManager.Instance;
            if (!game || !game.IsPlaying || !game.Player || game.Player.Damage.IsDead || Time.time < availableAt) return;
            if (IsAmmoFor(game.Player)) { MagnetToAmmo(game.Player); return; }
            magnetSpeed = 0;
            Vector3 delta = transform.position - game.Player.transform.position; delta.y = 0;
            // Only supplies follow the car. Keep weapons anchored for deliberate field-slot swaps.
            if (Kind != PickupKind.Weapon && delta.sqrMagnitude <= SupplyMagnetRadius * SupplyMagnetRadius &&
                Mathf.Abs(basePosition.y - game.Player.transform.position.y) <= 5f)
            {
                float speed = 18f + game.Player.Body.linearVelocity.magnitude;
                basePosition = Vector3.MoveTowards(basePosition, game.Player.transform.position + Vector3.up * .8f, speed * Time.deltaTime);
                transform.position = basePosition + Vector3.up * (.17f * Mathf.Sin(Time.time * 3));
                delta = transform.position - game.Player.transform.position; delta.y = 0;
            }
            if (delta.sqrMagnitude > 13 || Mathf.Abs(transform.position.y - game.Player.transform.position.y) > 4) return;
            if (Kind == PickupKind.Weapon)
            {
                if (NearbyWeapon(game.Player) != this) return;
                var current = game.Player.Weapons.FieldWeapon;
                if (current && (InputManager.Instance == null || !InputManager.Instance.SwapPressed)) return;
                int oldAmmo = game.Player.Weapons.FieldAmmo;
                game.Player.Weapons.EquipField(Weapon, Amount);
                if (current && oldAmmo > 0)
                {
                    var discarded = Create(PickupKind.Weapon, game.Player.transform.position + game.Player.transform.right * 3.5f, current, oldAmmo);
                    discarded.availableAt = Time.time + 1;
                }
            }
            else if (Kind == PickupKind.Health)
            {
                if (game.Player.Damage.Health >= game.Player.Damage.MaxHealth) return;
                game.Player.Repair(Amount); game.Notify("REPAIR PICKUP • +" + Amount + " chassis");
            }
            else if (Kind == PickupKind.Nitro)
            {
                if (game.Player.BoostCharge >= .99f) return;
                game.Player.RefillNitro(Amount * .01f); game.Notify("NITRO PICKUP • +" + Amount + "%");
            }
            else
            {
                game.Save.salvage += Amount; game.Notify("SCRAP PICKUP • +" + Amount);
                SaveSystem.Save(game.Save);
            }
            Destroy(gameObject);
        }
    }
}
