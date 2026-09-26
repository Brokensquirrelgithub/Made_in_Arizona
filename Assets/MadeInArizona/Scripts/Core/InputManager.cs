using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MadeInArizona
{
    [DefaultExecutionOrder(-100)]
    public sealed class InputManager : MonoBehaviour
    {
        public static InputManager Instance { get; private set; }
        public InputActionAsset Actions { get; private set; }
        public Vector2 Move { get; private set; }
        public Vector2 Aim { get; private set; } = Vector2.up;
        public float AimElevation {get;private set;}
        public bool Primary { get; private set; }
        public bool Secondary { get; private set; }
        /// <summary>Held to drift. The action keeps its old "Handbrake" name so saved rebinds still apply.</summary>
        public bool Drift { get; private set; }
        public bool Boost { get; private set; }
        public bool Repair { get; private set; }
        public bool Interact { get; private set; }
        public bool SwapPressed { get; private set; }
        public bool PausePressed { get; private set; }
        public bool UsingGamepad { get; private set; }
        public bool Rebinding => rebind != null;
        InputActionMap map;
        InputAction move, aim, pointer, primary, secondary, brake, boost, repair, interact, swap, pause;
        InputActionRebindingExtensions.RebindingOperation rebind;
        bool gameplayEnabled = true;

        void Awake()
        {
            Instance = this;
            Actions = ScriptableObject.CreateInstance<InputActionAsset>();
            Actions.name = "Made in Arizona Controls";
            map = new InputActionMap("Driving");
            move = map.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s").With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            move.AddBinding("<Gamepad>/leftStick").WithProcessor("stickDeadzone(min=0.14,max=0.95)");
            aim = map.AddAction("Aim", InputActionType.Value, "<Gamepad>/rightStick", expectedControlLayout: "Vector2");
            pointer = map.AddAction("Pointer", InputActionType.Value, "<Mouse>/position", expectedControlLayout: "Vector2");
            primary = Button("Primary", "<Mouse>/leftButton", "<Gamepad>/rightTrigger");
            secondary = Button("Secondary", "<Mouse>/rightButton", "<Gamepad>/leftTrigger");
            brake = Button("Handbrake", "<Keyboard>/space", "<Gamepad>/buttonEast");
            boost = Button("Boost", "<Keyboard>/leftShift", "<Gamepad>/rightShoulder");
            repair = Button("Repair", "<Keyboard>/r", "<Gamepad>/leftShoulder");
            interact = Button("Interact", "<Keyboard>/e", "<Gamepad>/buttonSouth");
            swap = Button("Swap", "<Keyboard>/f", "<Gamepad>/buttonNorth");
            pause = Button("Pause", "<Keyboard>/escape", "<Gamepad>/start");
            Actions.AddActionMap(map);
            map.Enable();
        }

        InputAction Button(string actionName, string keyboard, string controller)
        {
            var a = map.AddAction(actionName, InputActionType.Button, keyboard);
            a.AddBinding(controller);
            return a;
        }

        void Start()
        {
            var settings = GameManager.Instance != null ? GameManager.Instance.Save?.settings : null;
            if (settings != null && !string.IsNullOrEmpty(settings.bindingOverrides))
            {
                try { Actions.LoadBindingOverridesFromJson(settings.bindingOverrides); }
                catch (Exception e) { Debug.LogWarning("Discarding invalid input overrides: " + e.Message); }
            }
        }

        public void SetEnabled(bool enabled)
        {
            gameplayEnabled = enabled;
            if (!enabled) ClearGameplay();
        }

        void ClearGameplay()
        {
            Move = Vector2.zero;
            Primary = Secondary = Drift = Boost = Repair = Interact = SwapPressed = false;
        }

        void Update()
        {
            PausePressed = !Rebinding && pause.WasPressedThisFrame();
            Vector2 stickAim = aim.ReadValue<Vector2>();
            var pad = Gamepad.current;
            if (pad != null && (pad.leftStick.ReadValue().sqrMagnitude > .04f || stickAim.sqrMagnitude > .04f || pad.buttonSouth.wasPressedThisFrame || pad.leftTrigger.ReadValue() > .15f || pad.rightTrigger.ReadValue() > .15f)) UsingGamepad = true;
            if ((Mouse.current != null && (Mouse.current.delta.ReadValue().sqrMagnitude > 2f || Mouse.current.leftButton.wasPressedThisFrame)) || (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)) UsingGamepad = false;
            if (pad != null && (pad.dpad.ReadValue().sqrMagnitude > .01f || pad.leftShoulder.wasPressedThisFrame || pad.rightShoulder.wasPressedThisFrame || pad.buttonNorth.wasPressedThisFrame)) UsingGamepad = true;
            if (Rebinding || !gameplayEnabled) { ClearGameplay(); return; }
            Move = Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1);
            var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
            AimElevation=0;
            if (UsingGamepad)
            {
                if (stickAim.sqrMagnitude > .08f) Aim = stickAim.normalized;
            }
            else if (Camera.main != null && player != null && Mouse.current != null)
            {
                var ray = Camera.main.ScreenPointToRay(pointer.ReadValue<Vector2>());
                var plane = new Plane(Vector3.up, player.transform.position + Vector3.up * .5f);
                if(GeneratedWorld.Active && Physics.Raycast(ray,out var terrainHit,600,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore) && terrainHit.collider.GetComponentInParent<VehicleController>()!=player)
                {
                    var targetVehicle=terrainHit.collider.GetComponentInParent<VehicleController>();
                    // Aim at hull height, not the dirt: a cursor beside a car on a slope used to fire into the ground.
                    // A hostile close to the cursor lends its height so downhill and uphill shots still connect.
                    if(!targetVehicle)targetVehicle=HostileNear(player,terrainHit.point,5.5f);
                    Vector3 target=targetVehicle?targetVehicle.transform.position+Vector3.up*.85f:terrainHit.point+Vector3.up*.85f;
                    Vector3 delta=target-player.transform.position-Vector3.up*.85f;
                    float horizontal=new Vector2(delta.x,delta.z).magnitude;
                    if(horizontal>.3f){Aim=new Vector2(delta.x,delta.z)/horizontal;AimElevation=delta.y/horizontal;}
                }
                else if (plane.Raycast(ray, out float distance))
                {
                    Vector3 delta = ray.GetPoint(distance) - player.transform.position;
                    if (delta.sqrMagnitude > .3f) Aim = new Vector2(delta.x, delta.z).normalized;
                }
            }
            if (UsingGamepad && player != null && GameManager.Instance.Save.settings.aimAssist > 0)
            {
                float best = GameManager.Instance.Save.settings.aimAssist == 2 ? .88f : .96f;
                Vector2 corrected = Aim;
                foreach (var candidate in VehicleController.Active)
                {
                    if (candidate == player || candidate == null || candidate.Damage.IsDead) continue;
                    var ai = candidate.GetComponent<EnemyAI>();
                    if (ai != null && ai.IsFriendly) continue;
                    Vector3 delta = candidate.transform.position - player.transform.position;
                    if (delta.sqrMagnitude > 1800) continue;
                    Vector2 direction = new Vector2(delta.x, delta.z).normalized;
                    float dot = Vector2.Dot(Aim, direction);
                    if (dot > best) { best = dot; corrected = direction; }
                }
                Aim = Vector2.Lerp(Aim, corrected, .35f).normalized;
            }
            // A stick only chooses a heading, so the turret pitch must be chosen for the player. Without it every
            // gamepad shot left flat at muzzle height and sailed over enemies below the player.
            if (UsingGamepad && player != null && GeneratedWorld.Active) AimElevation = GamepadElevation(player, Aim);
            Primary = primary.IsPressed();
            Secondary = secondary.IsPressed();
            Drift = brake.IsPressed(); Boost = boost.IsPressed(); Repair = repair.IsPressed();
            Interact = interact.WasPressedThisFrame();
            SwapPressed = swap.WasPressedThisFrame();
        }

        static VehicleController HostileNear(VehicleController player, Vector3 point, float radius)
        {
            VehicleController best = null; float bestSq = radius * radius;
            foreach (var candidate in VehicleController.Active)
            {
                if (!Hostile(player, candidate)) continue;
                Vector3 d = candidate.transform.position - point; d.y = 0;
                if (d.sqrMagnitude < bestSq) { bestSq = d.sqrMagnitude; best = candidate; }
            }
            return best;
        }
        static bool Hostile(VehicleController player, VehicleController candidate)
        {
            if (candidate == null || candidate == player || candidate.IsPlayer || candidate.Damage == null || candidate.Damage.IsDead) return false;
            var ai = candidate.GetComponent<EnemyAI>();
            return ai == null || !ai.IsFriendly;
        }
        /// <summary>
        /// Pitch for a stick-aimed shot: toward the hull of the hostile best lined up with the heading, otherwise
        /// level with the ground ahead so shots follow hills instead of burying into them or flying over dips.
        /// </summary>
        static float GamepadElevation(VehicleController player, Vector2 aim)
        {
            Vector3 muzzle = player.transform.position + Vector3.up * .85f;
            float best = .93f; VehicleController target = null;
            foreach (var candidate in VehicleController.Active)
            {
                if (!Hostile(player, candidate)) continue;
                Vector3 delta = candidate.transform.position - player.transform.position;
                Vector2 flat = new Vector2(delta.x, delta.z);
                if (flat.sqrMagnitude < 1 || flat.sqrMagnitude > 5600) continue;
                float dot = Vector2.Dot(aim, flat.normalized);
                if (dot > best) { best = dot; target = candidate; }
            }
            if (target != null)
            {
                Vector3 delta = target.transform.position + Vector3.up * .85f - muzzle;
                float horizontal = new Vector2(delta.x, delta.z).magnitude;
                return Mathf.Clamp(delta.y / Mathf.Max(1, horizontal), -.8f, .8f);
            }
            const float probe = 18;
            Vector3 ahead = player.transform.position + new Vector3(aim.x, 0, aim.y) * probe;
            return Mathf.Clamp((GeneratedWorld.HeightAt(ahead) + .85f - muzzle.y) / probe, -.45f, .45f);
        }

        public string BindingLabel(string actionName, int bindingIndex = 0)
        {
            var a = map.FindAction(actionName);
            if (a == null || bindingIndex >= a.bindings.Count) return "—";
            return a.GetBindingDisplayString(bindingIndex);
        }

        public void StartRebind(string actionName, int bindingIndex, Action onComplete = null)
        {
            CancelRebind();
            var action = map.FindAction(actionName);
            if (action != null && bindingIndex == -1) {
                // Select the active device's binding; composite movement is edited one direction at a time.
                bindingIndex = UsingGamepad ? action.bindings.Count - 1 : action.bindings[0].isComposite ? 1 : 0;
            }
            if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count || action.bindings[bindingIndex].isComposite) return;
            action.Disable();
            rebind = action.PerformInteractiveRebinding(bindingIndex).WithCancelingThrough("<Keyboard>/escape")
                .WithControlsExcluding("<Mouse>/position").WithControlsExcluding("<Mouse>/delta")
                .OnCancel(op => { op.Dispose(); rebind = null; action.Enable(); onComplete?.Invoke(); })
                .OnComplete(op => { op.Dispose(); rebind = null; action.Enable(); PersistBindings(); onComplete?.Invoke(); });
            rebind.Start();
        }

        public void CancelRebind() { if (rebind != null) rebind.Cancel(); }
        public void ResetBindings() { CancelRebind(); Actions.RemoveAllBindingOverrides(); PersistBindings(); }
        void PersistBindings()
        {
            if (GameManager.Instance == null || GameManager.Instance.Save == null) return;
            GameManager.Instance.Save.settings.bindingOverrides = Actions.SaveBindingOverridesAsJson();
            SaveSystem.Save(GameManager.Instance.Save);
        }
        void OnDestroy()
        {
            CancelRebind();
            if (Actions != null) { Actions.Disable(); Destroy(Actions); }
            if (Instance == this) Instance = null;
        }
    }
}
