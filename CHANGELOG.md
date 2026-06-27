# Changelog

All notable changes to this project are documented here. The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project follows [Semantic Versioning](https://semver.org/).

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
