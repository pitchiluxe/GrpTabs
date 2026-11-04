# GrpTabs

Tabs for every window on Windows. Drag one window's tab onto another window and the two become one tabbed window: click a tab to switch, drag it away to split them again. Created by Erick Omari.

**Download:** [GrpTabs-win-Setup.exe](https://github.com/pitchiluxe/GrpTabs/releases/latest/download/GrpTabs-win-Setup.exe) (Windows 10 and 11, about 10 MB, installs for your account, no admin rights). Website: https://grouptabs.vercel.app

## What it does

- A tab above every app window (always, on hover, or never). Drag a tab onto another window's tabs or title bar to group; drag along the strip to reorder; drop it on the desktop to take it out; middle-click to close; double-click to maximize.
- A group shares one frame. Only the active tab's window is shown; the others are hidden behind it and come to the front, in the same place and size, when you click their tab.
- Hotkeys: Ctrl+Alt+PageDown / PageUp switch tabs, Ctrl+Alt+D takes the active window out, Ctrl+Alt+G pauses GrpTabs. All configurable.
- Settings: General (start with Windows, tray icon, updates, pause, exit), Appearance (opacity, tab width, icons, theme, preview), Behavior, Exceptions (programs that never get tabs), Groups (programs whose windows group automatically, open groups) and Hotkeys.
- Never loses a window: every window GrpTabs hides is written to `%AppData%\GrpTabs\hidden.json` first. Exit, update and crash all show them again (after a crash, on the next start).
- Updates itself from this repository's GitHub Releases (Velopack): checks at start and every six hours, downloads in the background, notifies you, installs on Restart or when GrpTabs closes.

Windows of apps running as administrator are left alone: a normal app is not allowed to move them. If TidyTabs is running too, GrpTabs tells you; both draw tabs in the same place.

## Build

Requires the .NET 10 SDK.

```
dotnet test tests                                   # core rules: groups, hotkeys, settings, auto-grouping
dotnet run --project app                            # run from source (updates are off when not installed)
python tools/make_icon.py                           # redraw the icon and website images (Pillow)
```

Release (bump `<Version>` in `app/GrpTabs.csproj` first):

```
dotnet publish app/GrpTabs.csproj -c Release -r win-x64 --self-contained false -o publish
vpk pack -u GrpTabs -v <version> -p publish -e GrpTabs.exe -i app/Assets/GrpTabs.ico --packTitle GrpTabs --packAuthors "Erick Omari" --framework net10.0-x64-desktop -o releases
vpk upload github --repoUrl https://github.com/pitchiluxe/GrpTabs --token <token> --publish --releaseName "GrpTabs <version>" --tag v<version> -o releases
```

Installed copies pick the new release up automatically. Set `GRPTABS_DEBUG=1` to write `%AppData%\GrpTabs\debug.log`.

## Layout

- `app/Core`: Win32 calls, the group model, rules, hotkeys and settings (pure parts are unit-tested in `tests/`).
- `app/Ui`: the engine (window events, tab strips, grouping, crash recovery), tab strip, settings window, tray and updater.
- `site/`: the website (static HTML/CSS/JS, deployed on Vercel).

MIT License. Copyright (c) 2026 Erick Omari.
