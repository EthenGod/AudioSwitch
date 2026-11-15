# AudioSwitch v0.13.0 — Stable release

[简体中文](https://github.com/EthenGod/AudioSwitch/blob/master/releases/RELEASE-NOTES-v0.13.0.md) | [English](https://github.com/EthenGod/AudioSwitch/blob/master/releases/RELEASE-NOTES-v0.13.0.en.md) | [日本語](https://github.com/EthenGod/AudioSwitch/blob/master/releases/RELEASE-NOTES-v0.13.0.ja.md)

> This document may contain machine-translated content. The [Chinese original](https://github.com/EthenGod/AudioSwitch/blob/master/releases/RELEASE-NOTES-v0.13.0.md) is authoritative.

Adds English, Japanese, system language matching and custom device names.

[![Download AudioSwitch.exe](https://img.shields.io/badge/Download%20AudioSwitch.exe-2563EB?style=for-the-badge)](https://github.com/EthenGod/AudioSwitch/releases/download/v0.13.0/AudioSwitch.exe)

**Save the EXE in a new, writable folder, keep its filename and double-click it.** On first launch, it extracts the app into an adjacent `AudioSwitch` folder. Keep that entire folder. Before upgrading, quit the old version from its tray menu. Your existing settings are preserved.

[Download EXE](https://github.com/EthenGod/AudioSwitch/releases/download/v0.13.0/AudioSwitch.exe) | [Download full ZIP](https://github.com/EthenGod/AudioSwitch/releases/download/v0.13.0/AudioSwitch-Desktop-v0.13.0-win-x64.zip)

If using the ZIP, extract all files and run the `AudioSwitch.exe` inside. The desktop panel requires Microsoft Edge WebView2.

## ✨ What's new

- Choose Simplified Chinese, English, Japanese or the system language on first launch. Change it later in Settings. If no supported language matches your system, AudioSwitch uses Chinese.
- Set a custom display name in device settings. Names saved on this computer can be completed one word at a time by pressing Tab.
- Language changes now animate text and layout. Error windows use styling consistent with the main panel, and user guides are available in all three languages.
- Successful EXE extraction creates only the `AudioSwitch` folder, without an extra folder containing the ZIP.

## 🛠️ Fixes

- Fixed update and repair tasks accepting further operations while cancellation was in progress, preventing duplicate submissions.
- Fixed settings imports failing when an older configuration contained a damaged name-completion cache.
- Fixed the browser panel failing to connect when opened from the local port's root address.

Source is available under **Source code** on the release page. Project-owned code is licensed under GPL-3.0-only. Third-party licenses are included in the package.
