<p align="center">
  <img src="BluetoothHandsFreeToggle/Assets/AppIcon.png" width="168" alt="BluetoothHandsFreeToggle Windows Bluetooth audio fix logo">
</p>

<h1 align="center">BluetoothHandsFreeToggle</h1>

<p align="center">
  <strong>Fix low-quality Bluetooth headset audio and stuck Hands-Free mode on Windows 10 and Windows 11.</strong>
</p>

<p align="center">
  Built for Call of Duty and Warzone players, gamers, streamers, developers, and anyone whose headphones suddenly sound like a phone call.
</p>

<p align="center">
  <a href="https://github.com/Avazbek22/BluetoothHandsFreeToggle/releases">
    <img src="https://img.shields.io/github/downloads/Avazbek22/BluetoothHandsFreeToggle/total?style=flat-square&color=0078d4" alt="Total downloads">
  </a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?style=flat-square&logo=windows11&logoColor=white" alt="Windows 10 and 11">
  <img src="https://img.shields.io/badge/.NET-10-512BD4?style=flat-square&logo=dotnet&logoColor=white" alt=".NET 10">
  <img src="https://img.shields.io/github/license/Avazbek22/BluetoothHandsFreeToggle?style=flat-square" alt="MIT license">
  <img src="https://img.shields.io/github/repo-size/Avazbek22/BluetoothHandsFreeToggle?style=flat-square" alt="Repository size">
</p>

<p align="center">
  <a href="#download">Download</a> •
  <a href="#typical-use-cases">Use cases</a> •
  <a href="#choose-your-mode">Modes</a> •
  <a href="#quick-start">Quick start</a> •
  <a href="#troubleshooting-playbook">Troubleshooting</a> •
  <a href="#frequently-asked-questions">FAQ</a> •
  <a href="#build-from-source">Build</a>
</p>

---

> **Call of Duty, Warzone, Discord, or another game opened your Bluetooth microphone—and now the whole game sounds muffled, compressed, or mono?**

BluetoothHandsFreeToggle is a free, open-source Windows utility for fixing low-quality Bluetooth headset audio caused by the Hands-Free Profile (HFP). Windows can switch gaming headphones from high-quality A2DP stereo to HFP call audio when Call of Duty voice chat, Discord, a browser, or recording software opens the headset microphone—and sometimes fails to switch back. This tool provides a safe Soft reset and a reliable Hard mode for restoring clear stereo game audio.

---

<a id="download"></a>
## Download 🚀

**Latest GitHub release:**
👉 [https://github.com/Avazbek22/BluetoothHandsFreeToggle/releases/latest](https://github.com/Avazbek22/BluetoothHandsFreeToggle/releases/latest)

**All versions and release notes:**
👉 [https://github.com/Avazbek22/BluetoothHandsFreeToggle/releases](https://github.com/Avazbek22/BluetoothHandsFreeToggle/releases)

**Package:** Portable Windows x64 app · Single EXE · No installer · No separate .NET runtime

---

<p align="center">
  <a href="https://boosty.to/avazbek22">
    <img src=".github/assets/boosty-support.svg" width="800" alt="Support BluetoothHandsFreeToggle on Boosty">
  </a>
</p>

## Why use it?

| 🎮 Game-friendly | 🛡️ Recoverable | ⚡ Fast |
|:---|:---|:---|
| Fix audio left in call mode after games, voice chat, or capture software. | Hard mode backs up the original Windows service state before changing anything. | Download one self-contained `.exe`; no installer or separate .NET runtime is required. |

Additional highlights:

- Interactive menu for quick recovery.
- Soft mode preserves headset microphone availability.
- Hard mode prioritizes high-quality playback.
- Restore returns the saved service startup and running state.
- Final state verification after every operation.
- English and Russian application interface with automatic language detection.
- Built-in help, status reporting, and safe handling of partial failures.

## Typical use cases

### Call of Duty or Warzone voice chat ruins Bluetooth audio

You launch Call of Duty, enter a Warzone match, or enable voice chat in a Black Ops multiplayer lobby. Windows activates the headset microphone, switches from A2DP stereo to HFP call mode, and gunfire, footsteps, music, and spatial audio suddenly sound thin or muffled.

Close or disable the in-game Bluetooth microphone first, then use **Soft reset**. If you use a laptop, USB, or standalone microphone and only need the headset for playback, **Hard mode** prevents Windows from selecting Bluetooth HFP again.

### The game closed, but the headset is still stuck in Hands-Free mode

Some games and communication apps release the microphone without Windows restoring the original stereo route. Soft reset restarts the active HFP service without changing its startup configuration and can release that stuck call session.

### Discord and a game are fighting over the headset

Running Discord, Steam Chat, Xbox Game Bar, or in-game voice chat alongside a game can keep the Bluetooth microphone active. Select a laptop or USB microphone in the voice application, then reset HFP to recover high-quality game audio.

### Streaming or recording software triggered low-quality sound

OBS, NVIDIA ShadowPlay, browser recording tools, and other capture software may open the default headset microphone. After changing their input device or stopping capture, use Soft reset to return to stereo playback.

### You want maximum game audio quality and use a separate microphone

Hard mode is designed for this setup. It disables Bluetooth Classic HFP system-wide, forcing Windows to prioritize high-quality A2DP playback. Restore HFP later whenever you need the headset microphone again.

## Choose your mode

| Mode | What it does | Headset microphone | Best used when |
|:---|:---|:---:|:---|
| **Status** | Reads supported HFP services and backup state without changing anything. | Available | You want to inspect the current state or diagnose a problem. |
| **Soft reset** | Restarts only active HFP services without changing their startup type. | Preserved | A game or voice app released the microphone, but Windows stayed in call mode. |
| **Hard mode** | Stops and disables Bluetooth Classic HFP system-wide after saving the original state. | Disabled | You need reliable stereo playback now and can use another microphone—or no microphone. |
| **Restore** | Restores the startup and Running/Stopped state saved by Hard mode. | Restored | You need the Bluetooth headset microphone again. |

> [!IMPORTANT]
> Bluetooth Classic cannot provide A2DP-quality stereo playback while the headset's HFP microphone is actively in use. Soft reset can clear a **stuck** HFP session, but it cannot bypass this Bluetooth limitation. Hard mode solves the quality problem by disabling HFP, which also disables the Bluetooth headset microphone.

On Windows 11, A2DP and HFP are exposed through unified audio endpoints, and Windows automatically selects HFP when an application opens the headset microphone. For the platform details, see [Microsoft's Bluetooth Classic Audio documentation](https://learn.microsoft.com/windows-hardware/drivers/bluetooth/bluetooth-classic-audio).

## Quick start

1. Download `BluetoothHandsFreeToggle.exe` from the [latest release](https://github.com/Avazbek22/BluetoothHandsFreeToggle/releases/latest).
2. Close Call of Duty, Warzone, Discord, browsers, OBS, recording tools, and other applications that may be using the headset microphone.
3. Run the executable. Interactive mode requests Administrator access once; canceling UAC leaves status-only functionality available.
4. Start with **`[2] Soft reset`**.
5. If Windows immediately returns to low-quality audio, confirm that no application is still using the headset microphone.
6. Use **`[3] Hard mode`** when you want to prioritize playback quality completely.
7. Use **`[4] Restore`** whenever you need the Bluetooth headset microphone again.

After an operation, reconnect the headset or restart audio playback if a game cached the previous Windows audio route.

### Interactive menu

```text
[1] Get status
[2] Soft reset
[3] Hard mode
[4] Restore
[5] Help
[6] About
[7] Change language
[8] Support the project (Boosty)
[0] Exit
```

The selected application language is stored in:

```text
%AppData%\BluetoothHandsFreeToggle\ui-settings.json
```

## Troubleshooting playbook

### Call of Duty or Warzone Bluetooth audio sounds muffled

1. Disable in-game voice chat or select a laptop/USB microphone in the Call of Duty audio settings.
2. Close any lobby, overlay, or background voice application still using the Bluetooth microphone.
3. Run **Soft reset**.
4. Restart the game audio or reconnect the headset if the old route was cached.
5. If you do not need the headset microphone, use **Hard mode** for reliable A2DP playback.

### Audio became bad after voice chat

1. Exit every app that might be capturing the Bluetooth microphone.
2. Run **Soft reset**.
3. Restart playback or reconnect the headset if necessary.

### Audio becomes bad again immediately

An application is probably still opening the HFP microphone. Check:

- In-game voice chat and microphone settings.
- Discord, Steam Chat, Teams, or another communication app.
- OBS, ShadowPlay, audio recorders, browser permissions, and background tabs.
- The selected input device in Windows and each running application.

Disable microphone access in that application, select a laptop/USB microphone, or use **Hard mode**.

### I need the headset microphone again

Run **Restore**. It returns the service configuration saved before Hard mode. If no backup exists, the tool uses conservative recovery defaults: `Manual + Running`.

### Restore only completed partially

Run Restore again. The backup is intentionally kept until every supported service has been successfully restored and verified.

## Safety and recovery

Before Hard mode changes service configuration, the original state is stored in:

```text
%ProgramData%\BluetoothHandsFreeToggle\backup.json
```

Recovery behavior is intentionally conservative:

- If a backup cannot be created, Hard mode makes no service changes.
- Repeated Hard mode runs preserve the first original-state backup.
- Backups are written atomically through a temporary file.
- Partial Restore attempts keep the backup for another retry.
- A fully successful and verified Restore removes the backup.
- A named mutex prevents concurrent operations from racing over services or backup state.
- Every operation reads the final Windows service state and reports partial failures.

The tool uses the Windows Service Control Manager API. It does **not**:

- Uninstall or replace Bluetooth drivers.
- Remove Bluetooth pairings.
- Modify headset firmware.
- Directly edit service startup values in the registry.
- Disable core Bluetooth services such as `bthserv` or `BluetoothUserService`.

## Supported Windows components

The target list is intentionally small:

| Service | Purpose |
|:---|:---|
| `BTAGService` | Bluetooth Audio Gateway used for HFP on many Windows 11 systems. |
| `BthHFSrv` | Bluetooth Hands-Free service present on some Windows 10 systems. |

Hard mode affects Bluetooth Classic HFP **system-wide**, not one selected headset. Bluetooth LE Audio and vendor-specific Bluetooth stacks may behave differently.

### Compatibility

- Windows 10 or Windows 11.
- Official release build: self-contained single-file `win-x64`.
- Bluetooth Classic headsets using A2DP/HFP.
- Administrator access for Soft, Hard, and Restore operations.

## Frequently asked questions

### Why do my Bluetooth headphones sound bad in Call of Duty or Warzone?

Call of Duty voice chat can open the Bluetooth headset microphone. On Bluetooth Classic devices, Windows must then switch from high-quality A2DP stereo to the lower-bandwidth HFP call profile. The result can sound muffled, compressed, mono, or similar to telephone audio.

### Why is my Bluetooth headset still in Hands-Free mode after closing the game?

The game, an overlay, Discord, or another background process may still hold the microphone open. Windows can also leave the HFP session stuck after the microphone is released. Close every microphone user and try Soft reset.

### Can I use the Bluetooth headset microphone and high-quality game audio simultaneously?

Not with the Bluetooth Classic A2DP/HFP combination. A2DP provides high-quality stereo output, while HFP provides two-way call audio with reduced playback quality. Use a laptop or USB microphone if you want clear game audio and voice chat at the same time.

### Does this fix Bluetooth audio in Discord, Steam, OBS, and browsers?

It can fix audio that remains stuck in HFP after those applications stop using the headset microphone. If an application is still actively recording from the Bluetooth microphone, Windows will continue to require HFP until you change or disable that input.

### Is Hard mode permanent?

No. Hard mode stores the original service configuration first. Choose Restore from the menu to return the saved startup and running state.

### Do I need to install .NET?

No. The official `win-x64` release is a self-contained single executable. The .NET 10 SDK is required only when building the project from source.

## For developers

The codebase keeps Windows-specific operations separate from recovery logic:

```text
BluetoothHandsFreeToggle/
├── App/            Application startup, elevation, and documentation
├── Core/           Soft, Hard, Restore, status, and backup logic
├── Localization/   English and Russian resources
├── UI/             Interactive console interface
└── Windows/        Service Control Manager integration

BluetoothHandsFreeToggle.Tests/
└── Unit tests for state transitions, backups, failures, and localization
```

Quality safeguards include nullable reference types, recommended .NET analyzers, warnings treated as errors, final-state verification, and unit tests for recovery-critical paths.

## Build from source

Requirements:

- Windows 10 or Windows 11.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- Git.

```powershell
git clone https://github.com/Avazbek22/BluetoothHandsFreeToggle.git
cd BluetoothHandsFreeToggle

dotnet restore
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build --no-restore
```

Create the compressed, self-contained, single-file `win-x64` build:

```powershell
dotnet publish BluetoothHandsFreeToggle/BluetoothHandsFreeToggle.csproj `
  --configuration Release `
  -p:PublishProfile=win-x64
```

The published executable is placed under:

```text
BluetoothHandsFreeToggle/bin/Release/net10.0-windows10.0.19041.0/win-x64/publish/
```

## Support the project

BluetoothHandsFreeToggle is free and open source. If it saved your game night, stream, meeting, or debugging session, consider supporting continued development and testing:

<p align="center">
  <a href="https://boosty.to/avazbek22">
    <img src=".github/assets/boosty-support.svg" width="800" alt="Support BluetoothHandsFreeToggle on Boosty">
  </a>
</p>

## Contributing

Bug reports, reproducible Windows/Bluetooth edge cases, and focused pull requests are welcome. Please include your Windows version, headset model, affected service state, and the application that triggered HFP when reporting audio-routing problems.

- [Open an issue](https://github.com/Avazbek22/BluetoothHandsFreeToggle/issues/new)
- [View the source](https://github.com/Avazbek22/BluetoothHandsFreeToggle)

## License

Released under the [MIT License](LICENSE).

© 2026 Avazbek Olimov
