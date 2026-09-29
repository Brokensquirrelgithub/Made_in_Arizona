# Playtest builds and in-game updates

Testers install the game once. After that, **Versions & Updates** (main menu, or **UPDATES** in the garage footer) lists every published build and switches the installed game to any of them, newer or older, without reinstalling. Saves, settings and garage progress live outside the game folder and are kept.

## How it works

1. **Every build knows which commit it is.** `BuildGame` writes `StreamingAssets/build.json` (commit, commit date, message, platform, build time and updater protocol) into each player while it builds. The file only exists for the duration of the build and is ignored by Git.
2. **Builds are published as GitHub Releases.** A release is tagged `build-<short sha>` and carries two assets, `MadeInArizona-Windows.zip` and `MadeInArizona-macOS.zip`. Its notes end with two machine-readable lines:
   ```
   mia-updater: 1
   commit: <full sha>
   ```
3. **The game reads the public Releases API** (`GameUpdater`), no account or token needed. It checks once at start-up and whenever the menu opens. The main menu and garage show a banner when a newer build exists.
4. **Switching:** the tester picks a build, and the game downloads its zip and checks the size and GitHub's SHA-256 checksum. After **Restart & switch**, a small script waits for the game to quit, swaps the files and reopens the game on the new build.
   - **Windows** (PowerShell): the game's `_Data` folder is mirrored, and the rest of the build is copied over. Nothing else in the folder is deleted.
   - **macOS** (bash): the new bundle is unpacked with `ditto`, cleared of quarantine and ad-hoc signed. It then replaces the old `.app`, which is restored if anything fails.
   If a switch fails, the game reopens on the old build and says so. The log is in the game's persistent data folder under `Updates/update.log`.

### No switching to builds from before this feature

A release is only offered when its notes say `mia-updater: N` with N ≥ `GameUpdater.MinimumProtocol` (1). Nothing published before the updater existed carries that line, so a tester can never switch to a build that could not switch back. If a future change breaks switching between builds, raise `GameUpdater.Protocol` and `MinimumProtocol` together.

To hide a bad build, delete its release, or remove the `mia-updater` line from its notes.

## Publishing a build

### Option A: automatically, on every push to `main` (GitHub Actions)

`.github/workflows/playtest-builds.yml` builds both players on Linux with [GameCI](https://game.ci) and publishes the release. It needs a Unity license stored as repository secrets (**Settings → Secrets and variables → Actions**):

| Secret | Value |
|---|---|
| `UNITY_LICENSE` | Contents of your Unity license file (`.ulf`); see GameCI's *Activation* guide. Pro/Plus users can set `UNITY_SERIAL` instead. |
| `UNITY_EMAIL` | Unity account email |
| `UNITY_PASSWORD` | Unity account password |

Until the secrets exist the workflow does nothing, apart from leaving a notice. It uses `-customBuildPath` and `-miaCommit` through `BuildGame.BuildForCI`. A newer push cancels a build that is still running, so only the latest commit is published. GameCI must publish a Docker image for the project's Unity version (`ProjectSettings/ProjectVersion.txt`). If it does not, use option B.

### Option B: from your Mac

```bash
Tools/publish-playtest.sh            # builds both players, zips them, creates the release
SKIP_BUILD=1 Tools/publish-playtest.sh   # publish the players already in Builds/
```

It needs Unity (as for `Tools/build.sh`), the Windows build module and the GitHub CLI (`brew install gh`, then `gh auth login`). The commit must be committed and pushed, because the release points at it.

## Tester setup (once)

1. Download the zip for your platform from the newest release on the [Releases page](https://github.com/Brokensquirrelgithub/Made_in_Arizona/releases).
2. **Windows:** unzip it into a folder you can write to, such as `Documents\Made in Arizona`, and run `Made in Arizona.exe`. `Program Files` is read-only for normal users, so updates cannot install there.
3. **macOS:** unzip it and move `Made in Arizona.app` into **Applications** or another folder. Before the first launch, run `xattr -dr com.apple.quarantine "/Applications/Made in Arizona.app"`, or right-click → **Open**. Running it straight from Downloads makes macOS use a read-only copy; the menu explains this if it happens.

From then on, use **Versions & Updates**.

## Limits

- Only the Windows and macOS players switch builds. The Unity editor only lists them.
- The Releases list shows the 50 most recent builds.
- Unauthenticated GitHub API calls are limited to 60 an hour per network. The game checks once at start-up and when the menu is opened (at most every two minutes).
- Builds are development builds and are not notarized. Each switch on macOS applies a fresh ad-hoc signature.
