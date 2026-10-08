using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MadeInArizona
{
    /// <summary>Resolution-independent immediate UI, with mouse, keyboard and explicit controller navigation.</summary>
    public sealed partial class GameUI : MonoBehaviour
    {
        static readonly Color Ink = new Color(.055f, .081f, .09f, .97f);
        static readonly Color Panel = new Color(.09f, .125f, .135f, .95f);
        static readonly Color Cream = new Color(.94f, .90f, .79f);
        static readonly Color Muted = new Color(.57f, .64f, .63f);
        static readonly Color Orange = new Color(1f, .36f, .17f);
        static readonly Color Lime = new Color(.80f, .90f, .42f);
        static readonly Color Blue = new Color(.30f, .83f, .87f);
        readonly Dictionary<string, GUIStyle> styles = new Dictionary<string, GUIStyle>();
        readonly string[] stations = { "DISPATCH", "MOTOR POOL", "PARTS & TUNING", "WEAPONS", "SUZUKI" };
        readonly string[] presets = { "SHADE TREE", "DESERT DAILY", "HIGH OCTANE", "ARIZONA SUMMER" };
        readonly string[] actions = { "Move up", "Move down", "Move left", "Move right", "Primary", "Secondary", "Swap", "Handbrake", "Boost", "Interact", "Reverse", "Pause" };
        int[] garageWeaponIndices;
        /// <summary>Catalog indices of every scrap weapon, core roles first and then the oddballs.</summary>
        int[] garageWeapons
        {
            get
            {
                if (garageWeaponIndices != null) return garageWeaponIndices;
                var list = new List<int>();
                foreach (string id in WeaponRules.GarageIds)
                {
                    int index = Array.FindIndex(ContentCatalog.Weapons, w => w && w.id == id);
                    if (index >= 0) list.Add(index);
                }
                return garageWeaponIndices = list.ToArray();
            }
        }
        int station, selectedPart, selectedWeapon, settingsPage, menuFocus;
        bool settings;
        float width, height, smoothedFps = 60;
        Vector2 listScroll, detailScroll;
        GameManager game => GameManager.Instance;

        void Start()
        {
            game.StateChanged += ResetFocus;
        }
        void OnDestroy() { if (game != null) game.StateChanged -= ResetFocus; }
        void ResetFocus()
        {
            worldSeed=null; worldMap=false; devMenu=false; settings=false; updatesMenu=false; coopPage=false; menuFocus=0;
            if (game != null && game.Save != null)
            {
                selectedWeapon=Array.FindIndex(garageWeapons,i=>ContentCatalog.Weapons[i].id==game.Save.selectedWeapon);
                if (selectedWeapon < 0) selectedWeapon=0;
            }
            GUI.FocusControl(null);
        }

        void Update()
        {
            if (introActive) { TickOpening(); return; }
            TickDevSave();UpdatesInput();if(updatesMenu)return;WorldMenuInput();
            if(game.State==GameState.MainMenu||game.State==GameState.Generating||worldMap)return;
            smoothedFps = Mathf.Lerp(smoothedFps, 1f / Mathf.Max(.001f, Time.unscaledDeltaTime), .05f);
            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame) { if (game.IsPlaying) { game.Pause(); settings = true; } else settings = !settings; }
            if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame && game.State == GameState.Garage) { station = (station + 1) % stations.Length; listScroll = Vector2.zero; }
            if (Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame && game.State == GameState.Garage && !settings) ConfirmSelection();
            if (Keyboard.current != null && Keyboard.current.backquoteKey.wasPressedThisFrame) DialogueSystem.Instance.Skip();
            if(Keyboard.current!=null && Keyboard.current.f2Key.wasPressedThisFrame && game.State==GameState.Garage && !settings)game.StartCombatTrial();
            if(devMenu)return;
            var pad = Gamepad.current;
            if (pad == null) return;
            if (settings) {
                if (pad.buttonEast.wasPressedThisFrame || pad.buttonNorth.wasPressedThisFrame) { settings = false; game.ApplySettings(); }
                if (pad.leftShoulder.wasPressedThisFrame) { settingsPage = (settingsPage + SettingsTabs.Length - 1) % SettingsTabs.Length; menuFocus = 0; }
                if (pad.rightShoulder.wasPressedThisFrame) { settingsPage = (settingsPage + 1) % SettingsTabs.Length; menuFocus = 0; }
                int rowCount = SettingsRows(settingsPage);
                if (pad.dpad.up.wasPressedThisFrame) menuFocus = (menuFocus + rowCount - 1) % rowCount;
                if (pad.dpad.down.wasPressedThisFrame) menuFocus = (menuFocus + 1) % rowCount;
                int direction = pad.dpad.right.wasPressedThisFrame ? 1 : pad.dpad.left.wasPressedThisFrame ? -1 : 0;
                if (direction != 0 || pad.buttonSouth.wasPressedThisFrame) AdjustSetting(direction == 0 ? 1 : direction);
                return;
            }
            if (pad.buttonNorth.wasPressedThisFrame && (game.State == GameState.Garage || game.State == GameState.Paused)) { settings = true; menuFocus = 0; return; }
            if (game.State == GameState.Garage) {
                if(pad.buttonWest.wasPressedThisFrame){game.StartCombatTrial();return;}
                if (pad.leftShoulder.wasPressedThisFrame) { station = (station + stations.Length - 1) % stations.Length; listScroll = Vector2.zero; }
                if (pad.rightShoulder.wasPressedThisFrame) { station = (station + 1) % stations.Length; listScroll = Vector2.zero; }
                int change = pad.dpad.down.wasPressedThisFrame || pad.dpad.right.wasPressedThisFrame ? 1 : pad.dpad.up.wasPressedThisFrame || pad.dpad.left.wasPressedThisFrame ? -1 : 0;
                if (change != 0) {
                    if (station == 0) game.SelectedMission = Mathf.Clamp(game.SelectedMission + change, 0, game.Save.unlockedMission);
                    if (station == 1) { int v = Mathf.Clamp(game.Save.selectedVehicle + change, 0, ContentCatalog.Vehicles.Length - 1); if (GarageManager.SelectVehicle(v, game.Save)) game.RefreshGarageVehicle(); }
                    if (station == 2) { selectedPart = Mathf.Clamp(selectedPart + change, 0, ContentCatalog.Parts.Length - 1); listScroll.y = Mathf.Max(0, (selectedPart - 3) * 81); }
                    if (station == 3) { selectedWeapon = Mathf.Clamp(selectedWeapon + change, 0, garageWeapons.Length - 1); listScroll.y = Mathf.Max(0, (selectedWeapon - 2) * 91); }
                    if (station == 4) { game.Save.dogCosmetic = Mathf.Clamp(game.Save.dogCosmetic + change, 0, 3); game.ReturnToGarage(); station = 4; }
                }
                if (pad.buttonSouth.wasPressedThisFrame) ConfirmSelection();
            }
            else if (game.State == GameState.Paused) { if (pad.buttonSouth.wasPressedThisFrame) game.Resume(); if (pad.buttonEast.wasPressedThisFrame) { if (CoopSession.IsRemoteClient) LeaveCoop(); else game.ReturnToGarage(); } }
            else if (game.State == GameState.Won || game.State == GameState.Lost) { if (CoopSession.IsRemoteClient) { if (pad.buttonEast.wasPressedThisFrame) LeaveCoop(); } else { if (pad.buttonSouth.wasPressedThisFrame) game.ReturnToGarage(); if (pad.buttonWest.wasPressedThisFrame) game.RetryMission(); } }
        }

        void ConfirmSelection()
        {
            if (station == 0) game.StartMission(game.SelectedMission);
            if (station == 2) BuyOrInstall(ContentCatalog.Parts[selectedPart]);
            if (station == 3) SelectGarageWeapon(ContentCatalog.Weapons[garageWeapons[selectedWeapon]]);
        }

        GUIStyle Style(int size, Color color, bool bold = false, TextAnchor anchor = TextAnchor.UpperLeft)
        {
            string key = size + "/" + color.GetHashCode() + "/" + bold + "/" + anchor;
            if (styles.TryGetValue(key, out var style)) return style;
            style = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = bold ? FontStyle.Bold : FontStyle.Normal, alignment = anchor, wordWrap = true, richText = false, padding = new RectOffset(0, 0, 0, 0) };
            style.normal.textColor = QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
            styles[key] = style; return style;
        }

        void Rect(float x, float y, float w, float h, Color color)
        {
            var previous = GUI.color; GUI.color = QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color; GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture); GUI.color = previous;
        }
        void Text(float x, float y, float w, float h, string text, int size = 18, Color? color = null, bool bold = false, TextAnchor anchor = TextAnchor.UpperLeft)
        {
            var rect = new Rect(x, y, w, h);
            var shadow = Style(size, new Color(0, 0, 0, .42f), bold, anchor);
            GUI.Label(new Rect(x + 1, y + 1, w, h), text, shadow);
            GUI.Label(rect, text, Style(size, color ?? Cream, bold, anchor));
        }
        bool Button(float x, float y, float w, float h, string text, bool accent = false, bool enabled = true, int size = 16)
        {
            var rect = new Rect(x, y, w, h);
            bool hover = rect.Contains(Event.current.mousePosition);
            Rect(x, y, w, h, enabled ? accent ? Orange : hover ? new Color(.22f, .29f, .3f) : new Color(.15f, .20f, .21f) : new Color(.11f, .14f, .15f));
            var oldEnabled = GUI.enabled; GUI.enabled = enabled;
            bool result = GUI.Button(rect, text, Style(size, enabled ? accent ? Ink : Cream : Muted, true, TextAnchor.MiddleCenter));
            GUI.enabled = oldEnabled;
            if (result && AudioManager.Instance) AudioManager.Instance.PlayUI();
            return result;
        }
        void Rule(float x, float y, float w) { Rect(x, y, w, 1, new Color(.32f, .39f, .38f, .5f)); }
        void Tag(float x, float y, string text, Color color) { Text(x, y, 650, 22, text, 12, color, true); }

        void OnGUI()
        {
            if (game == null || game.Save == null) return;
            float scale = Mathf.Min(Screen.width / 1440f, Screen.height / 900f) * Mathf.Clamp(game.Save.settings.uiScale, .85f, 1.2f);
            scale = Mathf.Max(.45f, scale);
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * scale);
            width = Screen.width / scale; height = Screen.height / scale;
            if (introActive) { DrawOpening(); GUI.matrix = Matrix4x4.identity; return; }
            if (CoopSession.Instance && (CoopSession.Instance.EndChoicePending || CoopSession.Instance.EndingSession))
            { DrawCoopEndPrompt(); GUI.matrix = Matrix4x4.identity; return; }
            if(game.State==GameState.MainMenu)
            {
                if(updatesMenu)DrawUpdates();
                else if (coopPage) { Rect(0, 0, width, height, Ink); DrawCoopPanel(); }
                else { DrawMainMenu(); if (width >= 1250) DrawCoopPanel(); }
                GUI.matrix=Matrix4x4.identity;return;
            }
            if(game.State==GameState.Generating){DrawGeneration();GUI.matrix=Matrix4x4.identity;return;}
            if(worldMap&&GeneratedWorld.Active){DrawWorldMap();GUI.matrix=Matrix4x4.identity;return;}
            if(updatesMenu){DrawUpdates();GUI.matrix=Matrix4x4.identity;return;}
            // Settings covers the screen: nothing underneath is drawn, so its buttons cannot take the click (IMGUI gives
            // it to the first control drawn). The dynamic zoom toggle was landing on the pause menu's map regenerate.
            if (settings) DrawSettings();
            else
            {
                if (game.State == GameState.Garage) DrawGarage();
                else if(!devMenu) DrawHUD();
                if (game.State == GameState.Paused && !devMenu) DrawPause();
                if(devMenu) DrawDevMenu();
                if (game.State == GameState.Won || game.State == GameState.Lost) DrawDebrief();
            }
            if (game.Dying) DrawDeathOverlay();
            DrawCoopBadge();
            if (!settings && game.NotificationUntil > Time.unscaledTime) {
                float w = Mathf.Min(650, width - 80); Rect((width - w) / 2, 106, w, 48, Ink);
                Rect((width - w) / 2, 106, 4, 48, Lime);
                Text((width - w) / 2 + 20, 119, w - 40, 32, game.Notification, 17, Cream, true);
            }
            if (game.Save.settings.subtitles && DialogueSystem.Instance.Active && game.State == GameState.Playing && !settings) DrawDialogue();
            GUI.matrix = Matrix4x4.identity;
        }

        void DrawGarage()
        {
            Rect(0, 0, width, 92, Ink);
            Rect(30, 22, 4, 44, Orange);
            Text(48, 17, 420, 35, "MADE IN ARIZONA", 29, Cream, true);
            Tag(49, 56, "117° AUTO CARE   /   REPAIRS BY SUPERIOR FIREPOWER", Orange);
            // Money and scrap are the two spendable currencies: same size, each in its own colour.
            Text(width - 760, 28, 230, 30, "$ " + game.Save.money.ToString("N0"), 25, Lime, true, TextAnchor.MiddleRight);
            Text(width - 510, 28, 270, 30, game.Save.salvage.ToString("N0") + " SCRAP", 25, Orange, true, TextAnchor.MiddleRight);
            Text(width - 225, 31, 190, 30, "REP " + game.Save.reputation, 16, Cream, true, TextAnchor.MiddleRight);
            for (int i = 0; i < stations.Length; i++) {
                float x = 32 + i * 175;
                if (Button(x, 110, 165, 39, stations[i], station == i)) { station = i; listScroll = Vector2.zero; }
            }
            float top = 169, panelHeight = height - 245;
            if (station == 0) DrawDispatch(top, panelHeight);
            if (station == 1) DrawVehicles(top, panelHeight);
            if (station == 2) DrawParts(top, panelHeight);
            if (station == 3) DrawWeapons(top, panelHeight);
            if (station == 4) DrawDog(top, panelHeight);
            Rect(0, height - 62, width, 62, Ink);
            Text(32, height - 42, 850, 30, InputManager.Instance.UsingGamepad ? "LB / RB  STATION     D-PAD  SELECT     A  CONFIRM     RIGHT STICK  ROTATE VIEW     Y  SETTINGS" : "TAB  CHANGE STATION     ENTER  CONFIRM     RMB DRAG / Q E  ROTATE VIEW     F1  SETTINGS", 12, Muted, true);
            bool freshBuild = GameUpdater.Instance && GameUpdater.Instance.UpdateAvailable;
            if (Button(width - 545, height - 48, 190, 34, freshBuild ? "NEW BUILD READY" : "UPDATES", freshBuild)) OpenUpdates();
            if (Button(width - 345, height - 48, 175, 34, "SETTINGS  /  F1")) { settings = true; menuFocus = 0; }
            if (Button(width - 155, height - 48, 120, 34, "QUIT")) Application.Quit();
        }

        void DrawDispatch(float top, float panelHeight)
        {
            float left = 32, w = 490;
            Rect(left, top, w, panelHeight, Ink);
            var mission = ContentCatalog.Missions[game.SelectedMission];
            Tag(left + 25, top + 22, "WORK ORDER  " + (game.SelectedMission + 1).ToString("00") + "   /   " + mission.region.ToUpperInvariant(), Orange);
            // Long titles shrink to fit their box instead of running under the briefing.
            string title = mission.title.ToUpperInvariant(); int titleSize = 36;
            while (titleSize > 20 && Style(titleSize, Cream, true).CalcHeight(new GUIContent(title), w - 50) > 100) titleSize -= 2;
            Text(left + 25, top + 57, w - 50, 100, title, titleSize, Cream, true);
            Rule(left + 25, top + 161, w - 50);
            Text(left + 25, top + 185, w - 50, 145, mission.briefing, 21);
            Tag(left + 25, top + 344, "THE FINE PRINT", Muted);
            Text(left + 25, top + 371, w - 50, 90, mission.optionalObjective, 16, Muted);
            Text(left + 25, top + panelHeight - 132, 225, 30, "$" + mission.reward.ToString("N0") + "  +  SALVAGE", 21, Lime, true);
            if (Button(left + 25, top + panelHeight - 83, w - 50, 58, "CLOCK IN. CAUSE A PROBLEM.  →", true)) game.StartMission(game.SelectedMission);
            float right = width - 360;
            Rect(right, top, 328, 88, Ink);
            Tag(right + 20, top + 16, "FLEET STATUS", Lime);
            Text(right + 20, top + 43, 295, 30, ContentCatalog.Vehicles[game.Save.selectedVehicle].displayName, 22, Cream, true);
            var stats = GarageManager.StatsFor(ContentCatalog.Vehicles[game.Save.selectedVehicle], game.Save);
            Rect(right, top + 100, 328, 91, Ink);
            Text(right + 20, top + 116, 285, 26, Mathf.RoundToInt(stats.horsepower) + " HP     " + Mathf.RoundToInt(stats.mass) + " KG     " + stats.drivetrain, 19, Cream, true);
            Text(right + 20, top + 151, 285, 24, "STALLION • FIELD MECHANIC   /   JOHNNY • DISPATCH", 12, Muted, true);
            Rect(right,top+207,328,130,Ink);
            Text(right+20,top+220,290,36,"TWO WAVES. SIX HOSTILE VEHICLES.",13,Cream,true);
            if(Button(right+20,top+262,288,56,"COMBAT TRIAL  /  F2 · X",true))game.StartCombatTrial();
            float trayX = left + w + 24, trayW = width - trayX - 32;
            Rect(trayX, top + panelHeight - 175, trayW, 175, Ink);
            Tag(trayX + 20, top + panelHeight - 155, "THE ROAD TO UNLIMITED LIABILITY", Orange);
            Text(trayX + 20, top + panelHeight - 120, trayW - 40, 38, "ACT " + (game.SelectedMission / 3 + 1) + "  /  " + new[] { "FIRST DAY, FINAL NOTICE", "A PATTERN OF NONPAYMENT", "EVERYTHING IS FINANCED", "UNINCORPORATED WARFARE", "THE FINAL NOTICE" }[game.SelectedMission / 3], 18, Cream, true);
            for (int i = 0; i < ContentCatalog.Missions.Length; i++) {
                float bWidth = Mathf.Min(44, (trayW - 40) / 15 - 5);
                bool completed = game.Save.completedMissions.Contains(i);
                if (Button(trayX + 20 + i * (bWidth + 5), top + panelHeight - 65, bWidth, 38, completed ? "✓" : (i + 1).ToString("00"), game.SelectedMission == i, i <= game.Save.unlockedMission)) game.SelectedMission = i;
            }
            Text(trayX + 20, top + 350, trayW - 45, 42, "GOOD DOG. BAD DECISIONS.", 16, Cream, true, TextAnchor.MiddleRight);
        }

        void DrawVehicles(float top, float panelHeight)
        {
            Rect(32, top, 450, panelHeight, Ink);
            Tag(56, top + 20, "THESE ALL PASSED SOMEONE’S INSPECTION", Orange);
            listScroll = GUI.BeginScrollView(new Rect(48, top + 57, 418, panelHeight - 75), listScroll, new Rect(0, 0, 390, ContentCatalog.Vehicles.Length * 82));
            for (int i = 0; i < ContentCatalog.Vehicles.Length; i++) {
                var v = ContentCatalog.Vehicles[i]; bool unlocked = v.unlockMission <= game.Save.unlockedMission;
                if (Button(0, i * 82, 385, 67, v.displayName.ToUpperInvariant() + "\n" + (unlocked ? v.drivetrain + "  •  " + v.horsepower + " HP  •  " + v.mass + " KG" : "UNLOCK: WORK ORDER " + (v.unlockMission + 1)), game.Save.selectedVehicle == i, unlocked)) {
                    if (GarageManager.SelectVehicle(i, game.Save)) game.RefreshGarageVehicle(); else game.Notify("The fleet fund cannot cover that vehicle yet.");
                }
            }
            GUI.EndScrollView();
            var def = ContentCatalog.Vehicles[game.Save.selectedVehicle];
            float x = width - 500;
            Rect(x, top, 468, 290, Ink);
            Tag(x + 24, top + 20, "CURRENT LIABILITY", Lime);
            Text(x + 24, top + 52, 420, 85, def.displayName.ToUpperInvariant(), 37, Cream, true);
            Text(x + 24, top + 148, 420, 115, def.description, 19, Muted);
            DrawVehicleStats(x, top + panelHeight - 200, 468);
        }

        void DrawVehicleStats(float x, float y, float w)
        {
            var def = ContentCatalog.Vehicles[game.Save.selectedVehicle]; var s = GarageManager.StatsFor(def, game.Save);
            Rect(x, y, w, 200, Ink);
            Tag(x + 24, y + 20, "DYNO SHEET / INSTALLED CONFIGURATION", Orange);
            Text(x + 24, y + 57, w - 48, 31, Mathf.RoundToInt(s.horsepower) + " HP     " + Mathf.RoundToInt(s.torque) + " NM     " + Mathf.RoundToInt(s.mass) + " KG", 24, Cream, true);
            Text(x + 24, y + 104, w - 48, 75, s.drivetrain + "  /  " + s.differential + " DIFFERENTIAL\n" + s.grip.ToString("0.00") + " GRIP  •  " + Mathf.RoundToInt(s.maxHealth) + " STRUCTURE\n" + s.finalDrive.ToString("0.00") + ":1 FINAL DRIVE  •  " + Mathf.RoundToInt(s.maxSpeed * .621371f) + " MPH TOP SPEED", 17, Muted);
        }

        void DrawParts(float top, float panelHeight)
        {
            Rect(32, top, 500, panelHeight, Ink);
            Tag(56, top + 20, "REAL PARTS. QUESTIONABLE COMBINATIONS.", Orange);
            listScroll = GUI.BeginScrollView(new Rect(48, top + 57, 465, panelHeight - 78), listScroll, new Rect(0, 0, 433, ContentCatalog.Parts.Length * 81));
            for (int i = 0; i < ContentCatalog.Parts.Length; i++) {
                var p = ContentCatalog.Parts[i];
                bool installed = game.Save.installedParts.Contains(p.id), owned = game.Save.ownedParts.Contains(p.id);
                string status = installed ? "INSTALLED" : owned ? "IN STOCK" : "$" + p.cost;
                if (Button(0, i * 81, 430, 68, p.displayName + "\n" + p.category.ToUpperInvariant() + "  /  " + status, selectedPart == i)) { selectedPart = i; }
            }
            GUI.EndScrollView();
            float x = width - 535;
            var part = ContentCatalog.Parts[selectedPart];
            Rect(x, top, 503, 377, Ink);
            Tag(x + 24, top + 20, part.category.ToUpperInvariant() + " / SHOP NOTES", Orange);
            Text(x + 24, top + 51, 455, 78, part.displayName.ToUpperInvariant(), 32, Cream, true);
            Text(x + 24, top + 142, 455, 102, part.description, 18, Cream);
            Text(x + 24, top + 251, 455, 42, "+" + part.mass + " KG   •   POWER ×" + part.hpMultiplier.ToString("0.00") + "   •   GRIP ×" + part.gripMultiplier.ToString("0.00") + "\nTORQUE ×" + part.torqueMultiplier.ToString("0.00") + "   •   STRUCTURE " + (part.healthBonus < 0 ? "" : "+") + part.healthBonus, 14, Muted);
            bool ownedPart = game.Save.ownedParts.Contains(part.id), isInstalled = game.Save.installedParts.Contains(part.id);
            bool compatible = GarageManager.Compatible(part, ContentCatalog.Vehicles[game.Save.selectedVehicle]);
            bool available = part.unlockMission <= game.Save.unlockedMission && compatible;
            string button = !compatible ? "DOES NOT FIT THIS VEHICLE" : !available ? "UNLOCK AT WORK ORDER " + (part.unlockMission + 1) : ownedPart ? isInstalled ? "REMOVE PART" : "INSTALL PART" : "BUY & INSTALL  /  $" + part.cost;
            if (Button(x + 24, top + 313, 455, 43, button, true, available)) BuyOrInstall(part);
            Rect(x, top + 386, 503, 68, Ink);
            Text(x + 18, top + 397, 128, 24, "FINAL DRIVE", 12, Muted, true);
            float finalDrive = GUI.HorizontalSlider(new Rect(x + 147, top + 401, 193, 24), game.Save.finalDriveTuning, .85f, 1.2f);
            Text(x + 18, top + 425, 128, 24, "RIDE HEIGHT", 12, Muted, true);
            float rideHeight = GUI.HorizontalSlider(new Rect(x + 147, top + 429, 193, 24), game.Save.rideHeightTuning, -.08f, .15f);
            game.Save.finalDriveTuning = finalDrive; game.Save.rideHeightTuning = rideHeight;
            if (Button(x + 359, top + 400, 126, 39, "APPLY TUNE")) { GarageManager.Tune(finalDrive, rideHeight, game.Save); game.RefreshGarageVehicle(); }
            DrawVehicleStats(x, top + panelHeight - 200, 503);
        }

        void BuyOrInstall(VehiclePart part)
        {
            if (!GarageManager.Compatible(part, ContentCatalog.Vehicles[game.Save.selectedVehicle])) { game.Notify("This part does not fit this vehicle."); return; }
            if (!game.Save.ownedParts.Contains(part.id) && !GarageManager.BuyPart(part, game.Save)) { game.Notify(GarageManager.LastMessage); return; }
            GarageManager.TogglePart(part, game.Save); game.RefreshGarageVehicle();
            game.Notify(GarageManager.LastMessage);
        }

        void SelectGarageWeapon(WeaponDefinition weapon)
        {
            if (GarageManager.BuyOrSelectWeapon(weapon, game.Save)) game.RefreshGarageVehicle();
            game.Notify(GarageManager.LastMessage);
        }

        void DrawWeapons(float top, float panelHeight)
        {
            Rect(32, top, 500, panelHeight, Ink);
            Tag(56, top + 20, "RT / GARAGE WEAPON • SPEND SCRAP TO OWN", Orange);
            listScroll = GUI.BeginScrollView(new Rect(48, top + 57, 465, panelHeight - 75), listScroll, new Rect(0, 0, 433, garageWeapons.Length * 91));
            for (int i = 0; i < garageWeapons.Length; i++)
            {
                var weapon = ContentCatalog.Weapons[garageWeapons[i]];
                string status = game.Save.selectedWeapon == weapon.id ? "FITTED" :
                    game.Save.ownedWeapons.Contains(weapon.id) ? "OWNED" : WeaponRules.ScrapCost(weapon.id) + " SCRAP";
                if (WeaponRules.Oddball(weapon.id)) status += "   •   ODDBALL";
                if (Button(0, i * 91, 430, 74, weapon.displayName + "\n" + status, selectedWeapon == i)) selectedWeapon = i;
            }
            GUI.EndScrollView();
            var selected = ContentCatalog.Weapons[garageWeapons[Mathf.Clamp(selectedWeapon, 0, garageWeapons.Length - 1)]];
            float x = width - 535;
            Rect(x, top, 503, panelHeight, Ink);
            Tag(x + 24, top + 24, "GARAGE LOADOUT / RIGHT TRIGGER", Lime);
            Text(x + 24, top + 56, 455, 70, selected.displayName.ToUpperInvariant(), 29, Cream, true);
            Tag(x + 24, top + 124, (WeaponRules.Oddball(selected.id) ? "ODDBALL  •  " : "") + (selected.role ?? ""), Orange);
            Text(x + 24, top + 152, 455, 92, selected.description, 17, Cream);
            Text(x + 24, top + 250, 455, 22, "STRENGTH", 13, Lime, true);
            Text(x + 24, top + 272, 455, 50, selected.strength ?? "", 16, Cream);
            Text(x + 24, top + 326, 455, 22, "WEAKNESS", 13, Orange, true);
            Text(x + 24, top + 348, 455, 50, selected.weakness ?? "", 16, Cream);
            Text(x + 24, top + 406, 455, 30, selected.damage + " DAMAGE / HIT    •    " + selected.fireRate.ToString("0.0") + " SHOTS/SEC", 15, Muted);
            Text(x + 24, top + 440, 455, 70, "LT holds enemy weapon drops. Drive over one to equip it; press Y / F to swap when carrying one.", 15, Muted);
            bool owned = game.Save.ownedWeapons.Contains(selected.id);
            string label = game.Save.selectedWeapon == selected.id ? "FITTED TO RT" : owned ? "EQUIP ON RT" : "BUY & EQUIP / " + WeaponRules.ScrapCost(selected.id) + " SCRAP";
            if (Button(x + 24, top + panelHeight - 88, 455, 56, label, true)) SelectGarageWeapon(selected);
        }

        void DrawDog(float top, float panelHeight)
        {
            Rect(32, top, 495, panelHeight, Ink);
            Tag(60, top + 25, "EMPLOYEE OF EVERY MONTH", Orange);
            Text(60, top + 65, 430, 88, "SUZUKI", 64, Cream, true);
            Text(60, top + 172, 430, 145, "Head of security.\nSenior inventory specialist.\nA completely ordinary dog.", 26);
            Rule(60, top + 336, 439);
            Text(60, top + 366, 430, 125, "She locates hidden salvage, supervises questionable repairs, and is never a combat target. Her compensation is confidential.", 20, Muted);
            Tag(60, top + panelHeight - 75, "PERFORMANCE REVIEW: GOOD", Lime);
            float x = width - 480;
            Rect(x, top, 448, 399, Ink);
            Tag(x + 25, top + 23, "COMPLETELY NECESSARY EQUIPMENT", Orange);
            string[] looks = { "FACTORY DOG", "SAFETY INSPECTOR", "DESERT DOGGLES", "RECOVERY CAPTAIN" };
            for (int i = 0; i < looks.Length; i++) if (Button(x + 25, top + 66 + i * 77, 398, 58, looks[i], game.Save.dogCosmetic == i)) { game.Save.dogCosmetic = i; game.ReturnToGarage(); station = 4; }
        }

        void DrawHUD()
        {
            var p = game.Player; if (!p) return;
            DrawCombatReadability();
            Rect(28, 26, 326, 79, Ink);
            Rect(28, 26, 4, 79, Orange);
            Text(47, 41, 300, 31, "MADE IN ARIZONA", 23, Cream, true);
            Tag(47, 78, (game.IsCombatTrial?"PROVING GROUND":ContentCatalog.Missions[game.SelectedMission].region.ToUpperInvariant()) + "  /  " + Mathf.RoundToInt(smoothedFps) + " FPS", Muted);
            float objX = width - 485;
            Rect(objX, 26, 457, 141, Ink);
            Tag(objX + 20, 42, (game.IsCombatTrial?"COMBAT TRIAL":"WORK ORDER " + (game.SelectedMission + 1).ToString("00")) + " / " + game.Mission.Remaining.ToString("0") + "s", Orange);
            Text(objX + 20, 73, 416, 69, game.Mission.Objective, 23, Cream, true);
            Bar(objX + 20, 149, 416, 4, game.Mission.Progress, Lime);
            Rect(28, height - 194, 365, 166, Ink);
            Text(48, height - 177, 330, 26, game.CurrentVehicle.displayName.ToUpperInvariant(), 16, Cream, true);
            DrawHealthBar(p, 48, height - 136, 240, 14);
            bool critical = p.Damage.Health / Mathf.Max(1, p.Damage.MaxHealth) < CriticalHealth;
            Text(300, height - 144, 75, 30, Mathf.CeilToInt(p.Damage.Health) + " HP", critical ? 16 : 14, critical ? new Color(1, .3f, .15f) : Cream, true);
            Bar(48, height - 102, 240, 6, p.BoostCharge, Blue);
            Text(300, height - 112, 75, 28, Mathf.RoundToInt(p.BoostCharge * 100) + "% N2O", 13, Blue, true);
            Text(48, height - 78, 320, 30, (p.SpeedKph * .621371f).ToString("000") + " MPH     " + p.RPM.ToString("0") + " RPM", 19, Cream, true);
            float weaponsX = 410;
            bool padControls = InputManager.Instance.UsingGamepad;
            Rect(weaponsX, height - 104, 210, 76, Ink);
            Tag(weaponsX + 12, height - 90, padControls ? "RT  /  GARAGE" : "LMB  /  GARAGE", Lime);
            Text(weaponsX + 12, height - 66, 190, 30, p.Weapons.GarageWeapon ? p.Weapons.GarageWeapon.displayName.ToUpperInvariant() : "NAIL GUN", 15, Cream, true);
            Bar(weaponsX + 12, height - 34, 186, 4, 1 - p.Weapons.GarageCooldown, Lime);
            Rect(weaponsX + 220, height - 104, 210, 76, Ink);
            Tag(weaponsX + 232, height - 90, padControls ? "LT  /  FIELD" : "RMB  /  FIELD", Orange);
            Text(weaponsX + 232, height - 66, 190, 30, p.Weapons.FieldWeapon ? p.Weapons.FieldWeapon.displayName.ToUpperInvariant() + "  " + p.Weapons.FieldAmmo : "EMPTY • FIND A DROP", p.Weapons.FieldWeapon ? 15 : 13, Cream, true);
            Bar(weaponsX + 232, height - 34, 186, 4, 1 - p.Weapons.FieldCooldown, Orange);
            var nearbyWeapon = CombatPickup.NearbyWeapon(p);
            if (nearbyWeapon)
            {
                Rect(weaponsX, height - 191, 430, 42, new Color(.04f, .07f, .08f, .92f));
                Rect(weaponsX, height - 191, 4, 42, Orange);
                Text(weaponsX + 14, height - 185, 400, 30, p.Weapons.FieldWeapon ?
                    (padControls ? "Y" : "F") + "  SWAP  •  " + nearbyWeapon.Weapon.displayName.ToUpperInvariant() :
                    "PICK UP  •  " + nearbyWeapon.Weapon.displayName.ToUpperInvariant(), 17, Cream, true, TextAnchor.MiddleLeft);
            }
            // Backing panel: the control hints were unreadable over pale sand.
            Rect(weaponsX, height - 142, 430, 28, new Color(Ink.r, Ink.g, Ink.b, .85f));
            var hintSettings = game.Save.settings;
            string padHint = (hintSettings.boostOnSouth ? "A BOOST" : "RB BOOST") + "    B DRIFT    Y SWAP" + (hintSettings.shoulderReverse ? "    LB REVERSE" : "");
            Text(weaponsX + 12, height - 142, 418, 28, padControls ? padHint : "SHIFT BOOST    SPACE DRIFT    F SWAP" + (hintSettings.shoulderReverse ? "    R REVERSE" : ""), 12, Cream, true, TextAnchor.MiddleLeft);
            if (game.Mission.Combo > 1) Text(28, 136, 320, 43, "×" + game.Mission.Combo + "  INSURANCE EVENT", 23, Orange, true);
            Text(28, 185, 320, 30, game.Mission.Score.ToString("N0") + "  DAMAGE CLAIM", 17, Cream, true);
            DrawMinimap(width - 216, height - 228, 188);
            DrawObjectiveMarker();
            if (game.State == GameState.Playing && InputManager.Instance.UsingGamepad == false && Mouse.current != null) {
                var m = Mouse.current.position.ReadValue(); float s = Screen.height / height;
                float x = m.x / s, y = (Screen.height - m.y) / s;
                Rect(x - 11, y - 1, 7, 2, Cream); Rect(x + 4, y - 1, 7, 2, Cream); Rect(x - 1, y - 11, 2, 7, Cream); Rect(x - 1, y + 4, 2, 7, Cream);
            }
        }

        Vector2 ScreenPoint(Vector3 world)
        {
            var cam=Camera.main;var point=cam.WorldToViewportPoint(world);
            // A perspective lens mirrors points behind it through the screen centre; un-mirror so directions stay true.
            if(point.z<0&&!cam.orthographic){point.x=1-point.x;point.y=1-point.y;}
            return new Vector2(point.x*width,(1-point.y)*height);
        }
        void CombatLine(Vector2 a,Vector2 b,float thickness,Color color)
        {
            // Rotate in the HUD's own (scaled) space. GUIUtility.RotateAroundPivot ignores the HUD scale, so at any UI
            // scale other than 1 every rotated line drifted off its pivot: the vertical bar of the gamepad reticle
            // separated from the horizontal one ("the reticle splits in two").
            Matrix4x4 matrix=GUI.matrix;
            GUI.matrix=matrix*Matrix4x4.TRS(a,Quaternion.Euler(0,0,Mathf.Atan2(b.y-a.y,b.x-a.x)*Mathf.Rad2Deg),Vector3.one)*Matrix4x4.Translate(-a);
            Rect(a.x,a.y-thickness*.5f,(b-a).magnitude,thickness,color);GUI.matrix=matrix;
        }
        void DrawCombatReadability()
        {
            if(!Camera.main || !game.Player || game.State!=GameState.Playing)return;
            foreach(var pickup in CombatPickup.Active)
            {
                if(!pickup || pickup.IsMagnetized || (pickup.transform.position-game.Player.transform.position).sqrMagnitude>6400)continue;
                var view=Camera.main.WorldToViewportPoint(pickup.transform.position+Vector3.up*1.1f);
                if(view.z<=0||view.x<0||view.x>1||view.y<0||view.y>1)continue;
                var point=ScreenPoint(pickup.transform.position+Vector3.up*1.1f);
                string label=pickup.Kind==PickupKind.Weapon ? pickup.Weapon.displayName.ToUpperInvariant()+"  "+pickup.Amount :
                    pickup.Kind==PickupKind.Health ? "+HEALTH" : pickup.Kind==PickupKind.Nitro ? "+NITRO" : "+SCRAP";
                Color color=pickup.Kind==PickupKind.Health?Lime:pickup.Kind==PickupKind.Nitro?Blue:pickup.Kind==PickupKind.Scrap?Orange:Cream;
                if (pickup.Kind == PickupKind.Weapon)
                {
                    Rect(point.x - 99, point.y - 13, 198, 30, new Color(.04f, .07f, .08f, .87f));
                    Rect(point.x - 99, point.y - 13, 3, 30, Orange);
                    Text(point.x - 90, point.y - 10, 180, 24, label, 13, color, true, TextAnchor.MiddleCenter);
                }
                else Text(point.x-82,point.y-10,164,24,label,12,color,true,TextAnchor.MiddleCenter);
            }
            string[] names={"FLANKER","TECHNICAL","RAMMER","SNIPER","ROCKET CARRIER","JUNK BOMB","HEAVY","COMMAND"};
            foreach(var vehicle in VehicleController.Active) {
                if(!vehicle||vehicle.IsPlayer||vehicle.Damage.IsDead)continue;
                var ai=vehicle.GetComponent<EnemyAI>();if(!ai)continue;
                var view=Camera.main.WorldToViewportPoint(vehicle.transform.position);
                if(view.z<0 || view.x<0 || view.x>1 || view.y<0 || view.y>1)continue;
                var point=ScreenPoint(vehicle.transform.position+Vector3.up*3);
                Color color=ai.IsFriendly?Blue:FactionRules.Accent(ai.Faction);
                Bar(point.x-37,point.y-8,74,5,vehicle.Damage.Health/vehicle.Damage.MaxHealth,color);
                if(!ai.IsFriendly)Text(point.x-110,point.y-43,220,16,FactionRules.Name(ai.Faction),9,color,true,TextAnchor.MiddleCenter);
                Text(point.x-80,point.y-28,160,20,ai.IsFriendly?"ESCORT":names[Mathf.Clamp(ai.Archetype,0,7)],10,color,true,TextAnchor.MiddleCenter);
                if(ai.IsTelegraphingAttack) {
                    Text(point.x-110,point.y+20,220,25,ai.AttackTelegraph.ToString().ToUpperInvariant()+"  "+ai.TelegraphRemaining.ToString("0.0")+"s",13,Orange,true,TextAnchor.MiddleCenter);
                }
            }
            if(Time.time-CombatFeedback.LastHitTime<.16f) {
                Vector2 point=ScreenPoint(CombatFeedback.LastHitPoint);
                CombatLine(point+new Vector2(-9,-9),point+new Vector2(-3,-3),3,Lime);
                CombatLine(point+new Vector2(9,9),point+new Vector2(3,3),3,Lime);
                CombatLine(point+new Vector2(-9,9),point+new Vector2(-3,3),3,Lime);
                CombatLine(point+new Vector2(9,-9),point+new Vector2(3,-3),3,Lime);
            }
            if(Time.time-CombatFeedback.LastKillTime<1.1f)Text(width*.5f-160,180,320,35,"HOSTILE VEHICLE DISABLED",19,Lime,true,TextAnchor.MiddleCenter);
            DrawDamageFeedback(game.Player);
            if(InputManager.Instance.UsingGamepad) {
                Vector2 aim=InputManager.Instance.Aim;
                Vector2 point=ScreenPoint(game.Player.transform.position+new Vector3(aim.x,0,aim.y)*14+Vector3.up*.8f);
                CombatLine(point+Vector2.left*8,point+Vector2.right*8,2,Cream);CombatLine(point+Vector2.up*8,point+Vector2.down*8,2,Cream);
            }
        }

        void Bar(float x, float y, float w, float h, float fraction, Color color) { Rect(x, y, w, h, new Color(.2f, .26f, .27f)); Rect(x, y, w * Mathf.Clamp01(fraction), h, color); }

        void DrawMinimap(float x, float y, float size)
        {
            if(GeneratedWorld.Active){DrawGeneratedMinimap(x,y,size);return;}
            Rect(x, y, size, size, Ink);
            Tag(x + 12, y + 9, "N  /  LIABILITY MAP", Muted);
            Rect(x + size * .48f, y + 31, size * .05f, size - 43, new Color(.24f, .29f, .28f));
            Vector3 player = game.Player.transform.position;
            var objective = game.Mission.ObjectivePosition;
            Func<Vector3, Vector2> map = p => new Vector2(x + 12 + Mathf.InverseLerp(game.IsCombatTrial?-54:-110, game.IsCombatTrial?54:110, p.x) * (size - 24), y + 34 + (1 - Mathf.InverseLerp(game.IsCombatTrial?-46:-90, game.IsCombatTrial?46:150, p.z)) * (size - 48));
            foreach(var vehicle in VehicleController.Active) {
                if(!vehicle||vehicle.IsPlayer||vehicle.Damage.IsDead)continue;
                var ai=vehicle.GetComponent<EnemyAI>();if(!ai)continue;
                var dot=map(vehicle.transform.position);Rect(dot.x-3,dot.y-3,6,6,ai.IsFriendly?Blue:FactionRules.Accent(ai.Faction));
            }
            Vector2 pp = map(player), op = map(objective);
            Rect(op.x - 5, op.y - 5, 10, 10, Orange); Rect(pp.x - 4, pp.y - 4, 8, 8, Lime);
            Text(x + 12, y + size + 5, size, 22, "ESC  PAUSE / SETTINGS", 11, Muted, true);
        }

        void DrawObjectiveMarker()
        {
            if (game.State != GameState.Playing || game.Mission.ObjectivePosition == Vector3.zero) return;
            Vector3 target = game.Mission.ObjectivePosition;
            float distance = Vector3.Distance(game.Player.transform.position, target);
            if (distance < 8) return;
            var point = Camera.main.WorldToViewportPoint(target + Vector3.up * 2);
            // Behind a perspective lens (far to the south): un-mirror and pin to the bottom edge.
            if (point.z < 0 && !Camera.main.orthographic) { point.x = 1 - point.x; point.y = -1; }
            float x = Mathf.Clamp(point.x * width, 50, width - 80), y = Mathf.Clamp((1 - point.y) * height, 205, height - 250);
            Text(x - 80, y, 160, 30, "◇ " + Mathf.RoundToInt(distance) + " m", 23, Orange, true, TextAnchor.MiddleCenter);
        }

        void DrawDialogue()
        {
            float w=350,x=28,y=135;
            float h=StylesRadioHeight(DialogueSystem.Instance.Text,w-42);
            Rect(x,y,w,h+40,Ink);Rect(x,y,3,h+40,Blue);
            Text(x+12,y+8,w-44,18,"RADIO / "+DialogueSystem.Instance.Speaker.ToUpperInvariant(),11,Blue,true);
            Text(x+12,y+31,w-30,h,DialogueSystem.Instance.Text,14);
            if(Button(x+w-29,y+6,22,22,"×"))DialogueSystem.Instance.Skip();
        }

        void DrawPause()
        {
            Rect(0, 0, width, height, new Color(.02f, .04f, .045f, .74f));
            float x = (width - 500) / 2, y = (height - 690) / 2;
            Rect(x, y, 500, 690, Ink); Tag(x + 40, y + 33, "ENGINE IDLING / WORLD ON HOLD", Orange);
            Text(x + 40, y + 75, 420, 77, "SERVICE BREAK", 41, Cream, true);
            if (Button(x + 40, y + 176, 420, 56, "BACK TO THE PROBLEM  /  A", true)) game.Resume();
            if (Button(x + 40, y + 249, 420, 49, "SETTINGS  /  Y")) { settings = true; menuFocus = 0; }
            if (Button(x + 40, y + 315, 420, 49, "DEV TUNING  /  MOUSE")) { devMenu=true; settings=false; }
            if(Button(x+40,y+380,420,49,"ARIZONA MAP / M",false,GeneratedWorld.Active!=null))OpenWorldMap();
            if(Button(x+40,y+445,420,49,CoopSession.IsRemoteClient?"LEAVE CO-OP / MAIN MENU":"MAIN MENU")) { if (CoopSession.IsRemoteClient) LeaveCoop(); else game.ShowMainMenu(); }
            if (Button(x + 40, y + 510, 420, 49, "RETURN TO GARAGE  /  B",false,!CoopSession.IsRemoteClient)) game.ReturnToGarage();
            if (Button(x + 40, y + 575, 420, 49, "REGENERATE MAP  •  NEW SEED", false, GeneratedWorld.Active != null && !game.IsCombatTrial && !CoopSession.IsRemoteClient)) game.RegenerateWorld();
            Text(x + 40, y + 630, 420, 40, "Restarts this job on a freshly generated Arizona. Progress is kept.", 13, Muted);
        }

        /// <summary>Red flash at the moment of death, a closing dark vignette, and a WRECKED title before the debrief.</summary>
        void DrawDeathOverlay()
        {
            float t = Time.unscaledTime - game.DyingStarted;
            float flash = Mathf.Clamp01(.55f - t * .9f);
            if (flash > 0) Rect(0, 0, width, height, new Color(1, .12f, .05f, Mathf.Round(flash * 20) / 20));
            float dark = Mathf.SmoothStep(0, .55f, t / GameManager.DeathSequenceSeconds);
            float band = Mathf.Lerp(0, height * .22f, dark / .55f);
            Rect(0, 0, width, band, new Color(0, 0, 0, dark + .3f)); Rect(0, height - band, width, band, new Color(0, 0, 0, dark + .3f));
            float title = Mathf.Round(Mathf.Clamp01((t - .45f) / .5f) * 20) / 20; // stepped: styles are cached per colour
            if (title <= 0) return;
            Text(0, height * .5f - 70, width, 90, "WRECKED", 84, new Color(1, .35f, .18f, title), true, TextAnchor.MiddleCenter);
            Text(0, height * .5f + 22, width, 34, "THE VEHICLE HAS BEEN DECLARED A TOTAL LOSS", 18, new Color(.95f, .9f, .8f, title * .9f), true, TextAnchor.MiddleCenter);
        }
        void DrawDebrief()
        {
            bool won = game.State == GameState.Won;
            Rect(0, 0, width, height, new Color(.025f, .035f, .035f, .7f));
            float x = (width - 740) / 2, y = (height - 600) / 2;
            Rect(x, y, 740, 600, Ink); Rect(x, y, 740, 5, won ? Lime : Orange);
            Tag(x + 40, y + 33, won ? "WORK ORDER CLOSED" : "CUSTOMER DECLINED RECOMMENDED SERVICE", won ? Lime : Orange);
            Text(x + 40, y + 76, 660, 90, won ? (game.IsCombatTrial?"TRIAL COMPLETE.":"PAYMENT COLLECTED.*") : "THAT WILL NOT BUFF OUT.", 45, Cream, true);
            Text(x + 40, y + 187, 660, 106, won ? (game.IsCombatTrial?game.Mission.Debrief:ContentCatalog.Missions[game.SelectedMission].closing) : "Recovery truck dispatched. Suzuki has already filed the paperwork. Your garage progression is safe.", 24);
            Rule(x + 40, y + 324, 660);
            Text(x + 40, y + 352, 660, 59, game.Mission.Score.ToString("N0") + " POINTS     " + game.Mission.Kills + " VEHICLES\n" + game.Mission.DestructionCount + " INSURANCE CLAIMS", 22, Cream, true);
            if (won) Text(x + 40, y + 424, 650, 34, game.IsCombatTrial?"PRACTICE COMPLETE • CAMPAIGN UNCHANGED":"+$" + game.Mission.AwardedMoney + "  /  PROGRESSION SAVED", 20, Lime, true);
            if (CoopSession.IsRemoteClient)
            {
                if (Button(x + 40, y + 498, 393, 58, "WAITING FOR HOST", true, false)) { }
                if (Button(x + 450, y + 498, 250, 58, "LEAVE CO-OP")) LeaveCoop();
            }
            else
            {
                if (Button(x + 40, y + 498, 393, 58, "BACK TO 117° AUTO CARE  /  A", true)) game.ReturnToGarage();
                if (Button(x + 450, y + 498, 250, 58, "REPLAY  /  X")) game.RetryMission();
            }
        }

        void DrawSettings()
        {
            Rect(0, 0, width, height, new Color(.02f, .03f, .035f, .90f));
            float x = (width - 930) / 2, y = Mathf.Max(25, (height - 845) / 2);
            Rect(x, y, 930, 845, Ink);
            Tag(x + 35, y + 25, "OWNER’S MANUAL / THE USEFUL PAGES", Orange);
            Text(x + 35, y + 59, 750, 57, "SHOP SETTINGS", 40, Cream, true);
            for (int i = 0; i < SettingsTabs.Length; i++) if (Button(x + 35 + i * 215, y + 127, 205, 41, SettingsTabs[i], settingsPage == i, size: 13)) { settingsPage = i; menuFocus = 0; }
            var s = game.Save.settings;
            if (settingsPage == 0) {
                SettingLabel(x, y + 196, "WINDOW MODE", 0);
                if (Button(x + 465, y + 191, 426, 38, Screen.fullScreenMode.ToString())) CycleWindow();
                SettingLabel(x, y + 249, "RESOLUTION", 1);
                if (Button(x + 465, y + 244, 426, 38, Screen.width + " × " + Screen.height)) CycleResolution();
                SettingLabel(x, y + 302, "DIFFICULTY", 2);
                if (Button(x + 465, y + 297, 426, 38, new[] { "SUNDAY DRIVER", "SHOP STANDARD", "LIABILITY WAIVER" }[Mathf.Clamp(s.difficulty, 0, 2)])) s.difficulty = (s.difficulty + 1) % 3;
                SettingLabel(x, y + 355, "CAMERA SHAKE", 3); s.shake = Slider(x + 465, y + 355, 426, s.shake, 0, 1);
                SettingLabel(x, y + 408, "INTERFACE SCALE", 4); s.uiScale = Slider(x + 465, y + 408, 426, s.uiScale, .85f, 1.2f);
                SettingLabel(x, y + 461, "SUBTITLES", 5); if (Button(x + 465, y + 456, 426, 38, s.subtitles ? "ON" : "OFF")) s.subtitles = !s.subtitles;
            }
            if (settingsPage == 1) {
                SettingLabel(x, y + 191, "GRAPHICS PRESET", 0);
                if (Button(x + 465, y + 188, 426, 38, presets[s.quality])) { s.quality = (s.quality + 1) % 4; game.ApplySettings(); }
                Text(x + 35, y + 232, 850, 40, s.quality == 3 ? "Long-lived debris, dense explosion layers, 4K shadows, more lights. Hardware performance varies; profiling required." : "Shared URP renderer. Metal on Mac, Direct3D on Windows. All presets preserve combat readability.", 14, Muted);
                SettingLabel(x, y + 291, "SHADOW DETAIL", 1);
                if (Button(x + 465, y + 286, 426, 38, ShadowDetailLabels[s.shadowDetail])) { s.shadowDetail = (s.shadowDetail + 1) % 3; game.ApplySettings(); }
                Text(x + 35, y + 330, 850, 40, ShadowDetailNotes[s.shadowDetail], 14, Muted);
                SettingLabel(x, y + 389, "TERRAIN CAMERA SWAY", 2); s.cameraSway = Slider(x + 465, y + 389, 426, s.cameraSway, 0, 1);
                Text(x + 35, y + 425, 850, 40, "The camera tips a few degrees as you climb, dip and travel, so hills and drops read in 3D. 0% keeps the fixed overhead view.", 14, Muted);
                SettingLabel(x, y + 484, "DYNAMIC CAMERA ZOOM", 3); if (Button(x + 465, y + 479, 426, 38, s.dynamicZoom ? "ON • PULLS BACK FOR EDGE THREATS" : "OFF • FIXED DISTANCE")) s.dynamicZoom = !s.dynamicZoom;
                SettingLabel(x, y + 537, "VSYNC", 4); if (Button(x + 465, y + 532, 426, 38, FrameSyncLabels[s.frameSync])) { s.frameSync = (s.frameSync + 1) % 3; game.ApplySettings(); }
                SettingLabel(x, y + 590, "CAMERA STYLE", 5); if (Button(x + 465, y + 585, 426, 38, s.perspectiveCamera ? "PERSPECTIVE • 3D DEPTH" : "ORTHOGRAPHIC • FLAT OVERHEAD")) s.perspectiveCamera = !s.perspectiveCamera;
                Text(x + 35, y + 628, 850, 40, "Perspective keeps the same angle and framing at the car, with distance falling away toward the top of the screen. The garage stays orthographic.", 14, Muted);
                SettingLabel(x, y + 686, "TIME OF DAY", 6); if (Button(x + 465, y + 681, 426, 38, TimeOfDayLabel(s.sunset))) s.sunset = !s.sunset;
                Text(x + 35, y + 724, 850, 24, "Sunset: a low warm sun with long shadows and a darker, softer image. Changes ease in over a few seconds.", 14, Muted);
            }
            if (settingsPage == 2) {
                string[] labels = { "MASTER", "MUSIC", "ENGINES", "WEAPONS", "DIALOGUE CUES", "ENVIRONMENT" };
                float[] values = { s.master, s.music, s.engines, s.weapons, s.dialogue, s.environment };
                for (int i = 0; i < labels.Length; i++) { SettingLabel(x, y + 215 + i * 65, labels[i], i); values[i] = Slider(x + 465, y + 215 + i * 65, 426, values[i], 0, 1); }
                s.master = values[0]; s.music = values[1]; s.engines = values[2]; s.weapons = values[3]; s.dialogue = values[4]; s.environment = values[5]; AudioListener.volume = AudioManager.OutputVolume(s.master);
                Text(x + 35, y + 634, 850, 36, "Original procedural score and synthesized effects. Dialogue is subtitled, with radio cues.", 15, Muted);
            }
            if (settingsPage == 3) {
                Text(x + 35, y + 186, 850, 52, "WASD drive / mouse aim / LMB garage weapon / RMB field weapon\nF swap drop / Space drift / Shift boost / Esc pause" + (s.shoulderReverse ? " / hold R to reverse" : ""), 17);
                Text(x + 35, y + 242, 850, 52, "GAMEPAD: left stick drive, right stick aim. RT garage weapon, LT field weapon.\nY swap drop, " +
                    (s.boostOnSouth ? "A boost, RB interact" : "RB boost, A interact") + ", B drift" + (s.shoulderReverse ? ", LB reverse" : "") + ", Start pause.", 17, Muted);
                Tag(x + 35, y + 306, "REBIND / SELECT A CONTROL THEN PRESS A NEW INPUT", Orange);
                for (int i = 0; i < actions.Length; i++) {
                    float bx = x + 35 + i % 4 * 215, by = y + 338 + i / 4 * 56;
                    string action = i < 4 ? "Move" : actions[i];
                    int binding = i < 4 ? i + 1 : 0;
                    if (InputManager.Instance.UsingGamepad) binding = InputManager.Instance.Actions.FindAction(action).bindings.Count - 1;
                    if (Button(bx, by, 202, 45, (action == "Handbrake" ? "DRIFT" : actions[i].ToUpperInvariant()) + " / " + InputManager.Instance.BindingLabel(action, binding), InputManager.Instance.UsingGamepad && menuFocus == i + 4)) BeginControlRebind(i);
                }
                SettingLabel(x, y + 513, "AIM ASSIST", 0);
                if (Button(x + 465, y + 508, 426, 38, new[] { "OFF", "LIGHT", "GENEROUS" }[Mathf.Clamp(s.aimAssist, 0, 2)])) s.aimAssist = (s.aimAssist + 1) % 3;
                SettingLabel(x, y + 560, "REVERSE (GAMEPAD)", 1);
                if (Button(x + 465, y + 555, 426, 38, s.shoulderReverse ? "HOLD LB • STICK NEVER REVERSES" : "AUTO • HOLD STICK BEHIND THE CAR")) s.shoulderReverse = !s.shoulderReverse;
                SettingLabel(x, y + 607, "NITRO BUTTON (GAMEPAD)", 2);
                if (Button(x + 465, y + 602, 426, 38, s.boostOnSouth ? "A • INTERACT MOVES TO RB" : "RB • INTERACT ON A")) s.boostOnSouth = !s.boostOnSouth;
                if (Button(x + 35, y + 652, 273, 37, "RESET INPUT BINDINGS", InputManager.Instance.UsingGamepad && menuFocus == 3)) InputManager.Instance.ResetBindings();
                if (InputManager.Instance.Rebinding) { Rect(x + 180, y + 309, 570, 180, Panel); Text(x + 200, y + 336, 530, 85, "PRESS A NEW KEY OR CONTROL\nEscape cancels. Devices are saved separately.", 23, Cream, true, TextAnchor.MiddleCenter); if (Button(x + 345, y + 431, 240, 37, "CANCEL REBIND")) InputManager.Instance.CancelRebind(); }
            }
            if (Button(x + 570, y + 773, 320, 48, "SAVE & CLOSE  /  B", true)) { game.ApplySettings(); settings = false; }
            Text(x + 35, y + 785, 520, 35, "LB/RB tabs · D-pad select/adjust · A toggle · B close", 12, Muted);
        }

        static readonly string[] SettingsTabs = { "DISPLAY & ACCESS", "GRAPHICS & CAMERA", "AUDIO", "CONTROLS" };
        /// <summary>Gamepad rows on each settings page (the controls page adds one per rebindable action).</summary>
        int SettingsRows(int page) => page == 0 ? 6 : page == 1 ? 7 : page == 2 ? 6 : 4 + actions.Length;
        static readonly string[] ShadowDetailLabels = { "MATCH GRAPHICS PRESET", "HIGH • 4K, SOFT EDGES", "ULTRA • 8K, SOFT EDGES" };
        static readonly string[] ShadowDetailNotes = {
            "Shadow map size and range follow the preset.",
            "A 4096 sun shadow map spent on the play area, with filtered edges. For capable GPUs.",
            "An 8192 sun shadow map with the softest filtering. For high-end GPUs; uses about 128 MB of video memory." };
        static readonly string[] FrameSyncLabels = { "ON • MATCH DISPLAY REFRESH", "OFF • 120 FPS CAP", "OFF • UNCAPPED" };
        static string TimeOfDayLabel(bool sunset) => sunset ? "SUNSET • LOW SUN, LONG SHADOWS" : "MIDDAY • FULL DESERT SUN";
        void SettingLabel(float x, float y, string label, int index)
        { Text(x + 35, y, 420, 32, (InputManager.Instance.UsingGamepad && menuFocus == index ? "›  " : "") + label, 17, InputManager.Instance.UsingGamepad && menuFocus == index ? Orange : Cream, true); }
        float Slider(float x, float y, float w, float value, float min, float max)
        {
            value = GUI.HorizontalSlider(new Rect(x, y + 6, w - 65, 24), value, min, max);
            Text(x + w - 60, y, 60, 29, Mathf.RoundToInt(value * 100) + "%", 16, Lime, true, TextAnchor.MiddleRight); return value;
        }
        void CycleWindow()
        {
            Screen.fullScreenMode = Screen.fullScreenMode == FullScreenMode.Windowed ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            game.Save.settings.fullscreen = Screen.fullScreenMode != FullScreenMode.Windowed;
        }
        void CycleResolution()
        {
            int[] widths = { 1280, 1600, 1920, 2560, 3440 };
            int index = Array.FindIndex(widths, w => w > Screen.width); if (index < 0) index = 0;
            int h = widths[index] == 3440 ? 1440 : widths[index] * 9 / 16;
            game.Save.settings.width = widths[index]; game.Save.settings.height = h;
            Screen.SetResolution(widths[index], h, Screen.fullScreenMode);
        }
        void AdjustSetting(int direction)
        {
            var s = game.Save.settings;
            if (settingsPage == 0) {
                if (menuFocus == 0) CycleWindow(); if (menuFocus == 1) CycleResolution();
                if (menuFocus == 2) s.difficulty = (s.difficulty + direction + 3) % 3;
                if (menuFocus == 3) s.shake = Mathf.Clamp01(s.shake + .1f * direction);
                if (menuFocus == 4) s.uiScale = Mathf.Clamp(s.uiScale + .05f * direction, .85f, 1.2f);
                if (menuFocus == 5) s.subtitles = !s.subtitles;
            } else if (settingsPage == 1) {
                if (menuFocus == 0) { s.quality = (s.quality + direction + 4) % 4; game.ApplySettings(); }
                if (menuFocus == 1) { s.shadowDetail = (s.shadowDetail + direction + 3) % 3; game.ApplySettings(); }
                if (menuFocus == 2) s.cameraSway = Mathf.Clamp01(s.cameraSway + .1f * direction);
                if (menuFocus == 3) s.dynamicZoom = !s.dynamicZoom;
                if (menuFocus == 4) { s.frameSync = (s.frameSync + direction + 3) % 3; game.ApplySettings(); }
                if (menuFocus == 5) s.perspectiveCamera = !s.perspectiveCamera;
                if (menuFocus == 6) s.sunset = !s.sunset;
            } else if (settingsPage == 2) {
                if (menuFocus == 0) s.master = Mathf.Clamp01(s.master + .1f * direction);
                if (menuFocus == 1) s.music = Mathf.Clamp01(s.music + .1f * direction);
                if (menuFocus == 2) s.engines = Mathf.Clamp01(s.engines + .1f * direction);
                if (menuFocus == 3) s.weapons = Mathf.Clamp01(s.weapons + .1f * direction);
                if (menuFocus == 4) s.dialogue = Mathf.Clamp01(s.dialogue + .1f * direction);
                if (menuFocus == 5) s.environment = Mathf.Clamp01(s.environment + .1f * direction);
            } else {
                if (menuFocus == 0) s.aimAssist = (s.aimAssist + direction + 3) % 3;
                else if (menuFocus == 1) s.shoulderReverse = !s.shoulderReverse;
                else if (menuFocus == 2) s.boostOnSouth = !s.boostOnSouth;
                else if (menuFocus == 3) InputManager.Instance.ResetBindings();
                else BeginControlRebind(menuFocus - 4);
            }
        }

        void BeginControlRebind(int index)
        {
            string action = index < 4 ? "Move" : actions[index];
            int binding = index < 4 ? index + 1 : 0;
            if (InputManager.Instance.UsingGamepad) binding = InputManager.Instance.Actions.FindAction(action).bindings.Count - 1;
            InputManager.Instance.StartRebind(action, binding, () => game.Notify("Binding saved."));
        }

    }
}
