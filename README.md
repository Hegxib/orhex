<p align="center">
  <img src="build/exe_icon.ico" width="96" height="96" alt="Orhex icon" />
</p>

<h1 align="center">Orhex</h1>
<p align="center"><b>Orhex — Game Mimic by Hegxib</b><br/>Spoof your Discord presence in seconds.</p>

<p align="center">
  <a href="https://github.com/Hegxib/orhex/releases"><img src="https://img.shields.io/github/v/release/Hegxib/orhex?style=flat&label=version&color=6366F1" alt="version" /></a>
  <img src="https://img.shields.io/badge/platform-Windows-0078D6?style=flat" alt="platform" />
  <img src="https://img.shields.io/badge/.NET-4.0-512BD4?style=flat" alt=".NET 4.0" />
  <img src="https://img.shields.io/badge/license-GPL--3.0-00C853?style=flat" alt="license" />
  <a href="https://hegxib.me/donate"><img src="https://img.shields.io/badge/donate-hegxib.me-ff3b6b?style=flat" alt="donate" /></a>
</p>

<p align="center">
  <a href="#download">Download</a> •
  <a href="#how-it-works">How it works</a> •
  <a href="#usage">Usage</a> •
  <a href="#building">Building</a> •
  <a href="https://hegxib.me">hegxib.me</a>
</p>

---

### What is Orhex?

Orhex makes Discord think you're playing any game — without installing it. Search for a title, let Orhex resolve the real executable name Discord expects, add it to your queue, and keep the window open while Discord shows you as playing.

No client mods. No injection. No game files touched. Just Rich Presence over Discord's local IPC pipe.

> **1.0** — clean white UI • 15m 40s default timer • queue runs all at once in separate windows.

<p align="center"><img src="assets/discnot.png" alt="Orhex screenshot" width="720" /></p>

### Features

- **Smart search** — exact, slug, alias & acronym matching against Discord's 24k detectable games, plus your installed Steam/Epic/GOG/Start Menu titles
- **Real exe names** — curated `GameDb` + local scan + `PickBestExe` (junk filtering, launcher penalty, name-match bonus)
- **Rich Presence (IPC)** — `discord-ipc-0…9`, 8-byte header, nonce, PING/PONG keepalive
- **Queue that runs together** — `MainForm` stays open, each queued game opens its own `MimicForm`/`NotifyIcon` with independent countdown
- **No time limit** supported
- **Persisted queue** — saved to `%LOCALAPPDATA%\Orhex\queue.dat`

### Download

Grab `orhex.exe` from [**Releases**](https://github.com/Hegxib/orhex/releases) — single file, no installer. Requires Windows 10/11, .NET Framework 4.0+, Discord running.

### Usage

1. Type a game name → **Search**
2. Check the resolved **Executable** (editable) and **Time** (`15m 40s` default)
3. **+ Add to Queue** — reorder with `▲ ▼`, remove with `✕`/`Remove`
4. **▶ START MIMICKING** — every queued game starts in its own window; Discord shows you playing them. Close each window to stop.

Single-game tip: just type a name and hit **Start** without adding to the queue.

`File → Exit` · `Help → About` explains every connection the tool makes.

### How it works

Orhex does not spoof processes by renaming its exe. It connects to Discord's local Rich Presence pipe and pushes `SET_ACTIVITY` with the correct `application_id`:

```
App lookup:  GameDb (curated) → local installed scan → DiscordDb.Search → Steam store fallback
Presence:    DiscordDb.FindApplicationId(name) → FindApplicationId(exe) → DiscordRpc.Connect(appId) → SetActivity
```

### Connections

Orhex makes **no silent network calls** except:

- `https://discord.com/api/v10/applications/detectable` — official detectable-games list (names, exe names, app IDs)
- `https://gist.githubusercontent.com/Cynosphere/.../gameslist.json` — backup mirror
- `https://store.steampowered.com/api/storesearch` — only when **Search** finds no local/Discord match
- `\\.\pipe\discord-ipc-*` — local Rich Presence only (no network)

About dialog (`MenuBuilder.ShowAbout`) lists all of the above.

### Antivirus & False Positives

Some engines may flag `orhex.exe` as `Wacatac.B!ml` / `Win/malicious_confidence_70%` / `Generic` / `Arctic Wolf Unsafe`.

**This is a heuristic false positive — not malware.** Orhex is fully open source, builds reproducibly with the stock .NET compiler, and contains no packing, obfuscation, or payload.

**Why it triggers:**
- Opens Discord's local IPC pipes (`\\.\pipe\discord-ipc-*`) and posts Rich Presence (`SET_ACTIVITY`) — flagged as *local IPC / C2-like*
- Downloads the public games list (`discord.com/api/v10/applications/detectable`, gist backup) and scans Start Menu / Epic / Steam manifests for display names — flagged as *recon*
- New, unsigned `winexe` with low reputation → ML classifiers (`Wacatac.B!ml`, `Falcon 70%`, `Elastic moderate`) default to *suspicious*

**How we keep releases clean:**
- Single-file `csc.exe` build, no dependencies, no packer — `build/build.ps1` is auditable
- Versioned releases are Authenticode-signed (`build/sign.ps1` + `build/orhex.pfx` with timestamp) and carry `1.0` `FileVersion` + manifest `asInvoker`
- VirusTotal is monitored per release; hashes are submitted to Microsoft and other vendors for whitelisting

**Verify it yourself:**
- Build from source: `powershell -ExecutionPolicy Bypass -File build\build.ps1 -SkipSign` and compare your hash to the Release hash — they match when signed
- Check the VirusTotal report linked from each Release and inspect *Behavior* — no network C2, no persistence, no injection

**If your AV blocks it:**
- Microsoft Defender → `Virus & threat protection → Protection history → Allow` or add `orhex.exe` / its folder to `Exclusions`
- To help everyone: submit the signed Release as a false positive — [Microsoft](https://www.microsoft.com/en-us/wdsi/filesubmission) (Developer → `Wacatac.B!ml` clears in ~24h and propagates to many engines), plus the vendor's portal if needed. Reference: *Discord Rich Presence spoofer, open source at https://github.com/Hegxib/orhex*

> Orhex will never ask for admin, never modifies game files, and never touches Discord's client files.

### Building

Single-file `winexe` via `csc.exe` — no NuGet, no external libs:

```powershell
powershell -ExecutionPolicy Bypass -File build\build.ps1 -SkipSign
# real releases: .\build\sign.ps1 (PFX, done by maintainer)
```

Requirements: `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`, `System.Windows.Forms`, `System.Drawing`, `System.Core`.

Output: `orhex.exe` (icon `build/exe_icon.ico`, manifest `build/app.manifest`, version `1.0`).

### Project layout

```
Orhex.cs          # Program, QueueItem, GameDb, MainForm, MimicForm
GameResolve.cs    # Json, GameScanner, OnlineSearch, DiscordDb
DiscordRpc.cs     # IPC Rich Presence client
build/            # build.ps1, sign.ps1, AssemblyInfo.cs (1.0), icons, manifest
assets/           # screenshots
```

### Links

- **Website:** https://hegxib.me
- **GitHub:** https://github.com/Hegxib/orhex
- **Donate:** https://hegxib.me/donate
- **Socials:** https://x.hegxib.me

### Disclaimer

Educational / research tool. Use at your own risk and in compliance with Discord's Terms of Service.

---

<p align="center"><sub>Orhex 1.0 · by Hegxib</sub></p>
