using UnityEngine;
using UnityEngine.InputSystem;
namespace MadeInArizona
{
    /// <summary>Versions &amp; Updates: lists published playtest builds and switches the installed game to any of them.</summary>
    public sealed partial class GameUI
    {
        bool updatesMenu;
        int updateSelection = -1;
        Vector2 updateScroll;
        Vector2 updateDetailsScroll;
        bool StartupUpdateOfferVisible => !updatesMenu && GameUpdater.Instance && GameUpdater.Instance.StartupOffer != null &&
            game != null && (game.State == GameState.MainMenu || game.State == GameState.Garage);

        void AcceptStartupUpdateOffer()
        {
            var updater = GameUpdater.Instance;
            if (!updater || updater.StartupOffer == null) return;
            var latest = updater.StartupOffer;
            updater.DismissStartupOffer();
            OpenUpdates(); updateSelection = 0; updateDetailsScroll = Vector2.zero;
            if (updater.InstallBlocker == null) updater.Download(latest);
        }

        void DrawStartupUpdateOffer()
        {
            var updater = GameUpdater.Instance;
            var latest = updater.StartupOffer;
            Rect(0, 0, width, height, new Color(.025f, .04f, .045f, .98f));
            float w = Mathf.Min(650, width - 40), h = Mathf.Min(350, height - 40);
            float x = (width - w) * .5f, y = (height - h) * .5f;
            Rect(x, y, w, h, Ink);
            Rect(x, y, 5, h, Lime);
            Tag(x + 28, y + 24, "VERSION UPDATE", Lime);
            Text(x + 28, y + 55, w - 56, 47, "A newer build is available", 29, Cream, true);
            Text(x + 28, y + 108, w - 56, 30, updater.CurrentLabel + "  →  BUILD " + latest.ShortCommit, 15, Muted);
            string summary = FirstLine(latest.message);
            if (string.IsNullOrEmpty(summary)) summary = latest.title;
            Text(x + 28, y + 151, w - 56, 63, Clip(summary, 130), 17, Cream);
            string blocker = updater.InstallBlocker;
            if (blocker != null) Text(x + 28, y + h - 111, w - 56, 35, blocker, 13, Orange);
            float buttonWidth = (w - 68) * .5f;
            if (Button(x + 28, y + h - 66, buttonWidth, 44, blocker == null ? "UPDATE TO LATEST  /  A" : "VIEW UPDATE", true, true, 15))
                AcceptStartupUpdateOffer();
            if (Button(x + 40 + buttonWidth, y + h - 66, buttonWidth, 44, "KEEP PLAYING  /  B", false, true, 15))
                updater.DismissStartupOffer();
        }

        public void OpenUpdates()
        {
            updatesMenu = true; settings = false; devMenu = false; worldMap = false; updateSelection = -1;
            var updater = GameUpdater.Instance;
            if (updater && !updater.Busy && updater.State != GameUpdater.Phase.Downloaded &&
                (updater.State != GameUpdater.Phase.Ready || (System.DateTime.Now - updater.LastChecked).TotalMinutes > 2)) updater.Check();
        }
        void UpdatesInput()
        {
            if (updatesMenu && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) updatesMenu = false;
        }
        /// <summary>Main-menu entry point, with a banner when a newer build or a switch result is waiting.</summary>
        void DrawUpdateEntry(float x, float y, float w)
        {
            var updater = GameUpdater.Instance; if (!updater) return;
            bool fresh = updater.UpdateAvailable;
            if (Button(x, y, 300, 44, fresh ? "NEW BUILD AVAILABLE" : "VERSIONS & UPDATES", fresh)) OpenUpdates();
            string line = !string.IsNullOrEmpty(updater.Notice) ? updater.Notice :
                fresh ? "Build " + updater.Builds[0].ShortCommit + " is out: " + FirstLine(updater.Builds[0].message) : updater.CurrentLabel;
            Text(x + 318, y + 3, w - 318, 42, line, 13, !string.IsNullOrEmpty(updater.Notice) || fresh ? Lime : Muted);
        }

        void DrawUpdates()
        {
            var updater = GameUpdater.Instance;
            Rect(0, 0, width, height, new Color(.025f, .04f, .045f, .96f));
            float x = Mathf.Max(30, (width - 1180) / 2), y = 40, w = Mathf.Min(1180, width - 60), h = height - 80;
            Rect(x, y, w, h, Ink);
            Tag(x + 30, y + 26, "PLAYTEST BUILDS  /  " + GameUpdater.Owner + "/" + GameUpdater.Repository, Orange);
            Text(x + 30, y + 52, w - 60, 50, "VERSIONS & UPDATES", 36, Cream, true);
            if (Button(x + w - 200, y + 26, 170, 40, "CLOSE  /  ESC")) { updatesMenu = false; return; }
            if (!updater) { Text(x + 30, y + 120, w - 60, 40, "The updater is not running.", 18, Orange); return; }

            Text(x + 30, y + 110, w - 60, 26, "INSTALLED: " + updater.CurrentLabel, 16, Lime, true);
            Text(x + 30, y + 138, w - 250, 44, updater.Status, 15, updater.State == GameUpdater.Phase.Failed ? Orange : Cream);
            if (!string.IsNullOrEmpty(updater.Notice)) Text(x + 30, y + 176, w - 60, 40, updater.Notice, 14, Lime);
            if (Button(x + w - 200, y + 110, 170, 36, updater.State == GameUpdater.Phase.Checking ? "CHECKING…" : "CHECK AGAIN", false, !updater.Busy)) { updateSelection = -1; updater.Check(); }

            // Build list.
            float listX = x + 30, listY = y + 222, listW = w * .52f, listH = h - 252;
            Rect(listX, listY, listW, listH, Panel);
            var builds = updater.Builds;
            if (builds.Count == 0)
                Text(listX + 20, listY + 20, listW - 40, 120, updater.State == GameUpdater.Phase.Checking ? "Looking for builds…" :
                    "No builds with version switching have been published yet. Builds appear here once they are released on GitHub (see Docs/UPDATES.md).", 15, Muted);
            updateScroll = GUI.BeginScrollView(new Rect(listX + 10, listY + 10, listW - 20, listH - 20), updateScroll, new Rect(0, 0, listW - 40, builds.Count * 78));
            for (int i = 0; i < builds.Count; i++)
            {
                var build = builds[i];
                string marker = updater.IsCurrent(build) ? "   •   INSTALLED" : i == 0 ? "   •   NEWEST" : "";
                string label = build.ShortCommit + "   " + GameUpdater.ShortDate(build.published) + marker + "\n" + Clip(FirstLine(build.message.Length > 0 ? build.message : build.title), 62);
                if (Button(0, i * 78, listW - 44, 68, label, updateSelection == i))
                { updateSelection = i; updateDetailsScroll = Vector2.zero; }
            }
            GUI.EndScrollView();

            // Details and actions for the selected build.
            float dx = listX + listW + 24, dw = x + w - 30 - dx;
            if (updateSelection < 0 || updateSelection >= builds.Count)
            {
                Text(dx, listY, dw, 200, "Pick any build to switch to it: newer builds bring the latest changes, older ones let you compare. " +
                    "Only builds made after version switching was added are listed, so you can always switch back.\n\nSaves, settings and garage progress are kept.", 16, Muted);
                return;
            }
            var selected = builds[updateSelection];
            Text(dx, listY, dw, 34, "BUILD " + selected.ShortCommit, 26, Cream, true);
            Text(dx, listY + 38, dw, 22, "Published " + GameUpdater.ShortDate(selected.published) + (selected.size > 0 ? "   •   " + GameUpdater.Megabytes(selected.size) : ""), 14, Muted);
            float by = listY + listH - 160;
            string notes = selected.message.Length > 0 ? selected.message : selected.title;
            float notesY = listY + 70, notesHeight = Mathf.Max(44, by - notesY - 12);
            float notesWidth = dw - 26;
            float contentHeight = Mathf.Max(notesHeight, Style(15, Cream).CalcHeight(new GUIContent(notes), notesWidth) + 12);
            updateDetailsScroll = GUI.BeginScrollView(new Rect(dx, notesY, dw, notesHeight),
                updateDetailsScroll, new Rect(0, 0, dw - 16, contentHeight));
            Text(0, 0, notesWidth, contentHeight, notes, 15, Cream);
            GUI.EndScrollView();
            string blocker = updater.InstallBlocker;
            if (updater.IsCurrent(selected)) { Text(dx, by + 40, dw, 40, "This is the build you are running.", 16, Lime, true); return; }
            if (updater.State == GameUpdater.Phase.Downloading && updater.Target == selected)
            {
                Bar(dx, by + 20, dw, 10, updater.Progress, Lime);
                Text(dx, by + 36, dw, 24, Mathf.RoundToInt(updater.Progress * 100) + "%   of   " + GameUpdater.Megabytes(selected.size), 14, Cream);
                if (Button(dx, by + 70, dw, 50, "CANCEL DOWNLOAD")) updater.Cancel();
                return;
            }
            if (updater.State == GameUpdater.Phase.Verifying && updater.Target == selected) { Text(dx, by + 40, dw, 30, "Verifying…", 16, Cream, true); return; }
            if (blocker != null) Text(dx, by - 40, dw, 60, blocker, 14, Orange);
            if (updater.IsDownloaded(selected))
            {
                if (Button(dx, by + 70, dw, 56, "RESTART & SWITCH TO " + selected.ShortCommit, true, blocker == null)) updater.InstallAndRestart();
                Text(dx, by + 20, dw, 40, "Downloaded. The game closes, swaps its files and reopens on this build.", 14, Muted);
                return;
            }
            bool newer = updater.CurrentIndex > 0 && updateSelection < updater.CurrentIndex;
            if (Button(dx, by + 70, dw, 56, (newer ? "DOWNLOAD UPDATE " : "DOWNLOAD & SWITCH TO ") + selected.ShortCommit, true, !updater.Busy && !string.IsNullOrEmpty(selected.assetUrl)))
                updater.Download(selected);
        }
        static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            int end = text.IndexOf('\n');
            return (end >= 0 ? text.Substring(0, end) : text).Trim();
        }
        static string Clip(string text, int length) => text.Length <= length ? text : text.Substring(0, length - 1) + "…";
    }
}
