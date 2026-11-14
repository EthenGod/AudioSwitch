# AudioSwitch user guide

[简体中文](../zh-CN/USER-GUIDE.md) | [English](USER-GUIDE.md) | [日本語](../ja/USER-GUIDE.md)

[Back to home](../../README.en.md)

This guide describes the current repository code. Check your version's release notes for features available in a published build.

## Language and appearance

Choose a language on first launch. AudioSwitch matches your system's preferred languages and recommends Simplified Chinese if none match. After you confirm, the choice is saved. Change it later under Settings → Language. Simplified Chinese, English and Japanese are available.

System device names and names you enter are not translated. App-provided device types, status labels and controls follow the selected language. Language is separate from light/dark mode and interface style. The main panel defaults to dark mode if no appearance preference is saved.

## Devices and auto switching

Use Audio devices to switch the default output or microphone. Open a device's settings to give it a display name; leave the name blank to restore its system name. Devices with the same name remain separate because AudioSwitch uses their Windows endpoint IDs.

In Auto switch, drag devices to change their priority, or use the move up/down buttons. Output and input devices have separate lists. Enabling priority or changing the order may switch devices immediately. Turn off “Also switch communications device” to leave the default communications roles unchanged.

Connection rules take priority over normal ordering and connection prompts. Accepting the system's selection keeps Windows' choice. A rule that switches on connection also applies the device's preset. Offline devices keep their priority and presets.

“Save only” stores settings without changing sound effects immediately. The preset is applied when you later switch to that device. Closing a device prompt keeps the system selection in effect at that moment.

## Settings and backups

The only active settings file is `%LOCALAPPDATA%\AudioSwitch\settings.json`. Changes are saved automatically. `AudioSwitch.exe.config` is a runtime file, not your personal settings.

Use Settings → Export backup to save a copy. In Import settings, choose a backup, review its summary, then confirm. AudioSwitch keeps a complete backup of your previous settings before replacing them. Importing does not start a new audio write. Invalid or unsupported backups do not replace your current settings.

Backups include device names, priorities, connection rules, presets and app preferences. Name-completion history stays on the local computer and is not included in backups or app packages. The Windows startup registration is also local and is not imported.

Use backups exported by AudioSwitch. Importing an older backup without a language field keeps your current language. An unsupported language or style can remain in the settings; the interface temporarily falls back to Chinese or the Classic style. “Keep unchanged” is different from “Off” for volume, spatial sound and Dolby parameters.

## Browser panel

In Settings, change the tray icon's double-click action to “Open in browser”. The panel is served only on the local address `127.0.0.1`.

“Keep browser port open” is off by default. When enabled, the local service stays running after browser tabs close. Turning it off allows existing tabs to keep working; the service exits after the last tab closes. Quitting the AudioSwitch background service also closes the browser service.

Leave the browser port blank for automatic assignment. Enter a number from `1–65535` to save it automatically; it takes effect the next time the service starts. Choose a different port if it is in use. Do not share the browser panel's full access URL.

## Updates and file checks

Check for updates currently uses GitHub. Choose stable releases only or include prereleases. Prereleases may contain unresolved issues.

With automatic updates enabled, downloads run while the computer is idle and installation happens on the next launch. Game mode pauses automatic updates; manual checks remain available. Game mode also lowers the background process priority and uses efficiency mode where supported. Device connection and disconnection events still work.

File check shows which program files passed, are missing, or cannot be read. If repairs are needed, download the complete package for the current version. Follow the confirmation prompts before installation or repair. Personal settings and existing backups are outside the scope of program file repairs.

## Dolby profiles

Dolby features require a supported DAX component and output device. If a component is unsupported, do not bypass the check or replace driver files.

The Dolby editor in device settings can read the current profile, save a device profile, or apply it to the current output. Enable automatic application on output selection to follow that device when it becomes the default multimedia output. Devices without a profile do not actively reset Dolby.

“Save only” does not write to current sound effects. In the Dolby editor, “Save and apply” applies immediately only to the current output. Turning off a strength override means keeping its current value, not disabling the effect. The driver may adjust strengths to supported steps.

The equalizer has two views:

- **10-point dB**: frequency labels are 32, 64, 125, 250, 500 Hz, 1, 2, 4, 8 and 16 kHz, with a range of −12 to +12 dB.
- **20 raw bands**: directly edit integer driver values. These values are not dB.

Viewing or switching views does not rewrite the curve. Editing any 10-point value recalculates all 20 bands, so intermediate details in the original curve may change. Saving and backups always preserve the 20-band values. If application fails, read the recovery result; a failure message does not mean recovery succeeded.

## Further reading

Technical references are currently in Chinese: [Settings format and implementation](../../CONFIGURATION.md) | [Dolby conversion details](../../DOLBY-EQ.md). The [translation guide](../../locales/CONTRIBUTING.md) is available in Chinese and English.
