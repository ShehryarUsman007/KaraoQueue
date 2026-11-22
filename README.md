# Karaoke Shuffler

A **very basic** karaoke singer rotation app. It was made at the request of a karaoke group in Windsor, Ontario, and built around exactly what they needed — nothing more. It isn't meant to be a general-purpose karaoke tool, so **anyone is free to modify it however they like** for their own group (see [LICENSE](LICENSE)).

## Download

Get the latest version from the [Releases page](https://github.com/ShehryarUsman007/Kareoke-Shuffler/releases/latest). Nothing else needs to be installed: .NET is bundled.

- **Windows:** `…-Setup.exe` (installer, no admin rights needed)
- **Ubuntu / Debian:** `.deb` — **Fedora / openSUSE:** `.rpm` — **any Linux:** `.AppImage`
- **Mac:** `…-macOS-AppleSilicon.zip` (M1–M4) or `…-macOS-Intel.zip`. The Mac app isn't signed by Apple, so the first time it's blocked: open it once, then go to System Settings → Privacy & Security → **Open Anyway**.

## What it does

- Pick tonight's singers from a saved list and shuffle the order (Fisher–Yates shuffle).
- **Next / Previous** move through the turns; the current singer is highlighted in a scrollable list.
- **Late singers** are inserted right after whoever is singing, but skipped until the next round starts.
- **Add Singer / Remove Singer** open a checklist of names, with a **Shuffle** button to reshuffle at any time.
- **Options** lets you set the RGB colours of the button text, background gradient, highlight and names.
- **Edit File** opens the saved singer list (one name per line) in your text editor.
- The window and everything in it scale to the screen, so it looks the same on HD, 1440p or 4K displays.

## Where the data is stored

| File | Contents |
|---|---|
| `%AppData%\Singer_Selector\singers.txt` | saved singer names, one per line |
| `%AppData%\Singer_Selector\settings.txt` | colours chosen in Options |

(On Linux/macOS this is `~/.config/Singer_Selector/`.)

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download). The app uses the [Uno Platform](https://platform.uno/) (C# only, no XAML), which is pulled in automatically through NuGet.

```
dotnet run --project Singer_Selector.csproj
```

It runs on Windows, Linux and macOS.

### Building the release files

`packaging/build.sh` builds the Windows installer, .deb, .rpm, AppImage and macOS zips into `dist/`. It needs `makensis` (NSIS), `rpmbuild`, `dpkg-deb`, `rsvg-convert` and `zip`.

GitHub Actions runs the same script on every push. Pushing a tag like `v1.0.0` (matching `<Version>` in `Singer_Selector.csproj`) creates a draft release with all the files attached.

## Code layout

- `Program.cs` — the window, layout, buttons, pop-up menus and screen scaling
- `Selector.cs` — the rotation logic (shuffle, late singers, next/previous, add/remove)
- `ColorSettings.cs` — the Options colours and saving/loading them
- `packaging/` — icon, installer/package definitions and the build script
