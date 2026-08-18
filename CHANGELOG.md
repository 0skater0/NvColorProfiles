# Changelog

All notable changes to this project are documented here. The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project follows [Semantic Versioning](https://semver.org/).

## 1.2.0 — 2026-08-18

### Added

- **Diagnostic bundle rework.** The support `.zip` now contains a triage summary (mode, active profile, last profile actually applied per the log), monitor model names from EDID instead of only opaque display ids, HDR / Advanced Color state per display, the currently assigned ICC profile per display, current Digital Vibrance and Hue readings, Windows feature version, Night Light and Windows Color Filter state, a plain-language breakdown of NVIDIA Control Panel's persisted color state per device, and a probe of running third-party color tools (f.lux, DisplayCAL, novideo_srgb, DisplayFusion, and others). What the bundle collects and what is redacted is documented in `docs/PRIVACY.md`.
- **Bug report template streamlined.** The GitHub bug template no longer asks you to type in your app version, NVIDIA driver, GPU model, or Windows version by hand — all of that ships in the diagnostic bundle now.

### Fixed

- **Renaming a profile keeps its rules and schedules working.** Renaming used to leave rules, schedule entries, and the fallback-profile setting pointing at the old name, so an app-open rule would silently stop firing after a rename. Every reference now cascades to the new name.

## 1.1.0 — 2026-08-18

### Fixed

- Gamma now applies through the NVIDIA display pipeline LUT, so it survives exclusive-fullscreen games that used to overwrite the GDI gamma ramp.

### Added

- **Export Diagnostic Bundle** in Settings → General for easier bug reports. The zip includes config, GPU info, the full log (capped at 5 MB), and the NVTweak registry branch. Personal data (usernames, user-profile paths, ICC-profile paths) is redacted.
- **Per-profile "include in cycle" flag** so the next/previous hotkeys skip profiles you never want to land on (e.g. the read-only Default while cycling between day and night).
- **Side mouse buttons as hotkey trigger.** Bindings now accept a modifier (Ctrl/Alt/Shift/Win) plus mouse button 4 (XButton1) or 5 (XButton2). Bare side buttons stay pass-through so the browser back/forward gestures keep working.
- **Per-profile direct hotkeys.** Every profile can now be given its own optional hotkey (keyboard or side mouse button with a modifier) that applies it immediately. Conflicts with other profile hotkeys or the global next/previous/toggle bindings are shown as a warning under the picker but not blocked.
- **Optional daily update check** with a Windows toast, tray marker, and manual Check-now button in Settings. Off until the first-run prompt is answered; the app makes no network requests before then.

### Removed

- Legacy GDI gamma backend. NvColorProfiles is NVIDIA-only by design and the GDI fallback was never reached in practice.

## 1.0.1 — 2026-06-13

### Fixed

- Removed UI lag when switching profiles, toggling automatic mode and opening the tray menu. NVIDIA display handles are now cached instead of re-enumerating the driver on every color change.

## 1.0.0 — 2026-06-12

First public release.

### Added

- Adjust **brightness, contrast, gamma, digital vibrance and hue** for NVIDIA displays.
- **Named profiles** with a read-only Default, switchable from the system tray.
- **Rule engine** for automatic per-application switching (process name or window-title regex), with a fallback profile, a configurable switch delay, and a picker for running apps.
- **Time schedule** for switching profiles by time of day, including windows that wrap past midnight.
- **Global hotkeys** to cycle profiles and toggle auto mode, freely rebindable.
- **Multi-monitor** per-display settings within a profile, using real monitor model names.
- **Live preview** while dragging the sliders.
- **Automatic re-apply** after standby/resume, resolution changes and exclusive fullscreen.
- **Import / export** of profiles, rules and schedules as JSON.
- **German and English UI** with automatic language detection.
- **Autostart** with Windows and a one-click reset to NVIDIA defaults.
- Ships as a **portable executable** and as an **NSIS installer**.
