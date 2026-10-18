using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace MadeInArizona
{
    public sealed partial class GameUI
    {
        static readonly string[] consoleCommands = { "help", "clear", "status", "cooplog", "money", "scrap", "unlock", "weapon", "field", "car", "repair", "nitro", "invulnerable", "garage" };
        readonly List<string> consoleLines = new List<string>();
        readonly List<string> consoleHistory = new List<string>();
        string consoleInput = "", completionSeed;
        int completionIndex = -1, historyIndex = -1;
        bool consoleFocus;
        Rect consoleRect = new Rect(80, 60, 790, 470);
        Vector2 consoleScroll;

        void ToggleConsole()
        {
            ConsoleOpen = !ConsoleOpen;
            completionSeed = null;
            if (ConsoleOpen)
            {
                consoleRect.x = Mathf.Clamp(consoleRect.x, 0, Mathf.Max(0, width - consoleRect.width));
                consoleRect.y = Mathf.Clamp(consoleRect.y, 0, Mathf.Max(0, height - consoleRect.height));
                consoleFocus = true;
                if (consoleLines.Count == 0) WriteConsole("DEV CONSOLE  •  help lists commands  •  Tab cycles suggestions");
            }
            else GUI.FocusControl(null);
        }

        void CaptureConsoleLog(string message, string stack, LogType type)
        {
            if (message.StartsWith("MIA_COOP_TRACE_SAVED:", StringComparison.Ordinal) ||
                message.StartsWith("MIA_COOP_TRACE_COLLECTED:", StringComparison.Ordinal) ||
                message.StartsWith("MIA_COOP_TRACE_UPLOADED:", StringComparison.Ordinal)) WriteConsole(message);
            else if (type == LogType.Warning || type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                WriteConsole("[" + type + "] " + message);
        }

        void WriteConsole(string message)
        {
            consoleLines.Add(message);
            if (consoleLines.Count > 120) consoleLines.RemoveAt(0);
            consoleScroll.y = float.MaxValue;
        }

        void DrawConsole()
        {
            GUI.depth = -1000;
            Rect(0, 0, width, height, new Color(0, 0, 0, .48f));
            consoleRect.width = Mathf.Min(790, width - 20);
            consoleRect.height = Mathf.Min(470, height - 20);
            consoleRect.x = Mathf.Clamp(consoleRect.x, 0, width - consoleRect.width);
            consoleRect.y = Mathf.Clamp(consoleRect.y, 0, height - consoleRect.height);
            consoleRect = GUI.ModalWindow(506, consoleRect, DrawConsoleWindow, GUIContent.none, GUIStyle.none);
            GUI.depth = 0;
        }

        void DrawConsoleWindow(int id)
        {
            float w = consoleRect.width, h = consoleRect.height;
            Rect(0, 0, w, h, Ink);
            Rect(0, 0, w, 40, new Color(.18f, .23f, .22f));
            Rect(0, 0, 5, 40, Orange);
            Text(18, 8, w - 75, 28, "DEV CONSOLE   /   F5 TO CLOSE", 19, Cream, true);
            if (Button(w - 39, 6, 32, 28, "×", size: 17)) ToggleConsole();
            GUI.DragWindow(new Rect(0, 0, w - 44, 40));

            float logHeight = h - 144;
            consoleScroll = GUI.BeginScrollView(new Rect(13, 49, w - 26, logHeight), consoleScroll,
                new Rect(0, 0, w - 47, Mathf.Max(logHeight, consoleLines.Count * 22 + 8)));
            for (int i = 0; i < consoleLines.Count; i++)
                Text(4, 4 + i * 22, w - 55, 21, consoleLines[i], 13,
                    consoleLines[i].StartsWith("[Error") || consoleLines[i].StartsWith("[Exception") ? Orange : Cream);
            GUI.EndScrollView();

            var suggestions = Suggestions(completionSeed ?? consoleInput);
            string hint = suggestions.Count == 0 ? "Enter to run   •   ↑/↓ history   •   Tab autocomplete" :
                "TAB  " + string.Join("     ", suggestions.GetRange(0, Mathf.Min(4, suggestions.Count))) +
                (suggestions.Count > 4 ? "   … (" + suggestions.Count + ")" : "");
            Text(18, h - 88, w - 36, 20, hint, 12, Lime);
            Rect(13, h - 62, w - 26, 43, Panel);
            Text(22, h - 52, 18, 25, ">", 18, Orange, true);
            var e = Event.current;
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                { RunConsoleLine(); e.Use(); }
                else if (e.keyCode == KeyCode.Tab)
                { CompleteConsole((e.modifiers & EventModifiers.Shift) != 0); e.Use(); }
                else if (e.keyCode == KeyCode.UpArrow || e.keyCode == KeyCode.DownArrow)
                { BrowseConsoleHistory(e.keyCode == KeyCode.UpArrow ? 1 : -1); e.Use(); }
                else if (e.keyCode == KeyCode.Escape)
                { ToggleConsole(); e.Use(); }
            }
            GUI.SetNextControlName("DevCommand");
            string edited = GUI.TextField(new Rect(45, h - 56, w - 72, 32), consoleInput,
                new GUIStyle(GUI.skin.textField) { fontSize = 18, normal = { textColor = Cream }, focused = { textColor = Cream } });
            if (edited != consoleInput) { consoleInput = edited; completionSeed = null; completionIndex = -1; historyIndex = -1; }
            if (consoleFocus) { GUI.FocusControl("DevCommand"); consoleFocus = false; }
        }

        List<string> Suggestions(string prefix)
        {
            var result = new List<string>();
            prefix = prefix.TrimStart().ToLowerInvariant();
            int space = prefix.IndexOf(' ');
            if (space < 0)
            {
                foreach (string command in consoleCommands)
                    if (command.StartsWith(prefix, StringComparison.Ordinal)) result.Add(command);
                return result;
            }
            string verb = prefix.Substring(0, space), arg = prefix.Substring(space + 1).TrimStart();
            if (arg.Contains(" ")) return result;
            if (verb == "weapon") foreach (string weapon in WeaponRules.GarageIds) AddSuggestion(result, verb, weapon, arg);
            if (verb == "field") foreach (var weapon in ContentCatalog.Weapons)
                if (weapon && !WeaponRules.GarageWeapon(weapon.id)) AddSuggestion(result, verb, weapon.id, arg);
            if (verb == "car") foreach (var car in ContentCatalog.Vehicles) if (car) AddSuggestion(result, verb, car.id, arg);
            if (verb == "unlock") foreach (string option in new[] { "cars", "weapons", "missions", "all" }) AddSuggestion(result, verb, option, arg);
            if (verb == "invulnerable") foreach (string option in new[] { "on", "off" }) AddSuggestion(result, verb, option, arg);
            return result;
        }

        static void AddSuggestion(List<string> list, string verb, string option, string prefix)
        { if (option.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) list.Add(verb + " " + option); }

        void CompleteConsole(bool backwards)
        {
            if (completionSeed == null) { completionSeed = consoleInput; completionIndex = backwards ? 0 : -1; }
            var options = Suggestions(completionSeed);
            if (options.Count == 0) { completionSeed = null; return; }
            completionIndex = (completionIndex + (backwards ? options.Count - 1 : 1)) % options.Count;
            consoleInput = options[completionIndex];
            GUI.FocusControl("DevCommand");
        }

        void BrowseConsoleHistory(int direction)
        {
            if (consoleHistory.Count == 0) return;
            historyIndex = Mathf.Clamp(historyIndex + direction, -1, consoleHistory.Count - 1);
            consoleInput = historyIndex < 0 ? "" : consoleHistory[consoleHistory.Count - 1 - historyIndex];
            completionSeed = null;
        }

        void RunConsoleLine()
        {
            string line = consoleInput.Trim();
            consoleInput = ""; completionSeed = null; completionIndex = -1; historyIndex = -1;
            if (line.Length == 0) return;
            WriteConsole("> " + line);
            consoleHistory.Add(line);
            if (consoleHistory.Count > 40) consoleHistory.RemoveAt(0);
            string[] words = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string command = words[0].ToLowerInvariant();
            string arg = words.Length > 1 ? words[1].ToLowerInvariant() : "";
            if (command == "help")
            {
                WriteConsole("help, clear, status, garage, repair, nitro");
                WriteConsole("money <amount>, scrap <amount>  •  add a positive amount");
                WriteConsole("unlock cars|weapons|missions|all  •  unlock progression");
                WriteConsole("weapon <id>, field <id>, car <id>  •  Tab lists IDs");
                WriteConsole("invulnerable on|off  •  ignore all player damage this sortie");
                WriteConsole("cooplog  •  save the previous 15 seconds from every co-op player; cooplog path shows the folder");
                WriteConsole("car changes the selected car in the garage; field equips a drop weapon during play.");
                return;
            }
            if (command == "clear") { consoleLines.Clear(); return; }
            if (command == "status")
            {
                WriteConsole("State " + game.State + "  •  $" + game.Save.money + "  •  " + game.Save.salvage + " scrap  •  mission " + game.Save.unlockedMission);
                if (game.Player) WriteConsole("Car " + game.Player.Definition.id + "  •  garage weapon " + game.Player.Weapons.GarageWeapon.id + "  •  invulnerable " + (game.Player.Damage.Invulnerable ? "on" : "off"));
                return;
            }
            if (command == "cooplog")
            {
                var coop = CoopSession.Instance;
                if (arg == "path") WriteConsole(coop && !string.IsNullOrEmpty(coop.LastTracePath)
                    ? "Co-op trace folder: " + coop.LastTracePath : "No co-op trace has been saved in this session.");
                else if (words.Length == 1) WriteConsole(coop ? coop.RequestDiagnosticCapture() : "Co-op is unavailable.");
                else WriteConsole("Use cooplog or cooplog path.");
                return;
            }
            if (CoopSession.IsRemoteClient) { WriteConsole("Only the host can run dev commands in co-op."); return; }
            if (words.Length > 2) { WriteConsole("Too many arguments. Type help for syntax."); return; }
            if (command == "invulnerable")
            {
                if (!game.Player || !game.IsPlaying) { WriteConsole("Start a sortie first."); return; }
                if (arg != "on" && arg != "off") { WriteConsole("Use invulnerable on or invulnerable off."); return; }
                game.Player.Damage.Invulnerable = arg == "on";
                WriteConsole("Player invulnerability " + arg + "."); return;
            }
            if (command == "money" || command == "scrap")
            {
                if (!int.TryParse(arg, NumberStyles.None, CultureInfo.InvariantCulture, out int amount) || amount <= 0)
                { WriteConsole("Enter a positive whole number."); return; }
                if (command == "money") game.Save.money = (int)Math.Min(100000000L, (long)game.Save.money + amount);
                else game.Save.salvage = (int)Math.Min(1000000L, (long)game.Save.salvage + amount);
                SaveSystem.Save(game.Save);
                WriteConsole(command + " now " + (command == "money" ? game.Save.money : game.Save.salvage));
                return;
            }
            if (command == "unlock")
            {
                if (arg != "cars" && arg != "weapons" && arg != "missions" && arg != "all")
                { WriteConsole("Use unlock cars, weapons, missions, or all."); return; }
                if (arg == "cars" || arg == "all") foreach (var car in ContentCatalog.Vehicles)
                    if (car && !game.Save.ownedVehicles.Contains(car.id)) game.Save.ownedVehicles.Add(car.id);
                if (arg == "weapons" || arg == "all") foreach (string id in WeaponRules.GarageIds)
                    if (!game.Save.ownedWeapons.Contains(id)) game.Save.ownedWeapons.Add(id);
                if (arg == "missions" || arg == "all") game.Save.unlockedMission = ContentCatalog.Missions.Length - 1;
                SaveSystem.Save(game.Save); WriteConsole("Unlocked " + arg + "."); return;
            }
            if (command == "weapon")
            {
                var weapon = WeaponRules.Find(arg);
                if (!weapon || !WeaponRules.GarageWeapon(arg)) { WriteConsole("Unknown garage weapon. Type weapon and press Tab."); return; }
                if (!game.Save.ownedWeapons.Contains(arg)) game.Save.ownedWeapons.Add(arg);
                game.Save.selectedWeapon = arg;
                var loadout=GarageManager.Loadout(game.Save,ContentCatalog.Vehicles[game.Save.selectedVehicle].id);
                if(loadout!=null)loadout.weapon=arg;
                selectedWeapon = Array.FindIndex(garageWeapons, i => ContentCatalog.Weapons[i].id == arg);
                game.Player?.Weapons?.ConfigurePlayerPrimary(arg);
                if (game.State == GameState.Garage) game.RefreshGarageVehicle();
                SaveSystem.Save(game.Save); WriteConsole("Garage weapon: " + weapon.displayName); return;
            }
            if (command == "field")
            {
                var weapon = WeaponRules.Find(arg);
                if (!weapon || WeaponRules.GarageWeapon(arg)) { WriteConsole("Unknown field weapon. Type field and press Tab."); return; }
                if (!game.IsPlaying || !game.Player) { WriteConsole("Field weapons require an active sortie."); return; }
                game.Player.Weapons.EquipField(weapon, WeaponRules.MaxAmmo(arg));
                WriteConsole("Field weapon: " + weapon.displayName); return;
            }
            if (command == "car")
            {
                if (game.State != GameState.Garage) { WriteConsole("Select cars in the garage."); return; }
                int index = Array.FindIndex(ContentCatalog.Vehicles, car => car && car.id.Equals(arg, StringComparison.OrdinalIgnoreCase));
                if (index < 0) { WriteConsole("Unknown car. Type car and press Tab."); return; }
                string id = ContentCatalog.Vehicles[index].id;
                if (!game.Save.ownedVehicles.Contains(id)) game.Save.ownedVehicles.Add(id);
                game.Save.selectedVehicle = index; game.RefreshGarageVehicle();
                WriteConsole("Car: " + ContentCatalog.Vehicles[index].displayName); return;
            }
            if (command == "repair" || command == "nitro")
            {
                if (!game.Player || !game.IsPlaying) { WriteConsole("Start a sortie first."); return; }
                if (command == "repair") game.Player.Repair(game.Player.Damage.MaxHealth);
                else game.Player.RefillNitro(1);
                WriteConsole(command == "repair" ? "Vehicle repaired." : "Nitro refilled."); return;
            }
            if (command == "garage") { game.ReturnToGarage(); WriteConsole("Returned to garage."); return; }
            WriteConsole("Unknown command. Type help.");
        }
    }
}
