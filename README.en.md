# AudioSwitch

[简体中文](README.md) | [English](README.en.md) | [日本語](README.ja.md)

AudioSwitch is a Windows tray app for switching your default speakers, headphones and microphone. Choose a device manually, or set priorities for automatic switching when devices connect or disconnect.

[![Download on GitHub Releases](https://img.shields.io/badge/Download_on_GitHub_Releases-2563EB?style=for-the-badge&logo=github&logoColor=white)](https://github.com/EthenGod/AudioSwitch/releases)

Supports Windows 10 / 11 x64. The desktop panel requires Microsoft Edge WebView2 Runtime.

## Download and run

Open [GitHub Releases](https://github.com/EthenGod/AudioSwitch/releases) and download a file under **Assets** for the version you want. For v0.12.2, both packages include the full desktop panel.

| File | How to use it |
| --- | --- |
| **AudioSwitch.exe** (recommended) | Save it in a new, writable folder. Keep the filename and double-click it. On first launch, it extracts the complete app into an adjacent `AudioSwitch` folder. |
| **AudioSwitch-Desktop-v0.12.2-win-x64.zip** | Extract the entire ZIP into a new folder, then run the `AudioSwitch.exe` inside it. |

Before a manual upgrade, exit the old version from its tray menu, then start the new version. Your device settings are kept in your Windows user profile and carry over to the new version.

## Using AudioSwitch

- **Switch devices** — Right-click the tray icon to choose an output or microphone. Double-click to open the main panel. In Settings, you can change this to open the local browser panel.
- **Auto switch** — Set separate priorities for output and input devices. “Also switch communications device” controls whether the Windows default communications device changes too.
- **Device presets** — Open a device's settings from Audio devices to give it a custom name or save a volume level. Output devices also support Windows spatial sound. Compatible Dolby devices can have their own profiles. “Save only” stores a preset for later; “Save and apply” in the Dolby editor applies it to the current output immediately.
- **Game mode** — Enable it from the tray menu or Settings to pause automatic update checks and downloads. Device switching keeps working.

Closing the panel leaves AudioSwitch running in the tray. Use the tray menu to quit the background service.

## Settings and compatibility

Settings are stored at `%LOCALAPPDATA%\AudioSwitch\settings.json`. Export a backup or import existing settings from the panel. Devices are matched by Windows endpoint ID, so devices with the same name keep separate presets. [Settings and backup guide](docs/en/USER-GUIDE.md#settings-and-backups)

AudioSwitch changes Windows default devices. Apps with their own output selection may continue using a different device. Dolby profiles require a supported DAX component and device. [Using Dolby profiles](docs/en/USER-GUIDE.md#dolby-profiles)

The [user guide](docs/en/USER-GUIDE.md) also covers language selection, the browser panel and update settings. Check the release notes for your downloaded version to see which new features it includes.

## Development and testing

The C# background service handles device events, switching and presets. The desktop panel uses React, TypeScript and Tauri 2. On Windows, build the backend and run regression tests from the repository root:

```powershell
.\build.ps1 -Test -OutputDirectory staging
```

Developer documentation is currently in Chinese: [Frontend and desktop builds](frontend/README.md) | [Hardware test checklist](MANUAL-TESTS.md) | [Release packaging](releases/README.md)

## License

AudioSwitch's own code is licensed under [GPL-3.0-only](LICENSE). The bundled SoundVolumeCommandLine (SVCL) uses its author's license, which prohibits charging for distribution or distributing it as part of a commercial product. [Copyright notices](NOTICE.txt) | [Third-party licenses](THIRD_PARTY.md)
