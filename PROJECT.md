# Streamlined Desktop Overlay for Windows (SLW3D)

A lightweight, customizable 3D glass desktop overlay for Windows 10/11 built with .NET 10, WinForms + WebView2, and Three.js.

## Features
- Attaches cleanly to Explorer's desktop layer (below all apps, above icons — never hides or alters Windows desktop icons).
- Interactive 3D orbital centerpiece, floating app arc, customizable decks (`+ DECK`, `+ ADD APP`, up to 8 decks), and bottom search/dock bar.
- Live **Theme Color** slider (360° Hue + Intensity + quick swatches + **Saved Themes** profiles), icon sizing (`Small · Medium · Large`), and wallpaper picker with Dim & Fit modes.
- Live **Clock & Calendar** panel, **System** telemetry panel, one-click **Game Mode** (plus automatic Game Mode watcher), and bottom-left **Power** menu (Lock, Sleep, Restart, Shut Down with 2-click confirmation).
- Global hotkeys: `Ctrl+Alt+I` (Full 3D desktop), `Ctrl+Alt+G` (toggle Game Mode), `Ctrl+Alt+Shift+D` (emergency Panic restore).

## Build & Run
```powershell
.\build.ps1
```
Then launch `bin\controller\Desktop3D.Controller.exe`.
