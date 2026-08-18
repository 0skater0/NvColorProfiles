# Privacy — what the diagnostic bundle contains

Short version: the diagnostic bundle is a plain `.zip` written to your Downloads folder (or next
to the executable in portable mode). Nothing is uploaded automatically — attaching it to a bug
report is a manual step you perform after you have inspected the file. Every text entry is run
through PII redaction before it lands in the zip.

**Please attach it when you file a bug.** Without it, the most likely thing to happen is that we
spend a couple of rounds guessing at your setup instead of actually looking at your bug. With it,
we can usually pinpoint what is going on on the first read.

## What is inside the zip

| Entry | Content |
|---|---|
| `summary.txt` | One-glance triage: app version, switching mode, active profile, counts of profiles / rules / schedules, whether the gamma backend initialised, and the last profile actually applied according to the log. |
| `README.txt` | Short note describing what the bundle is. |
| `system.txt` | Windows version and feature update (24H2, 25H2, ...), .NET runtime, UI culture, Night Light state, Windows Color Filter state. |
| `gpu.txt` | Per display: monitor model name from EDID, GDI device name, NvAPI display id, EDID manufacturer + product code, HDR / Advanced Color state, currently assigned ICC profile filename, current Digital Vibrance and Hue readings, driver version and GPU model. |
| `nvcp-state.txt` | NVIDIA Control Panel's persisted color state per device, parsed into plain text so a human can read it. |
| `color-tools.txt` | Names of running third-party tools that push their own color transform (f.lux, DisplayCAL, novideo_srgb, DisplayFusion, and others from a small allow-list). |
| `logs.txt` | The app's own log, truncated to the last 5 MB. Contains profile-apply events, driver initialisation and warnings from the color pipeline. |
| `registry-nvtweak.txt` | Raw dump of the same NVCP subtree that `nvcp-state.txt` summarises, kept as a backup so the summary can be verified against the source. |
| `config.json` | The app's persisted configuration — the profiles, rules, schedules and settings you can see in the settings window. |

## Redaction, before anything is written to the zip

- **Windows username** anywhere it appears is replaced with `<USER>`.
- **Machine name** anywhere it appears is replaced with `<HOSTNAME>`.
- **User-profile paths** (`C:\Users\<name>\...`) are collapsed to `C:\Users\<USER>\...`.
- **User-owned ICC profile paths** are further collapsed to `<REDACTED>\<filename>`. The filename
  stays because the bug reader usually needs to know which profile was involved. System ICC
  profiles under `C:\Windows\...` carry no personal information and are kept as-is.

Beyond that: the bundle intentionally reports only the panel model (from EDID vendor + product
code), not the panel serial number. It lists only tools from a small color-related allow-list,
not your full process list. The ICC profile is reported by filename only, never by file
contents. Nothing outside what is listed above is collected.

## Inspect before you send

Every entry in the zip is text — you can open the `.zip` in any archive viewer and read each
file before you upload. If something in there is sensitive for you personally, delete that entry
from the zip and re-attach it, or leave a note in the bug report about what you removed.

## If you cannot generate a bundle

If the app crashes before you can open the export dialog, tick "App does not start (cannot
generate a bundle)" in the bug report and describe how the crash happens in the "Additional
context" field. We will work from there.
