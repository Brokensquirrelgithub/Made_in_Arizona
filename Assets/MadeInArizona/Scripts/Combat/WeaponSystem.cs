using UnityEngine;

namespace MadeInArizona
{
    public sealed class WeaponSystem : MonoBehaviour
    {
        public WeaponDefinition GarageWeapon { get; private set; }
        public WeaponDefinition FieldWeapon { get; private set; }
        public int FieldAmmo { get; private set; }
        public float GarageCooldown => GarageWeapon ? Mathf.Clamp01((garageAt - Time.time) * Mathf.Max(.1f, GarageWeapon.fireRate)) : 0;
        public float FieldCooldown => FieldWeapon ? Mathf.Clamp01((fieldAt - Time.time) * Mathf.Max(.1f, FieldWeapon.fireRate)) : 0;
        VehicleController owner;
        Transform turret, muzzleTransform;
        Light muzzleFlash;
        LineRenderer lobMarker;
        Material lobMarkerMaterial;
        VehicleController lobTarget;
        Vector3 turretRestPosition;
        Vector3 aimDirection = Vector3.forward;
        float garageAt, fieldAt, enemyRocketAt, recoil, recoilVelocity, lobTargetUntil;

        public VehicleController AssistedTarget => lobTarget;

        public void Initialize(VehicleController vehicle)
        {
            owner = vehicle;
            GarageWeapon = WeaponRules.Find(vehicle.IsPlayer ? GameManager.Instance?.Save?.selectedWeapon : "riveter") ?? WeaponRules.Find("riveter");
            if (!WeaponRules.GarageWeapon(GarageWeapon.id)) GarageWeapon = WeaponRules.Find("riveter");
            FieldWeapon = null; FieldAmmo = 0; garageAt = fieldAt = 0;
            turret = VehicleController.FindChild(vehicle.Visual, "Turret");
            muzzleTransform = VehicleController.FindChild(vehicle.Visual, "Muzzle");
            if (turret != null) turretRestPosition = turret.localPosition;
            if (muzzleTransform != null)
            {
                muzzleFlash = muzzleTransform.GetComponent<Light>();
                if (muzzleFlash == null) muzzleFlash = muzzleTransform.gameObject.AddComponent<Light>();
                muzzleFlash.type = LightType.Point; muzzleFlash.range = 7;
                muzzleFlash.intensity = 0; muzzleFlash.shadows = LightShadows.None;
            }
        }
        void Update()
        {
            if (muzzleFlash != null) muzzleFlash.intensity = Mathf.MoveTowards(muzzleFlash.intensity, 0, Time.deltaTime * 95);
            if (owner == null || owner.Damage.IsDead) return;
            if (turret != null && aimDirection.sqrMagnitude > .1f)
            {
                turret.rotation = Quaternion.Slerp(turret.rotation, Quaternion.LookRotation(aimDirection, Vector3.up), Time.deltaTime * 25);
                recoil = Mathf.SmoothDamp(recoil, 0, ref recoilVelocity, .075f, 20, Time.deltaTime);
                turret.localPosition = turretRestPosition - Vector3.forward * recoil;
            }
        }
        public void AimAt(Vector3 direction)
        {
            if (!GeneratedWorld.Active) direction.y = 0;
            if (direction.sqrMagnitude > .001f) aimDirection = direction.normalized;
            UpdateLobMarker();
        }
        public void EquipField(WeaponDefinition weapon, int ammo)
        {
            if (!owner || !owner.IsPlayer || !weapon || WeaponRules.GarageWeapon(weapon.id)) return;
            FieldWeapon = weapon; FieldAmmo = Mathf.Max(1, ammo); fieldAt = 0;
            GameManager.Instance?.Notify("FIELD WEAPON • " + weapon.displayName + " / " + FieldAmmo + " rounds");
        }
        public void ConfigureEnemyPrimary(string id)
        {
            if (owner != null && !owner.IsPlayer)
                GarageWeapon = WeaponRules.Find(id) ?? WeaponRules.Find("riveter");
        }
        public void FirePrimary(Vector3 direction)
        {
            if (!CanFire() || Time.time < garageAt || !GarageWeapon) return;
            garageAt = Time.time + 1f / Mathf.Max(.1f, GarageWeapon.fireRate);
            Fire(GarageWeapon, direction);
        }
        public void FireSecondary(Vector3 direction)
        {
            if (!CanFire()) return;
            if (!owner.IsPlayer) { FireEnemyRocket(direction); return; }
            if (!FieldWeapon || FieldAmmo <= 0 || Time.time < fieldAt) return;
            var weapon = FieldWeapon;
            fieldAt = Time.time + 1f / Mathf.Max(.1f, weapon.fireRate);
            Fire(weapon, direction);
            if (--FieldAmmo == 0) { FieldWeapon = null; GameManager.Instance?.Notify("FIELD WEAPON EMPTY • FIND ANOTHER DROP"); }
        }
        void FireEnemyRocket(Vector3 direction)
        {
            if (Time.time < enemyRocketAt) return;
            enemyRocketAt = Time.time + 3.3f;
            AimAt(direction);
            Vector3 muzzle = Muzzle(aimDirection);
            ProjectileSystem.Fire(muzzle, aimDirection, 49, 62, 6, owner.gameObject, new Color(1, .35f, .08f), ExplosionKind.Rocket, 2.7f);
            ExplosionSystem.Burst(muzzle, new Color(1, .5f, .1f), 8, 2);
            Flash(new Color(1, .3f, .06f), 13, .2f);
            AudioManager.Instance?.PlayShot(muzzle, WeaponRules.Find("invoice"));
        }
        void Fire(WeaponDefinition weapon, Vector3 direction)
        {
            AimAt(direction);
            Vector3 muzzle = Muzzle(aimDirection);
            float multiplier = !owner.IsPlayer ? (weapon.id == "boomstick" || weapon.id == "sweeper" ? .3f : weapon.id == "minigun" ? .48f : .65f) : weapon.id == "riveter" ? WorldExploration.PlayerWeaponMultiplier(0) :
                weapon.id == "invoice" ? WorldExploration.PlayerWeaponMultiplier(1) :
                weapon.id == "sweeper" ? WorldExploration.PlayerWeaponMultiplier(2) : 1;
            float damage = weapon.damage * multiplier;
            if (weapon.id == "mines")
                FieldOrdnance.PlaceMine(owner, weapon, damage);
            else if (weapon.id == "grenade" || weapon.id == "mortar" || weapon.id == "pothole")
                FieldOrdnance.LaunchShell(owner, muzzle, aimDirection, weapon, damage, 0,
                    owner.IsPlayer ? AcquireLobTarget(aimDirection, weapon) : null);
            else if (weapon.id == "sweeper" || weapon.id == "boomstick")
            {
                int pellets = weapon.id == "boomstick" ? 12 : 8;
                for (int i = 0; i < pellets; i++)
                {
                    Vector3 spread = Quaternion.AngleAxis((i - (pellets - 1) * .5f) * (weapon.id == "boomstick" ? 3.2f : 3.7f) + Random.Range(-.85f, .85f), Vector3.up) * aimDirection;
                    ProjectileSystem.Fire(muzzle, spread, weapon.speed, damage, 0, owner.gameObject, weapon.projectileColor, ExplosionKind.Ammunition, weapon.id == "boomstick" ? .33f : .42f);
                }
            }
            else if (weapon.id == "shredder")
            {
                for (int i = -1; i <= 1; i++)
                {
                    Vector3 spread = Quaternion.AngleAxis(i * 9 + Random.Range(-1f, 1f), Vector3.up) * aimDirection;
                    ProjectileSystem.Fire(muzzle, spread, weapon.speed, damage, 0, owner.gameObject, weapon.projectileColor, ExplosionKind.Ammunition, 1.15f);
                }
            }
            else if (weapon.id == "cluster")
            {
                for (int i = -1; i <= 1; i++)
                {
                    Vector3 spread = Quaternion.AngleAxis(i * 8, Vector3.up) * aimDirection;
                    ProjectileSystem.Fire(muzzle, spread, weapon.speed, damage, weapon.blastRadius, owner.gameObject, weapon.projectileColor, ExplosionKind.Rocket, 2.6f);
                }
            }
            else
            {
                float spread = weapon.id == "minigun" ? Random.Range(-4f, 4f) : weapon.id == "sniper" ? 0 : Random.Range(-.7f, .7f);
                Vector3 shot = Quaternion.AngleAxis(spread, Vector3.up) * aimDirection;
                float radius = weapon.id == "invoice" && owner.IsPlayer ? weapon.blastRadius * WorldExploration.PlayerRocketRadiusMultiplier() : weapon.blastRadius;
                ProjectileSystem.Fire(muzzle, shot, weapon.speed, damage, radius, owner.gameObject, weapon.projectileColor,
                    weapon.blastRadius > 0 ? ExplosionKind.Rocket : ExplosionKind.Ammunition,
                    weapon.id == "sniper" ? 3 : weapon.id == "minigun" ? .65f : 1.5f);
            }
            ExplosionSystem.Burst(muzzle, weapon.projectileColor, weapon.id == "mines" ? 2 : 4, 1);
            Flash(weapon.projectileColor, weapon.id == "mortar" ? 14 : 7, weapon.id == "minigun" ? .035f : .09f);
            AudioManager.Instance?.PlayShot(muzzle, weapon);
            if (owner.IsPlayer && weapon.blastRadius > 0) CameraController.Instance?.Shake(.1f);
        }
        Vector3 Muzzle(Vector3 direction)
        {
            if (muzzleTransform != null) return new Vector3(muzzleTransform.position.x, transform.position.y + .85f, muzzleTransform.position.z);
            return transform.position + Vector3.up * .85f + direction * 2.4f;
        }
        static bool Lobbed(WeaponDefinition weapon) => weapon && (weapon.id == "grenade" || weapon.id == "mortar" || weapon.id == "pothole");
        VehicleController AcquireLobTarget(Vector3 direction, WeaponDefinition weapon)
        {
            if (!owner || !owner.IsPlayer || !Lobbed(weapon)) return null;
            Vector3 flatAim = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (flatAim.sqrMagnitude < .01f) flatAim = Vector3.ProjectOnPlane(owner.transform.forward, Vector3.up);
            flatAim.Normalize();
            float range = weapon.id == "mortar" ? 125 : weapon.id == "grenade" ? 72 : 58;
            float cone = weapon.id == "mortar" ? 42 : 34;
            if (ValidLobTarget(lobTarget, flatAim, range * 1.15f, cone + 14) && Time.time <= lobTargetUntil) return lobTarget;
            VehicleController best = null; float bestScore = float.MaxValue;
            foreach (var candidate in VehicleController.Active)
            {
                if (!ValidLobTarget(candidate, flatAim, range, cone)) continue;
                Vector3 delta = Vector3.ProjectOnPlane(candidate.transform.position - owner.transform.position, Vector3.up);
                float angle = Vector3.Angle(flatAim, delta);
                float score = angle / cone * 1.45f + delta.magnitude / range;
                if (candidate == lobTarget) score -= .24f;
                if (score < bestScore) { bestScore = score; best = candidate; }
            }
            lobTarget = best; lobTargetUntil = Time.time + .72f;
            return best;
        }
        bool ValidLobTarget(VehicleController candidate, Vector3 flatAim, float range, float cone)
        {
            if (!candidate || candidate == owner || candidate.Damage == null || candidate.Damage.IsDead) return false;
            var ai = candidate.GetComponent<EnemyAI>();
            if (candidate.IsPlayer || ai == null || ai.IsFriendly) return false;
            Vector3 delta = Vector3.ProjectOnPlane(candidate.transform.position - owner.transform.position, Vector3.up);
            return delta.sqrMagnitude > 4 && delta.sqrMagnitude <= range * range && Vector3.Angle(flatAim, delta) <= cone;
        }
        void UpdateLobMarker()
        {
            if (!owner || !owner.IsPlayer || !Lobbed(FieldWeapon)) { SetLobMarker(false); lobTarget = null; return; }
            var target = AcquireLobTarget(aimDirection, FieldWeapon);
            if (!target) { SetLobMarker(false); return; }
            Vector3 point = FieldOrdnance.PredictLandingPoint(target, Muzzle(aimDirection), FieldWeapon);
            EnsureLobMarker();
            lobMarker.enabled = true;
            float ring = Mathf.Clamp(FieldWeapon.blastRadius * .42f, 1.8f, 4.2f);
            for (int i = 0; i < lobMarker.positionCount; i++)
            {
                float a = i / (float)lobMarker.positionCount * Mathf.PI * 2;
                lobMarker.SetPosition(i, point + new Vector3(Mathf.Cos(a) * ring, .22f, Mathf.Sin(a) * ring));
            }
            Color color = FieldWeapon.projectileColor;
            lobMarker.startColor = new Color(color.r, color.g, color.b, .92f);
            lobMarker.endColor = lobMarker.startColor;
        }
        void EnsureLobMarker()
        {
            if (lobMarker) return;
            var marker = new GameObject("Ballistic assisted landing ring"); marker.transform.SetParent(transform, false);
            lobMarker = marker.AddComponent<LineRenderer>(); lobMarker.useWorldSpace = true; lobMarker.loop = true;
            lobMarker.positionCount = 32; lobMarker.widthMultiplier = .13f; lobMarker.numCornerVertices = 2;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            lobMarkerMaterial = new Material(shader) { name = "Ballistic lock ring" };
            lobMarkerMaterial.color = Color.white; lobMarker.sharedMaterial = lobMarkerMaterial;
            lobMarker.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lobMarker.receiveShadows = false;
        }
        void SetLobMarker(bool enabled) { if (lobMarker) lobMarker.enabled = enabled; }
        void Flash(Color color, float intensity, float kick)
        {
            recoil = Mathf.Min(.28f, recoil + kick);
            if (muzzleFlash == null) return;
            muzzleFlash.color = color; muzzleFlash.intensity = intensity;
        }
        bool CanFire() => owner != null && !owner.Damage.IsDead && GameManager.Instance != null && GameManager.Instance.IsPlaying;
        void OnDestroy() { if (lobMarkerMaterial) Destroy(lobMarkerMaterial); }
    }
}
