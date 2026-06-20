# PixelPaws — a desktop pet for Windows

A Comnyang-style desktop mascot: a cute pixel-art **black cat** that lives on top of your screen,
wanders, watches your cursor, naps, eats, can be dragged around, walks along the top edges of your
open windows, reacts when you type, stretches on a timer, and unrolls toilet paper when you scroll. 🐾

Built with **C# / WPF on .NET 8**. Original art (not Comnyang's) — this is a clone of the *mechanism*.

## Quick setup (one-click)

After cloning, just **double-click `install.bat`**. It builds the app (Release), puts a
**PixelPaws shortcut on your Desktop** (with the cat icon), and **enables auto-start at login**.

- `build.bat` — just build, don't install.
- `install.bat` — build + desktop shortcut + auto-start. Re-run after `git pull` to update.
- `uninstall.bat` — remove the shortcut and disable auto-start (keeps the app).

Requires the **.NET 8 SDK** (`winget install Microsoft.DotNet.SDK.8` if you don't have it).

## Run manually

```powershell
cd desktop-pet
dotnet build -c Release
.\src\DesktopPet\bin\Release\net8.0-windows\PixelPaws.exe
```

## What it does

- **Wanders & watches** — strolls around, and turns to face your mouse cursor while idle; occasionally chases it.
- **Walks on windows** — detects the top edges of your open windows and walks/sits along them. Falls and lands when it steps off.
- **Drag** — left-click and drag to pick it up; release to drop (it falls to the nearest surface).
- **Pet it** — hover the mouse over the cat and it shows a happy face with floating hearts.
- **Naps & eats** — random sleep (with Zzz) and eating animations.
- **Typing reaction** — when you type, the cat types along at a tiny keyboard; type *fast* and it turns **red** and frantic. Calms down shortly after you stop.
- **Stretch reminder** — on a timer (off / 15 / 30 / 45 / 60 min) the cat stretches and a cute "stretch time!" popup nudges you to stretch too.
- **Toilet paper** 🧻 — scroll the mouse wheel and the cat unrolls a trail of toilet paper beside it (it retracts when you stop).
- **Tray icon** → Settings / Pause / AI companion / Quit. Settings persist to `%AppData%\PixelPaws\settings.json`. Single instance only.

## 🤖 AI companion (optional, off by default)

The cat can also **talk to you**. It's **disabled by default** — nothing AI-related runs and no
network request is made until you turn it on.

- **Chat** — with it enabled, **tap the cat** (or tray → *Talk to cat…*), type, and it replies
  in-character in a speech bubble above its head.
- **Emotion → animation** — the reply carries a hidden emotion tag that drives the cat's animation
  (happy → sparkle, excited → zoomies, sleepy → loaf, silly → spin, …). This is the one idea
  borrowed from [Open-LLM-VTuber](https://github.com/Open-LLM-VTuber/Open-LLM-VTuber).
- **Cute tools** — it can check the **time**, your **system stats** (CPU/battery/app), and the
  **weather** when you ask.
- **Speaks up on its own** — occasionally makes a short, context-aware remark (time of day, what
  you're doing) when you're around. Toggle in Settings.

### Setup

1. Get a **free** Google **Gemini** API key at [aistudio.google.com](https://aistudio.google.com/app/apikey).
2. Open **Settings** (tray → *Settings…*), tick **"Let me chat with the cat"**, and paste the key.

The key is stored **only** in `%AppData%\PixelPaws\settings.json` on your PC — it is **never** sent
anywhere except your chosen LLM provider, and is **never** committed (the repo's `.gitignore` blocks
`settings.json` and `*.key`; see `settings.example.json` for the schema). The provider is pluggable
(`Services/Ai/IAiProvider.cs`) so Groq/Ollama can be added later.

## How it works

| Piece | File |
|-------|------|
| Win32 P/Invoke (window enum, cursor, DPI, hooks, click-through) | `src/DesktopPet/Native/Win32.cs` |
| Per-frame simulation, physics, collision, behaviours | `src/DesktopPet/Engine/PetEngine.cs` |
| Behaviour transitions / personality | `src/DesktopPet/Engine/StateMachine.cs` |
| Window-edge "platforms" detection | `src/DesktopPet/Engine/SurfaceProvider.cs` |
| Sprite-sheet slicing & animation | `src/DesktopPet/Engine/SpriteAnimator.cs` |
| Transparent always-on-top window (the cat) | `src/DesktopPet/UI/PetWindow.xaml(.cs)` |
| Click-through overlay for hearts / toilet paper | `src/DesktopPet/UI/EffectsOverlay.cs` |
| Global keyboard / mouse-wheel hooks | `src/DesktopPet/Services/KeyboardMonitor.cs`, `MouseMonitor.cs` |
| Tray / settings / autostart | `src/DesktopPet/Services/` |
| AI companion (chat, emotion map, cute tools, provider) | `src/DesktopPet/Services/Ai/`, `src/DesktopPet/UI/ChatInputWindow.xaml(.cs)` |

The pet is a borderless, transparent, top-most, no-taskbar window sized to one sprite cell; the engine
moves it by setting `Left`/`Top`. Surfaces (the desktop floor above the taskbar + the top edge of every
eligible visible window) refresh several times a second; the pet falls when it walks off an edge.
Hearts and toilet paper are drawn on one persistent click-through overlay window (not per-effect
windows — that throws inside the render loop).

## The art

Art lives in `src/DesktopPet/Assets/pets/cat/`:

- `spritesheet.png` — a transparent 4×5 grid of 128px cells (20 frames).
- `manifest.json` — describes the grid and which cell indices make up each animation.

Frames are addressed row-major (`index = row * columns + col`) and must face **right** (the engine
mirrors them for leftward movement). Frame order: idle, blink, eat×2, walk×4, sleep×2, fall, drag,
pet×2, stretch×2, typing×2, fast-typing×2.

To regenerate from a new AI-generated collage (e.g. from Gemini), drop the image at
`tools/gemini_cat.png` and run `python tools/slice_gemini.py` — it keys out the background, slices the
frames, normalizes and bottom-aligns them, and writes a clean sheet.

## Notes / next steps

- Multi-monitor with *mixed* DPI is only lightly handled.
- The pet gets onto window tops by being dragged there or when a window appears under it; it doesn't yet
  climb window sides autonomously (Shimeji-style).
- Optional tray app icon: drop an `app.ico` in `Assets/` (falls back to the system icon otherwise).
