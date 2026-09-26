using UnityEngine;

namespace MadeInArizona
{
    /// <summary>Boss components are separate hit targets, with mechanical consequences when broken.</summary>
    public sealed class BossWeakPoint : MonoBehaviour
    {
        public string ComponentName;
        public float Health = 110;
        public bool Disabled { get; private set; }
        VehicleController owner;
        Renderer shell;
        MaterialPropertyBlock block;
        void Awake() { block = new MaterialPropertyBlock(); }
        public void Initialize(VehicleController vehicle, string componentName, float health)
        {
            owner = vehicle; ComponentName = componentName; Health = health; shell = GetComponent<Renderer>();
        }
        public void ApplyDamage(float amount, Vector3 hitPoint, GameObject source, bool explosive = false)
        {
            if (Disabled || owner == null || owner.Damage.IsDead) return;
            var attacker = source ? source.GetComponentInParent<VehicleController>() : null;
            if (!explosive && attacker && attacker != owner && VehicleController.Allied(attacker, owner)) return;
            Health -= amount;
            owner.Damage.ApplyDamage(amount * .45f, hitPoint, source, explosive);
            ExplosionSystem.Burst(hitPoint, new Color(.3f, 1, 1), 5, 3);
            if (Health > 0) return;
            Disabled = true;
            if (ComponentName == "Weapon") owner.GetComponent<EnemyAI>()?.DisableWeapon();
            else owner.Damage.DamageComponent(ComponentName, .85f);
            if (shell != null)
            {
                block.SetColor("_BaseColor", new Color(.1f, .08f, .07f));
                block.SetColor("_EmissionColor", Color.black); shell.SetPropertyBlock(block);
            }
            ExplosionSystem.Detonate(transform.position, 3, 0, source, ComponentName == "Radiator" ? ExplosionKind.Propane : ExplosionKind.Electrical);
            GameManager.Instance?.Notify("COMMAND RIG: " + ComponentName.ToUpperInvariant() + " DISABLED");
            // A disabled exposed component also strips a chunk of structural armor.
            owner.Damage.ApplyDamage(owner.Damage.MaxHealth * .08f, hitPoint, source, explosive);
        }
        void Update()
        {
            if (Disabled || shell == null) return;
            float pulse = 1.4f + Mathf.Sin(Time.time * 5) * .5f;
            block.SetColor("_BaseColor", new Color(.1f, .65f, .7f));
            block.SetColor("_EmissionColor", new Color(.04f, .7f, .85f) * pulse); shell.SetPropertyBlock(block);
        }
    }
}
