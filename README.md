# BluetoothHandsFreeToggle

![Downloads](https://img.shields.io/github/downloads/Avazbek22/BluetoothHandsFreeToggle/total)
![License](https://img.shields.io/github/license/Avazbek22/BluetoothHandsFreeToggle)
![.NET](https://img.shields.io/badge/.NET-10-purple)
![Platform](https://img.shields.io/badge/platform-Windows-green)
![Repo size](https://img.shields.io/github/repo-size/Avazbek22/BluetoothHandsFreeToggle)

BluetoothHandsFreeToggle is a small Windows utility that manages the **Bluetooth Hands-Free (HFP / Headset)** audio path at the operating system level.

The tool is designed for scenarios where Windows applications (most commonly games or communication-enabled apps) implicitly activate the Hands-Free profile, forcing Bluetooth headphones into low-quality call audio instead of stereo (**A2DP**).

### What this project is

* A **console-based Windows tool** for enabling or disabling Bluetooth Hands-Free (HFP) support
* Focused on **audio stability**, not device removal or driver manipulation
* Intended for users who want Bluetooth headphones for **output only**, often with a separate microphone

### How it works (high-level)

* Detects known Windows components responsible for Bluetooth Hands-Free (HFP)
* Stops and disables them when requested
* Optionally applies a registry-level startup lock for reliability
* Can restore a safe default state using a small local backup

### Technology stack

* **Language:** C#
* **Runtime:** .NET (console application)
* **Platform:** Windows 10 / Windows 11
* **Privileges:** Administrator (required only to change service state)

**Download:** 👉 **[Latest Release](../../releases/latest)** • **[All Releases](../../releases)**

---

## Quick start

1. Download `BluetoothHandsFreeToggle.exe` from **[Latest Release](../../releases/latest)**.
2. Run it (Windows will ask for **Administrator** once).
3. Use the menu:

   * **[1] Status**
   * **[2] Disable Hands‑Free (HFP)**
   * **[3] Enable Hands‑Free (HFP)**

That’s it.

---

## When you need it

BluetoothHandsFreeToggle is useful if you:

* Play games where sound randomly turns into “telephone quality” (very common with **Call of Duty / Warzone / MW**).
* Use Bluetooth headphones for gaming and **never want Windows to switch to Headset/Hands‑Free**.
* Use a **separate microphone** (laptop mic / USB mic) and want Bluetooth for output only.
* Want stable audio devices without Windows “communications” surprises.

---

## What it does (simple explanation)

Bluetooth headsets on Windows usually expose two profiles:

* **A2DP (Stereo)** — great audio quality ✅
* **HFP / Hands‑Free (Headset / phone mode)** — low quality call audio ❌

Some apps trigger HFP (even if you don’t explicitly enable voice chat). Once HFP activates, Windows may switch your headset into the “Headset” device and your audio quality drops.

**This tool disables the Windows components that enable HFP**, so Windows can no longer push your headset into phone mode.

---

## How it works (under the hood)

BluetoothHandsFreeToggle performs **only a small, conservative set of changes**:

* Targets known HFP-related Windows components (services) when they exist on your system.
* Stops them (when possible) and sets startup to **Disabled**.
* Optionally applies a registry “Start=Disabled” lock for reliability.
* Creates a small backup so “Enable” can restore a safe baseline.

✅ It does **not** uninstall drivers, remove Bluetooth pairings, or modify firmware.

**Backup location:**

* `%ProgramData%\BluetoothHandsFreeToggle\backup.json`

---

## Important notes

* When HFP is **disabled**, your Bluetooth headset **microphone / call mode will not work**.
* If you need voice chat, use:

  * Laptop built‑in mic, or
  * USB microphone / audio interface.

You can always restore voice mode by selecting **Enable Hands‑Free (HFP)**.

---

## FAQ

### Will this break stereo (A2DP) audio?

No. The goal is to keep your headset in **Stereo (A2DP)** and prevent Windows from switching to **Hands‑Free (HFP)**.

### Why does Windows keep switching my headset into “phone mode”?

Because an app requests voice/communications audio, Windows enables the headset’s **Hands‑Free profile**. That profile trades audio quality for bidirectional “call” audio.

### Is this safe?

The tool is intentionally conservative:

* It only touches known HFP components.
* It avoids driver removal and pairing resets.
* It keeps a small backup for restore.

### I disabled HFP but audio still sounds bad. Why?

Usually an app already switched your audio pipeline and keeps the old state.
Try:

1. Close the game/app that triggered it.
2. Disconnect and reconnect the headset.
3. If stubborn, reboot once.

### Status shows only one component present. Is that normal?

Yes. Different Windows builds/drivers expose HFP via different service sets.

### Will antivirus warn about the EXE?

Sometimes. Tools that require admin and change service startup modes can trigger heuristics. You can check the release’s SHA‑256 on GitHub and build from source if you prefer.

---

## Tips for the best experience

* For gaming, keep Bluetooth for **output only** and use a **separate mic**.
* If a specific game triggers HFP, disable HFP first, then launch the game.
* If you ever need headset mic/calls, temporarily **Enable** HFP.

---

## License (MIT)

---

## RU Русский (кратко)

BluetoothHandsFreeToggle отключает в Windows режим **Hands‑Free (HFP)**, чтобы Bluetooth‑наушники не превращались в «телефонный режим» с ужасным качеством звука. Особенно полезно для игр (включая Call of Duty), когда приложение дёргает голос/связь и ломает стерео.

Скачать: **[Latest Release](../../releases/latest)**
