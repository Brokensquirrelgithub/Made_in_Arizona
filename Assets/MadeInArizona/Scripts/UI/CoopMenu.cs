using UnityEngine;

namespace MadeInArizona
{
    public sealed partial class GameUI
    {
        string coopCodeEntry = "";
        bool coopPage;
        void LeaveCoop()
        {
            var coop = CoopSession.Instance;
            if (!coop) return;
            if (coop.IsHost) coop.RequestEndSession();
            else coop.Leave();
        }

        void DrawCoopPanel()
        {
            var coop = CoopSession.Instance;
            if (!coop) return;
            float x = coopPage ? (width - 420) * .5f : Mathf.Max(width * .08f + 760, width - 455);
            float y = coopPage ? Mathf.Max(35, (height - 500) * .5f) : height * .13f + 179;
            float w = Mathf.Min(420, width - x - 20);
            if (w < 300) return;
            Rect(x, y, w, 440, Ink);
            Tag(x + 20, y + 22, "CO-OP / UP TO 4 PLAYERS", Orange);
            Text(x + 20, y + 58, w - 40, 70, "One PC hosts the world. Friends join through Unity Relay with a code.", 17, Cream);
            if (coop.IsHost)
            {
                Text(x + 20, y + 144, w - 40, 36, "JOIN CODE  " + coop.JoinCode, 27, Lime, true);
                if (Button(x + 20, y + 196, w - 40, 45, "COPY JOIN CODE")) GUIUtility.systemCopyBuffer = coop.JoinCode;
                Text(x + 20, y + 265, w - 40, 52, coop.PlayerCount + " / " + CoopSession.MaxPlayers + " players connected", 20, Cream, true);
            }
            else if (coop.IsClient)
            {
                Text(x + 20, y + 152, w - 40, 90, "Waiting for the host to start a campaign or combat trial.", 20, Cream);
            }
            else
            {
                if (Button(x + 20, y + 140, w - 40, 49, "HOST CO-OP", true, !coop.Busy)) coop.Host();
                Text(x + 20, y + 207, w - 40, 27, "FRIEND'S JOIN CODE", 14, Muted, true);
                coopCodeEntry = GUI.TextField(new Rect(x + 20, y + 238, w - 40, 40), coopCodeEntry, 16).ToUpperInvariant();
                if (Button(x + 20, y + 293, w - 40, 48, "JOIN HOST", false, !coop.Busy)) coop.Join(coopCodeEntry);
            }
            if (coop.IsHost || coop.IsClient)
                if (Button(x + 20, y + 340, w - 40, 41, "LEAVE CO-OP")) LeaveCoop();
            Text(x + 20, y + 393, w - 40, 40, coop.Status, 13, Muted);
            if (coopPage && Button(x, y + 452, w, 40, "BACK TO MAIN MENU")) coopPage = false;
        }

        void DrawCoopBadge()
        {
            var coop = CoopSession.Instance;
            if (!coop || (!coop.IsHost && !coop.IsClient)) return;
            float x = width * .5f - 180;
            Rect(x, 18, 360, 42, Ink);
            bool spectating = game.Player && game.Player.Damage && game.Player.Damage.IsDead;
            string label = spectating ? "CO-OP  •  SPECTATING TEAMMATE"
                : coop.IsHost ? "CO-OP  " + coop.PlayerCount + "/4  •  CODE " + coop.JoinCode
                : coop.HostPaused ? "CO-OP  •  HOST PAUSED" : "CO-OP  •  HOST WORLD";
            Text(x + 8, 27, 344, 24, label, 15, Lime, true, TextAnchor.MiddleCenter);
        }

        void DrawCoopEndPrompt()
        {
            var coop = CoopSession.Instance;
            if (!coop) return;
            Rect(0, 0, width, height, new Color(.02f, .04f, .045f, .96f));
            float w = 620, h = 335, x = (width - w) * .5f, y = (height - h) * .5f;
            Rect(x, y, w, h, Ink);
            Tag(x + 28, y + 25, "CO-OP PLAYTEST / END SESSION", Orange);
            Text(x + 28, y + 65, w - 56, 75, coop.EndingSession ? "Closing the co-op session…" :
                "Make this session's tuning the new default?", 26, Cream, true);
            Text(x + 28, y + 145, w - 56, 65,
                "Your choice applies to every player. Discard restores each player's car, weapon and local tuning from before the session.", 16, Muted);
            if (coop.EndingSession) return;
            if (Button(x + 28, y + 232, 170, 52, "SAVE DEFAULTS", true)) coop.FinishEndSession(true);
            if (Button(x + 210, y + 232, 170, 52, "DISCARD CHANGES")) coop.FinishEndSession(false);
            if (Button(x + 392, y + 232, 200, 52, "KEEP TESTING")) coop.CancelEndSession();
        }
    }
}
