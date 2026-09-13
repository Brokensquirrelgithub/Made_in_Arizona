using UnityEngine;

namespace MadeInArizona
{
    public sealed class WeaponSystem : MonoBehaviour
    {
        public float Heat { get; private set; }
        public bool Overheated { get; private set; }
        public float RocketCooldown => Mathf.Clamp01((secondaryAt - Time.time) / 1.7f);
        public float ShotgunCooldown => Mathf.Clamp01((tertiaryAt - Time.time) / .85f);
        public int PrimaryIndex = 0;
        VehicleController owner;
        Transform turret, muzzleTransform;
        Light muzzleFlash;
        Vector3 turretRestPosition;
        float primaryAt, secondaryAt, tertiaryAt, primaryFiredAt;
        float recoil, recoilVelocity;
        Vector3 aimDirection = Vector3.forward;
        public void Initialize(VehicleController vehicle)
        {
            owner = vehicle;
            turret = VehicleController.FindChild(vehicle.Visual, "Turret");
            muzzleTransform = VehicleController.FindChild(vehicle.Visual, "Muzzle");
            if (turret != null) turretRestPosition = turret.localPosition;
            if (muzzleTransform != null)
            {
                muzzleFlash = muzzleTransform.GetComponent<Light>();
                if (muzzleFlash == null) muzzleFlash = muzzleTransform.gameObject.AddComponent<Light>();
                muzzleFlash.type = LightType.Point;
                muzzleFlash.range = 7;
                muzzleFlash.intensity = 0;
                muzzleFlash.shadows = LightShadows.None;
                muzzleFlash.color = new Color(1, .66f, .22f);
            }
        }
        void Update()
        {
            if (muzzleFlash != null) muzzleFlash.intensity = Mathf.MoveTowards(muzzleFlash.intensity, 0, Time.deltaTime * 95);
            if (owner == null || owner.Damage.IsDead) return;
            // Riveter heat is accumulated per round. Cooling only begins after the trigger is released.
            if (Time.time - primaryFiredAt > .13f)
                Heat = Mathf.Max(0, Heat - Time.deltaTime * (Overheated ? .43f : .3f) * Mathf.Max(.4f, owner.Stats.cooling));
            if (Overheated && Heat < .25f) Overheated = false;
            if (turret != null && aimDirection.sqrMagnitude > .1f)
            {
                Quaternion desired = Quaternion.LookRotation(aimDirection, Vector3.up);
                turret.rotation = Quaternion.Slerp(turret.rotation, desired, Time.deltaTime * 25);
                recoil = Mathf.SmoothDamp(recoil, 0, ref recoilVelocity, .075f, 20, Time.deltaTime);
                turret.localPosition = turretRestPosition - Vector3.forward * recoil;
            }
        }
        public void AimAt(Vector3 direction)
        {
            if(!GeneratedWorld.Active)direction.y = 0;
            if (direction.sqrMagnitude > .001f) aimDirection = direction.normalized;
        }
        public void FirePrimary(Vector3 direction)
        {
            if (!CanFire() || Time.time < primaryAt || Overheated) return;
            ContentCatalog.EnsureLoaded();
            if (ContentCatalog.Weapons.Length == 0) return;
            var weapon = ContentCatalog.Weapons[Mathf.Clamp(PrimaryIndex, 0, ContentCatalog.Weapons.Length - 1)];
            primaryAt = Time.time + 1f / Mathf.Clamp(weapon.fireRate, 1, 25);
            primaryFiredAt = Time.time;
            AimAt(direction);
            float configuredHeat = weapon.heat > 1 ? weapon.heat * .01f : weapon.heat;
            Heat = Mathf.Clamp01(Heat + Mathf.Clamp(configuredHeat * .48f, .01f, .1f));
            if (Heat >= 1)
            {
                Overheated = true;
                if (owner.IsPlayer) GameManager.Instance?.Notify("RIVETER OVERHEATED • RELEASE TO COOL");
            }
            float inaccuracy = Mathf.Lerp(.75f, 2.8f, Heat * Heat);
            Vector3 aim = Quaternion.AngleAxis(Random.Range(-inaccuracy, inaccuracy), Vector3.up) * aimDirection;
            Vector3 muzzle = Muzzle(aim);
            float damage = weapon.damage * (owner.IsPlayer ? WorldExploration.PlayerWeaponMultiplier(0) : .65f);
            ProjectileSystem.Fire(muzzle, aim, Mathf.Max(75, weapon.speed), damage, weapon.blastRadius, owner.gameObject, weapon.projectileColor, weapon.blastRadius > .1f ? ExplosionKind.Grenade : ExplosionKind.Ammunition, 1.4f);
            ExplosionSystem.Burst(muzzle, new Color(1, .77f, .32f), 3, 1);
            Flash(new Color(1, .68f, .24f), 6.5f, .055f);
            AudioManager.Instance?.PlayShot(muzzle, 0);
        }
        public void FireSecondary(Vector3 direction)
        {
            if (!CanFire() || Time.time < secondaryAt) return;
            secondaryAt = Time.time + (owner.IsPlayer ? 1.7f : 3.3f);
            AimAt(direction);
            Vector3 muzzle = Muzzle(aimDirection);
            ProjectileSystem.Fire(muzzle, aimDirection, 49, owner.IsPlayer ? 110*WorldExploration.PlayerWeaponMultiplier(1) : 62, owner.IsPlayer?6*WorldExploration.PlayerRocketRadiusMultiplier():6, owner.gameObject, new Color(1, .35f, .08f), ExplosionKind.Rocket, 2.7f);
            ExplosionSystem.Burst(muzzle, new Color(1, .5f, .1f), 8, 2);
            Flash(new Color(1, .3f, .06f), 13, .2f);
            AudioManager.Instance?.PlayShot(muzzle, 1);
            if (owner.IsPlayer) CameraController.Instance?.Shake(.12f);
        }
        public void FireTertiary(Vector3 direction)
        {
            if (!CanFire() || Time.time < tertiaryAt) return;
            tertiaryAt = Time.time + .85f;
            AimAt(direction);
            Vector3 muzzle = Muzzle(aimDirection);
            for (int i = 0; i < 8; i++)
            {
                float fan = (i - 3.5f) * 3.7f + Random.Range(-.85f, .85f);
                Vector3 spread = Quaternion.AngleAxis(fan, Vector3.up) * aimDirection;
                ProjectileSystem.Fire(muzzle, spread, 96 + Random.Range(-8, 8), owner.IsPlayer ? 16*WorldExploration.PlayerWeaponMultiplier(2) : 10, 0, owner.gameObject, new Color(.4f, .94f, 1), ExplosionKind.Ammunition, .42f);
            }
            ExplosionSystem.Burst(muzzle, new Color(.3f, .9f, 1), 13, 2);
            Flash(new Color(.3f, .9f, 1), 10, .15f);
            AudioManager.Instance?.PlayShot(muzzle, 2);
            if (owner.IsPlayer) CameraController.Instance?.Shake(.1f);
        }
        Vector3 Muzzle(Vector3 direction)
        {
            // Keep collision at body height so horizontal fire still catches compact vehicles and props.
            if (muzzleTransform != null)
                return new Vector3(muzzleTransform.position.x, transform.position.y + .85f, muzzleTransform.position.z);
            // Fallback for vehicles assembled without the standard visual hierarchy.
            return transform.position + Vector3.up * .85f + direction * 2.4f;
        }
        void Flash(Color color, float intensity, float kick)
        {
            recoil = Mathf.Min(.28f, recoil + kick);
            if (muzzleFlash == null) return;
            muzzleFlash.color = color;
            muzzleFlash.intensity = intensity;
        }
        bool CanFire() => owner != null && !owner.Damage.IsDead && GameManager.Instance != null && GameManager.Instance.IsPlaying;
    }
}
