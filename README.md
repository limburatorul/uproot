# Uproot

Uninstall Windows apps without the wait, and without what they leave behind.

Windows' own Installed apps page sizes each app by opening every one of its files. On a PC with a few large apps, that
means hundreds of thousands of files and a search box that takes ten seconds to answer. Uproot reads the same lists
(the registry's Uninstall entries and your Store packages) and shows them at once; sizes are measured in the
background from the folders' listings, so searching never waits for them.

- **One list, searched as you type**: desktop programs and Store apps, with their real size on disk (Store apps' data included).
- **Remove several at once**: tick them and Uproot runs each app's own uninstaller in turn, waiting until it has really
  finished, even the ones that hand off to a copy of themselves.
- **Then the leftovers**: folders and registry keys named after the app, in the places apps keep things, that nothing
  still installed uses. You see the list and choose. Folders go to the Recycle Bin; keys are saved to a `.reg` file first.
- **Entries whose uninstaller is gone** (a deleted folder, a drive that's no longer there) are marked and can be taken off the list.
- Works with File Labs, SpaceScan and Shelf from [Protagonist Labs](https://protagonistlabs.app/?utm_source=github&utm_medium=readme&utm_campaign=uproot).

Free, MIT licence. Windows 10 (2004) or later, x64.

## Install

Download `Uproot-<version>-setup.exe` from [Releases](https://github.com/limburatorul/uproot/releases/latest). It installs to
`C:\Program Files\ProtagonistLabs\Uproot` and asks for administrator rights, as uninstallers and leftovers in Program
Files, ProgramData and HKLM need them. `Uproot.exe` from the same release runs without installing.

Uproot's own files: the log and the registry backups in `%APPDATA%\ProtagonistLabs\Uproot`, a few settings in `HKCU\Software\Uproot`.

## Build

```
installer\build.ps1
```

Needs the .NET 8 SDK and Inno Setup 6. `Uproot.exe --selftest` runs the checks (it writes `%TEMP%\uproot-selftest.txt`).
