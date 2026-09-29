using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace MadeInArizona
{
    /// <summary>Identity of an installed build, written into StreamingAssets by the build script.</summary>
    [Serializable]
    public sealed class BuildStamp
    {
        public string commit, date, message, platform, builtAt;
        public int protocol;
        public string ShortCommit => string.IsNullOrEmpty(commit) ? "unknown" : commit.Substring(0, Mathf.Min(7, commit.Length));
    }

    /// <summary>
    /// Checks the public GitHub repository for published playtest builds and switches the installed game to any of
    /// them: newer or older. Builds are GitHub Releases tagged <c>build-&lt;sha&gt;</c> whose notes declare
    /// <c>mia-updater: N</c>; only builds that include this updater (N ≥ <see cref="MinimumProtocol"/>) are offered, so a
    /// tester can never switch to a build that could not switch back. The download is verified, then a small
    /// platform script waits for the game to quit, swaps the files in place and relaunches it. Saves and settings live
    /// outside the install folder and are untouched.
    /// </summary>
    public sealed class GameUpdater : MonoBehaviour
    {
        public const string Owner = "Brokensquirrelgithub", Repository = "Made_in_Arizona";
        /// <summary>Updater protocol written into every build stamp and release; bump when switching becomes incompatible.</summary>
        public const int Protocol = 1;
        /// <summary>Oldest protocol the menu offers. Builds from before the updater never have a qualifying release.</summary>
        public const int MinimumProtocol = 1;
        public const string StampFile = "build.json", TagPrefix = "build-", ProtocolKey = "mia-updater:", CommitKey = "commit:";
        public const string WindowsAsset = "MadeInArizona-Windows.zip", MacAsset = "MadeInArizona-macOS.zip";
        const string PendingKey = "mia.update.pending";

        public enum Phase { Idle, Checking, Ready, Failed, Downloading, Verifying, Downloaded, Installing }

        public sealed class Build
        {
            public string tag, commit, title, message, assetUrl, digest;
            public long size;
            public DateTime published;
            public string ShortCommit => string.IsNullOrEmpty(commit) ? tag : commit.Substring(0, Mathf.Min(7, commit.Length));
        }

        public static GameUpdater Instance { get; private set; }
        public BuildStamp Current { get; private set; }
        public readonly List<Build> Builds = new List<Build>();
        public Phase State { get; private set; }
        public string Status { get; private set; } = "Not checked yet.";
        /// <summary>One-off message about the last switch (success or failure), shown in the menus.</summary>
        public string Notice { get; private set; }
        public float Progress { get; private set; }
        public Build Target { get; private set; }
        public DateTime LastChecked { get; private set; }
        string downloadedZip;
        UnityWebRequest activeRequest;

        public static string UpdatesFolder => Path.Combine(Application.persistentDataPath, "Updates");
        public static string LogPath => Path.Combine(UpdatesFolder, "update.log");
        static string ApiUrl => "https://api.github.com/repos/" + Owner + "/" + Repository + "/releases?per_page=50";
        public static string ReleasesPage => "https://github.com/" + Owner + "/" + Repository + "/releases";
        static string DownloadPrefix => ReleasesPage + "/download/";

        void Awake()
        {
            Instance = this;
            Current = LoadStamp();
            string pending = PlayerPrefs.GetString(PendingKey, "");
            if (!string.IsNullOrEmpty(pending))
            {
                PlayerPrefs.DeleteKey(PendingKey); PlayerPrefs.Save();
                bool failed = Array.IndexOf(Environment.GetCommandLineArgs(), "-miaUpdateFailed") >= 0;
                bool switched = Current != null && !string.IsNullOrEmpty(Current.commit) && Current.commit.StartsWith(pending, StringComparison.OrdinalIgnoreCase);
                Notice = switched && !failed ? "Switched to build " + Current.ShortCommit + "." :
                    "The last switch did not finish, so this build is unchanged. Details: " + LogPath;
            }
        }
        void Start()
        {
            // Automated test runs stay offline.
            if (!SmokeTestRunner.Active) Check();
        }
        void OnDestroy() { if (Instance == this) Instance = null; activeRequest?.Abort(); }

        static BuildStamp LoadStamp()
        {
            try
            {
                string path = Path.Combine(Application.streamingAssetsPath, StampFile);
                if (!File.Exists(path)) return null;
                var stamp = JsonUtility.FromJson<BuildStamp>(File.ReadAllText(path));
                return stamp != null && !string.IsNullOrEmpty(stamp.commit) ? stamp : null;
            }
            catch (Exception exception) { Debug.LogWarning("MIA_UPDATER could not read build stamp: " + exception.Message); return null; }
        }

        public string CurrentLabel => Current != null ? "BUILD " + Current.ShortCommit + (string.IsNullOrEmpty(Current.date) ? "" : " • " + ShortDate(Current.date)) :
            Application.isEditor ? "UNITY EDITOR (UNPUBLISHED)" : "LOCAL BUILD (UNPUBLISHED)";

        static string PlatformAsset
        {
            get
            {
                switch (Application.platform)
                {
                    case RuntimePlatform.WindowsPlayer: case RuntimePlatform.WindowsEditor: return WindowsAsset;
                    case RuntimePlatform.OSXPlayer: case RuntimePlatform.OSXEditor: return MacAsset;
                    default: return null;
                }
            }
        }

        public bool IsCurrent(Build build) =>
            build != null && Current != null && !string.IsNullOrEmpty(Current.commit) && !string.IsNullOrEmpty(build.commit) &&
            (Current.commit.StartsWith(build.commit, StringComparison.OrdinalIgnoreCase) || build.commit.StartsWith(Current.commit, StringComparison.OrdinalIgnoreCase));
        public int CurrentIndex { get { for (int i = 0; i < Builds.Count; i++) if (IsCurrent(Builds[i])) return i; return -1; } }
        /// <summary>A published build newer than the one installed.</summary>
        public bool UpdateAvailable
        {
            get
            {
                if (Builds.Count == 0 || IsCurrent(Builds[0])) return false;
                int current = CurrentIndex;
                if (current > 0) return true;
                return Current != null && DateTime.TryParse(Current.builtAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var built) && Builds[0].published > built.ToUniversalTime();
            }
        }
        public bool Busy => State == Phase.Checking || State == Phase.Downloading || State == Phase.Verifying || State == Phase.Installing;

        /// <summary>Why this copy of the game cannot switch builds by itself, or null when it can (measured once per launch).</summary>
        public string InstallBlocker
        {
            get
            {
                if (!blockerMeasured) { blocker = MeasureInstallBlocker(); blockerMeasured = true; }
                return blocker;
            }
        }
        string blocker; bool blockerMeasured;
        static string MeasureInstallBlocker()
        {
            if (Application.isEditor) return "Builds switch in the installed game. The editor only lists them.";
            if (PlatformAsset == null) return "Automatic switching supports the Windows and macOS builds.";
            string root = InstallRoot;
            if (Application.platform == RuntimePlatform.OSXPlayer && root.Contains("/AppTranslocation/"))
                return "macOS is running a read-only copy. Move Made in Arizona.app into Applications (or any folder) and reopen it.";
            string parent = Application.platform == RuntimePlatform.OSXPlayer ? Path.GetDirectoryName(root) : root;
            if (!Writable(parent)) return "The game folder is read-only. Move the game somewhere you can write to (not Program Files).";
            return null;
        }
        /// <summary>The .app bundle on macOS; the folder holding the executable on Windows.</summary>
        static string InstallRoot => Application.platform == RuntimePlatform.OSXPlayer
            ? Path.GetFullPath(Path.Combine(Application.dataPath, ".."))
            : Path.GetDirectoryName(Path.GetFullPath(Application.dataPath));
        static bool Writable(string folder)
        {
            try
            {
                string probe = Path.Combine(folder, ".mia-write-test");
                File.WriteAllText(probe, ""); File.Delete(probe); return true;
            }
            catch { return false; }
        }

        // ---------------------------------------------------------------- listing

        public void Check()
        {
            if (Busy) return;
            StartCoroutine(CheckRoutine());
        }
        IEnumerator CheckRoutine()
        {
            State = Phase.Checking; Status = "Checking GitHub for published builds…";
            using (var request = UnityWebRequest.Get(ApiUrl))
            {
                request.SetRequestHeader("Accept", "application/vnd.github+json");
                request.timeout = 20;
                activeRequest = request;
                yield return request.SendWebRequest();
                activeRequest = null;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    State = Phase.Failed;
                    Status = request.responseCode == 403 || request.responseCode == 429
                        ? "GitHub is rate-limiting this network. Try again in a few minutes."
                        : "Could not reach GitHub (" + (string.IsNullOrEmpty(request.error) ? "no connection" : request.error) + ").";
                    yield break;
                }
                try { Parse(request.downloadHandler.text); }
                catch (Exception exception)
                {
                    State = Phase.Failed; Status = "GitHub's answer could not be read: " + exception.Message;
                    yield break;
                }
            }
            LastChecked = DateTime.Now;
            State = Phase.Ready;
            Status = Builds.Count == 0 ? "No published builds with version switching yet." :
                UpdateAvailable ? "A newer build is available." :
                CurrentIndex == 0 ? "You are on the newest build." : Builds.Count + " published builds.";
        }

        [Serializable] sealed class ReleaseList { public Release[] items; }
        [Serializable] sealed class Release { public string tag_name, name, body, published_at, target_commitish; public bool draft; public Asset[] assets; }
        [Serializable] sealed class Asset { public string name, browser_download_url, digest; public long size; }

        void Parse(string json)
        {
            var list = JsonUtility.FromJson<ReleaseList>("{\"items\":" + json + "}");
            Builds.Clear();
            string wanted = PlatformAsset;
            if (list == null || list.items == null) return;
            foreach (var release in list.items)
            {
                if (release == null || release.draft || string.IsNullOrEmpty(release.tag_name) || !release.tag_name.StartsWith(TagPrefix, StringComparison.Ordinal)) continue;
                ReadNotes(release.body, out int protocol, out string commit, out string message);
                // Builds from before version switching never declare a protocol, so they are never offered.
                if (protocol < MinimumProtocol) continue;
                Asset asset = null;
                if (release.assets != null) foreach (var candidate in release.assets) if (candidate != null && candidate.name == wanted) asset = candidate;
                if (wanted != null && asset == null) continue;
                // Only ever download this repository's own release files.
                if (asset != null && (asset.browser_download_url == null || !asset.browser_download_url.StartsWith(DownloadPrefix, StringComparison.OrdinalIgnoreCase))) continue;
                DateTime.TryParse(release.published_at, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var published);
                Builds.Add(new Build
                {
                    tag = release.tag_name, commit = string.IsNullOrEmpty(commit) ? release.tag_name.Substring(TagPrefix.Length) : commit,
                    title = string.IsNullOrEmpty(release.name) ? release.tag_name : release.name, message = message,
                    assetUrl = asset != null ? asset.browser_download_url : null, size = asset != null ? asset.size : 0,
                    digest = asset != null ? asset.digest : null, published = published
                });
            }
            Builds.Sort((a, b) => b.published.CompareTo(a.published));
        }
        /// <summary>Reads the machine lines (<c>mia-updater: 1</c>, <c>commit: sha</c>) and the human summary from release notes.</summary>
        public static void ReadNotes(string body, out int protocol, out string commit, out string message)
        {
            protocol = 0; commit = null;
            var summary = new StringBuilder();
            foreach (string raw in (body ?? "").Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith(ProtocolKey, StringComparison.OrdinalIgnoreCase)) { int.TryParse(line.Substring(ProtocolKey.Length).Trim(), out protocol); continue; }
                if (line.StartsWith(CommitKey, StringComparison.OrdinalIgnoreCase)) { commit = line.Substring(CommitKey.Length).Trim(); continue; }
                if (line.Length > 0 && summary.Length < 600) summary.AppendLine(line);
            }
            message = summary.ToString().Trim();
        }

        // ---------------------------------------------------------------- download

        public bool IsDownloaded(Build build) => State == Phase.Downloaded && Target == build && !string.IsNullOrEmpty(downloadedZip) && File.Exists(downloadedZip);

        public void Download(Build build)
        {
            if (build == null || Busy || string.IsNullOrEmpty(build.assetUrl) || IsCurrent(build)) return;
            StartCoroutine(DownloadRoutine(build));
        }
        public void Cancel()
        {
            if (State != Phase.Downloading) return;
            activeRequest?.Abort();
        }
        IEnumerator DownloadRoutine(Build build)
        {
            Target = build; downloadedZip = null; Progress = 0;
            State = Phase.Downloading; Status = "Downloading build " + build.ShortCommit + "…";
            Directory.CreateDirectory(UpdatesFolder);
            foreach (string stale in Directory.GetFiles(UpdatesFolder, "*.zip")) TryDelete(stale);
            string zip = Path.Combine(UpdatesFolder, build.tag + "-" + PlatformAsset);
            using (var request = new UnityWebRequest(build.assetUrl, UnityWebRequest.kHttpVerbGET))
            {
                request.downloadHandler = new DownloadHandlerFile(zip) { removeFileOnAbort = true };
                request.redirectLimit = 10;
                activeRequest = request;
                var operation = request.SendWebRequest();
                while (!operation.isDone) { Progress = request.downloadProgress; yield return null; }
                activeRequest = null;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    TryDelete(zip);
                    State = Phase.Failed;
                    Status = request.error == "Request aborted" ? "Download cancelled." : "Download failed: " + request.error;
                    yield break;
                }
            }
            Progress = 1;
            State = Phase.Verifying; Status = "Verifying the download…";
            long length = new FileInfo(zip).Length;
            if (build.size > 0 && length != build.size) { Fail(zip, "The download was incomplete (" + length + " of " + build.size + " bytes). Try again."); yield break; }
            if (!string.IsNullOrEmpty(build.digest) && build.digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                var hashing = Task.Run(() => Sha256(zip));
                while (!hashing.IsCompleted) yield return null;
                if (hashing.IsFaulted || !string.Equals(hashing.Result, build.digest.Substring(7), StringComparison.OrdinalIgnoreCase))
                { Fail(zip, "The download did not match GitHub's checksum. Try again."); yield break; }
            }
            downloadedZip = zip;
            State = Phase.Downloaded;
            Status = "Build " + build.ShortCommit + " is ready. Restart to switch.";
        }
        void Fail(string zip, string message) { TryDelete(zip); State = Phase.Failed; Status = message; }
        static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }

        // ---------------------------------------------------------------- install

        /// <summary>Saves, hands the downloaded build to the platform install script and quits. The script relaunches the game.</summary>
        public void InstallAndRestart()
        {
            if (!IsDownloaded(Target) || InstallBlocker != null) return;
            try
            {
                State = Phase.Installing; Status = "Installing build " + Target.ShortCommit + "…";
                if (GameManager.Instance != null && GameManager.Instance.Save != null) SaveSystem.Save(GameManager.Instance.Save);
                PlayerPrefs.SetString(PendingKey, Target.commit); PlayerPrefs.Save();
                File.AppendAllText(LogPath, "[" + DateTime.Now.ToString("s") + "] switching " + (Current != null ? Current.ShortCommit : "unpublished") + " -> " + Target.ShortCommit + Environment.NewLine);
                if (Application.platform == RuntimePlatform.WindowsPlayer) LaunchWindowsInstaller();
                else LaunchMacInstaller();
                Application.Quit();
            }
            catch (Exception exception)
            {
                PlayerPrefs.DeleteKey(PendingKey);
                State = Phase.Failed; Status = "Could not start the installer: " + exception.Message;
                Debug.LogError("MIA_UPDATER " + exception);
            }
        }

        void LaunchWindowsInstaller()
        {
            string exe = Process.GetCurrentProcess().MainModule.FileName;
            string script = Path.Combine(UpdatesFolder, "install-update.ps1");
            File.WriteAllText(script, WindowsInstallScript(LogPath, InstallRoot, Path.GetFileName(exe), downloadedZip, Path.Combine(UpdatesFolder, "staged"),
                Path.GetFileName(Path.GetFullPath(Application.dataPath)), Process.GetCurrentProcess().Id), new UTF8Encoding(true));
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe", Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + script + "\"",
                UseShellExecute = true, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        void LaunchMacInstaller()
        {
            string script = Path.Combine(UpdatesFolder, "install-update.sh");
            File.WriteAllText(script, MacInstallScript(LogPath, InstallRoot, downloadedZip, Path.Combine(UpdatesFolder, "staged"), Process.GetCurrentProcess().Id));
            Process.Start(new ProcessStartInfo { FileName = "/usr/bin/nohup", Arguments = "/bin/bash \"" + script + "\"", UseShellExecute = false, CreateNoWindow = true });
        }

        /// <summary>
        /// PowerShell that waits for the game to exit, unpacks the build, mirrors the game's own data folder (so stale
        /// files go) and copies the remaining files over without deleting anything else in the folder, then relaunches.
        /// </summary>
        public static string WindowsInstallScript(string log, string install, string exeName, string zip, string stage, string dataFolder, int pid)
        {
            var s = new StringBuilder();
            s.AppendLine("$ErrorActionPreference = 'Stop'");
            s.AppendLine("$log = " + Ps(log));
            s.AppendLine("function Log($m) { Add-Content -LiteralPath $log -Value ('[' + (Get-Date -Format s) + '] ' + $m) }");
            s.AppendLine("$install = " + Ps(install) + "; $exe = Join-Path $install " + Ps(exeName) + "; $zip = " + Ps(zip) + "; $stage = " + Ps(stage) + "; $data = " + Ps(dataFolder));
            s.AppendLine("try {");
            s.AppendLine("  Log 'Waiting for the game to close'");
            s.AppendLine("  try { Wait-Process -Id " + pid + " -Timeout 90 -ErrorAction SilentlyContinue } catch { }");
            s.AppendLine("  Start-Sleep -Milliseconds 700");
            s.AppendLine("  if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }");
            s.AppendLine("  Add-Type -AssemblyName System.IO.Compression.FileSystem");
            s.AppendLine("  [System.IO.Compression.ZipFile]::ExtractToDirectory($zip, $stage)");
            s.AppendLine("  $found = Get-ChildItem -LiteralPath $stage -Filter " + Ps(exeName) + " -Recurse -Depth 2 | Select-Object -First 1");
            s.AppendLine("  if (-not $found) { throw ('The download has no ' + " + Ps(exeName) + ") }");
            s.AppendLine("  $source = $found.DirectoryName");
            s.AppendLine("  robocopy (Join-Path $source $data) (Join-Path $install $data) /MIR /R:10 /W:1 /NP /NFL /NDL /NJH /NJS | Out-Null");
            s.AppendLine("  if ($LASTEXITCODE -ge 8) { throw ('Copying game data failed (robocopy ' + $LASTEXITCODE + ')') }");
            s.AppendLine("  robocopy $source $install /E /XD (Join-Path $source $data) /R:10 /W:1 /NP /NFL /NDL /NJH /NJS | Out-Null");
            s.AppendLine("  if ($LASTEXITCODE -ge 8) { throw ('Copying game files failed (robocopy ' + $LASTEXITCODE + ')') }");
            s.AppendLine("  Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue");
            s.AppendLine("  Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue");
            s.AppendLine("  Log 'Installed; relaunching'");
            s.AppendLine("  Start-Process -FilePath $exe -WorkingDirectory $install");
            s.AppendLine("} catch {");
            s.AppendLine("  Log ('Update failed: ' + $_)");
            s.AppendLine("  Start-Process -FilePath $exe -WorkingDirectory $install -ArgumentList '-miaUpdateFailed'");
            s.AppendLine("}");
            return s.ToString();
        }
        /// <summary>
        /// Bash that waits for the game to exit, unpacks the bundle with ditto (keeping permissions and symlinks), clears
        /// quarantine, ad-hoc signs it (CI builds from Linux are unsigned), swaps it in place with a rollback, and relaunches.
        /// </summary>
        public static string MacInstallScript(string log, string app, string zip, string stage, int pid)
        {
            var s = new StringBuilder();
            s.AppendLine("#!/bin/bash");
            s.AppendLine("LOG=" + Sh(log) + "; APP=" + Sh(app) + "; ZIP=" + Sh(zip) + "; STAGE=" + Sh(stage));
            s.AppendLine("exec >>\"$LOG\" 2>&1");
            s.AppendLine("relaunch() { open \"$APP\" --args \"$@\"; exit 0; }");
            s.AppendLine("fail() { echo \"[$(date '+%F %T')] Update failed: $1\"; relaunch -miaUpdateFailed; }");
            s.AppendLine("echo \"[$(date '+%F %T')] waiting for the game to close\"");
            s.AppendLine("for i in $(seq 1 360); do kill -0 " + pid + " 2>/dev/null || break; sleep 0.25; done");
            s.AppendLine("sleep 0.5");
            s.AppendLine("rm -rf \"$STAGE\" && mkdir -p \"$STAGE\" || fail 'could not prepare the staging folder'");
            s.AppendLine("/usr/bin/ditto -x -k \"$ZIP\" \"$STAGE\" || fail 'could not unpack the download'");
            s.AppendLine("NEW=$(find \"$STAGE\" -maxdepth 2 -name '*.app' -type d | head -n 1)");
            s.AppendLine("[ -n \"$NEW\" ] || fail 'the download has no application bundle'");
            s.AppendLine("chmod +x \"$NEW\"/Contents/MacOS/* 2>/dev/null");
            s.AppendLine("/usr/bin/xattr -dr com.apple.quarantine \"$NEW\" 2>/dev/null");
            s.AppendLine("/usr/bin/codesign --force --deep --sign - \"$NEW\" 2>/dev/null || echo 'ad-hoc signing skipped'");
            s.AppendLine("OLD=\"$APP.previous-build\"; rm -rf \"$OLD\"");
            s.AppendLine("mv \"$APP\" \"$OLD\" || fail 'could not move the old build aside'");
            s.AppendLine("if ! mv \"$NEW\" \"$APP\"; then mv \"$OLD\" \"$APP\"; fail 'could not move the new build into place'; fi");
            s.AppendLine("rm -rf \"$OLD\" \"$STAGE\" \"$ZIP\"");
            s.AppendLine("echo \"[$(date '+%F %T')] installed; relaunching\"");
            s.AppendLine("relaunch");
            return s.ToString().Replace("\r\n", "\n");
        }
        static string Ps(string value) => "'" + (value ?? "").Replace("'", "''") + "'";
        static string Sh(string value) => "'" + (value ?? "").Replace("'", "'\\''") + "'";

        public static string ShortDate(string iso) =>
            DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var when) ? when.ToLocalTime().ToString("MMM d, HH:mm") : iso;
        public static string ShortDate(DateTime utc) => utc == default ? "" : utc.ToLocalTime().ToString("MMM d, HH:mm");
        public static string Megabytes(long bytes) => bytes <= 0 ? "" : (bytes / 1048576f).ToString("0") + " MB";
    }
}
