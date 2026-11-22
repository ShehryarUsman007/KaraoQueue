# KaraoQueue

A **very basic** karaoke singer rotation app. It was made at the request of a karaoke group in Windsor, Ontario, and built around exactly what they needed, nothing more. It isn't meant to be a general-purpose karaoke tool, so **anyone is free to modify it however they like** for their own group (see [LICENSE](LICENSE)).

## Download

Get the latest version from the [Releases page](https://github.com/ShehryarUsman007/KaraoQueue/releases/latest). Nothing else needs to be installed: .NET is bundled.

- **Windows:** `…-Setup.exe` (installer, no admin rights needed)
- **Ubuntu / Debian:** `.deb`
- **Fedora / openSUSE:** `.rpm`
- **Any Linux:** `.AppImage`
- **Mac:** `…-macOS-AppleSilicon.zip` (M1–M4) or `…-macOS-Intel.zip`. The Mac app isn't signed by Apple, so the first time it's blocked: open it once, then go to System Settings → Privacy & Security → **Open Anyway**.

So far it has been tested on **Fedora 45 Workstation** and **Windows 11**. The other downloads (Ubuntu/Debian, AppImage, Mac) are built the same way but haven't been tried yet.

## What it does

- Pick tonight's singers from a saved list and shuffle the order (Fisher–Yates shuffle).
- **Next / Previous** move through the turns; the current singer is highlighted in a scrollable list.
- **Late singers** are inserted right after whoever is singing, but skipped until the next round starts.
- **Add / Remove** open an alphabetical checklist of names. The first add of the night shuffles automatically; a **Shuffle** button for reshuffling later can be turned on in Options.
- **Options → Settings** lets you set the RGB colours of the button text, background gradient, highlight and names, make the button and name text bigger or smaller, and turn the Shuffle button on or off.
- **Options → Edit File** opens the saved singer list (one name per line) in your default text editor.
- **Options → Add/Change Image** picks a .png or .jpg of max **400 × 225 pixels** (e.g. your group's logo) to show in the bottom-left corner (smaller pictures keep their size, with see-through space around them). Picking a new one replaces the old one; **Options → Remove Image** takes it away.
- The window is as tall as the screen and everything in it scales, so it looks the same on HD, 1440p or 4K displays. Dragging it onto a screen with a different resolution resizes it to fit, and it reopens where it was last closed.

## Where the data is stored

| File | Contents |
|---|---|
| `%AppData%\KaraoQueue\singers.txt` | saved singer names, one per line |
| `%AppData%\KaraoQueue\logo.png` | the picture chosen with Add/Change Image (always saved as PNG) |
| `%AppData%\KaraoQueue\settings.txt` | colours, text sizes and the Shuffle setting chosen in Options → Settings, and where the window was last closed |

(On Linux/macOS this is `~/.config/KaraoQueue/`. Version 1.0.0, then called Karaoke Shuffler, used a `Singer_Selector` folder; KaraoQueue copies it over on first start.)

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download). The app uses the [Uno Platform](https://platform.uno/) (C# only, no XAML), which is pulled in automatically through NuGet.

```
dotnet run --project Singer_Selector.csproj
```

It runs on Windows, Linux and macOS.

### Building the release files

`packaging/build.sh` builds the Windows installer, .deb, .rpm, AppImage and macOS zips into `dist/`. It needs `makensis` (NSIS), `rpmbuild`, `dpkg-deb`, `rsvg-convert` and `zip`.

GitHub Actions runs the same script on every push. Pushing a tag like `v1.2.1` (matching `<Version>` in `Singer_Selector.csproj`) publishes a release with all the files attached.

## Code layout

- `Program.cs`: the window, layout, buttons, pop-up menus and screen scaling
- `Selector.cs`: the rotation logic (shuffle, late singers, next/previous, add/remove)
- `AppSettings.cs`: the Options colours and text sizes, and saving/loading them
- `packaging/`: icon, installer/package definitions and the build script
