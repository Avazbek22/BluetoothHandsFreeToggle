# BluetoothHandsFreeToggle

<p align="center">
  <img src="BluetoothHandsFreeToggle/Assets/AppIcon.png" width="144" alt="BluetoothHandsFreeToggle logo">
</p>

![Downloads](https://img.shields.io/github/downloads/Avazbek22/BluetoothHandsFreeToggle/total)
![License](https://img.shields.io/github/license/Avazbek22/BluetoothHandsFreeToggle)
![.NET](https://img.shields.io/badge/.NET-10-purple)
![Platform](https://img.shields.io/badge/platform-Windows-green)
![Repo size](https://img.shields.io/github/repo-size/Avazbek22/BluetoothHandsFreeToggle)
[![Support on Boosty](https://img.shields.io/badge/Support-Boosty-F15F2C)](https://boosty.to/avazbek22)

BluetoothHandsFreeToggle is a Windows utility for resetting or disabling the Bluetooth Classic Hands-Free Profile (HFP) when a headset becomes stuck in low-quality call audio instead of stereo A2DP playback.

**Download:** [Latest Release](../../releases/latest) · [All Releases](../../releases)

## Modes

### Soft reset

Restarts only HFP services that are currently active.

- Does not change service startup configuration.
- Does not create or replace a backup.
- Keeps the Bluetooth headset microphone available after the restart.
- Can clear an HFP session that remained active after a game or communications app stopped using the microphone.

### Hard mode

Stops and disables HFP services to force high-quality playback.

- Saves the original startup and Running/Stopped state before making changes.
- Disables the Bluetooth Classic headset microphone system-wide.
- Is intended for output-only use, often with a laptop or USB microphone.

### Restore

Restores the startup and Running/Stopped state saved by Hard mode. If no backup exists, it uses the recovery defaults `Manual + Running`.

## Bluetooth Classic limitation

Bluetooth Classic cannot provide A2DP-quality playback while the HFP microphone is actively in use. Soft reset can fix a profile that is incorrectly stuck, but it cannot remove this protocol limitation.

On Windows 11, A2DP and HFP audio endpoints are unified and Windows selects HFP automatically when an application opens the Bluetooth microphone. See [Microsoft's Bluetooth Classic Audio documentation](https://learn.microsoft.com/windows-hardware/drivers/bluetooth/bluetooth-classic-audio).

## Quick start

1. Download `BluetoothHandsFreeToggle.exe` from the [latest release](../../releases/latest).
2. Run it and approve UAC.
3. Choose:

   - `[1]` Status
   - `[2]` Soft reset
   - `[3]` Hard mode
   - `[4]` Restore
   - `[5]` Help
   - `[6]` About
   - `[7]` Change language

Hard mode displays an additional warning before disabling HFP.

The interface automatically selects Russian when the Windows UI or regional
culture is Russian; otherwise it uses English. A language selected from the
menu is remembered in:

```text
%AppData%\BluetoothHandsFreeToggle\ui-settings.json
```

## Command line

```powershell
BluetoothHandsFreeToggle.exe status
BluetoothHandsFreeToggle.exe soft
BluetoothHandsFreeToggle.exe hard
BluetoothHandsFreeToggle.exe restore
BluetoothHandsFreeToggle.exe language en
BluetoothHandsFreeToggle.exe language ru
```

Backward-compatible aliases:

```text
disable = hard
enable  = restore
```

Exit codes:

- `0` — the operation completed and the final state was verified;
- `1` — the operation failed completely or partially;
- `2` — invalid command-line arguments.

Unknown commands and options do not open the interactive UI or trigger UAC.

## Safe restore behavior

The original state is stored in:

```text
%ProgramData%\BluetoothHandsFreeToggle\backup.json
```

Safety rules:

- If the backup cannot be created, Hard mode makes no service changes.
- A repeated Hard mode call preserves the first original-state backup.
- The backup is written atomically through a temporary file.
- A partial Restore keeps the backup for another attempt.
- A fully successful Restore removes the backup.
- A named mutex prevents concurrent operations from changing services and backup state at the same time.
- Every operation reads the final service state; partial failures return exit code `1`.

The tool uses the Windows Service Control Manager API. It does not directly edit service startup values in the registry, uninstall drivers, remove pairings, or modify firmware.

## Supported Windows components

The target list is intentionally conservative:

- `BthHFSrv` — present on some Windows 10 systems;
- `BTAGService` — Bluetooth Audio Gateway, commonly present on Windows 11.

It does not disable core Bluetooth services such as `bthserv` or `BluetoothUserService`.

Bluetooth LE Audio and vendor-specific Bluetooth stacks can behave differently. Hard mode targets Bluetooth Classic HFP for the whole system, not one selected headset.

## Building and testing

Requirements:

- Windows 10/11
- .NET 10 SDK

```powershell
dotnet restore
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build --no-restore
```

Warnings are treated as build errors. Unit tests cover Soft/Hard/Restore state transitions, backup preservation, partial failures, and status-query errors.

Create a compressed, self-contained, single-file `win-x64` build:

```powershell
dotnet publish BluetoothHandsFreeToggle/BluetoothHandsFreeToggle.csproj `
  --configuration Release `
  --no-restore `
  -p:PublishProfile=win-x64
```

## Support the project

BluetoothHandsFreeToggle is free and open source. If the utility helped you,
you can support its continued development on [Boosty](https://boosty.to/avazbek22).

## License

[MIT](LICENSE) © 2026 Avazbek Olimov

## Русский

BluetoothHandsFreeToggle помогает вернуть нормальное качество звука, когда Windows переводит Bluetooth-наушники в телефонный режим HFP.

- **Soft reset** перезапускает активные HFP-службы, не меняет их автозапуск и сохраняет доступность микрофона.
- **Hard mode** полностью отключает HFP ради приоритета качества воспроизведения; Bluetooth-микрофон не работает до Restore.
- **Restore** возвращает сохранённые настройки и исходное состояние служб.
- Язык определяется по настройкам Windows и переключается пунктом `[7]`; выбор сохраняется между запусками.
- Встроенная справка `[5]` объясняет режимы, ограничения Bluetooth Classic и порядок восстановления.
- Поддержать дальнейшую разработку можно на [Boosty](https://boosty.to/avazbek22).

Важно: при активном Bluetooth Classic микрофоне одновременно сохранить A2DP-качество технически невозможно. Soft mode предназначен для сброса ошибочно «залипшего» HFP, а Hard mode — для гарантированного исключения HFP.
