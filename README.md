# PixelPaws — a desktop pet for Windows

A Comnyang-style desktop mascot: a cute pixel-art **cat** that lives on top of your screen,
wanders, watches your cursor, naps, eats, can be dragged around, walks along the top edges of your
open windows, reacts when you type, stretches on a timer, and unrolls toilet paper when you scroll. 🐾

Built with **C# / WPF on .NET 8**. Original art (not Comnyang's) — this is a clone of the *mechanism*.

<!-- DEMO: record the cat (wander / walk-on-windows / typing reaction / toilet paper),
     save it to docs/demo.gif, then uncomment the line below.
![PixelPaws demo](docs/demo.gif)
-->

## Install

**[⬇️ Download PixelPaws.exe](https://github.com/jordansbc/pixelpaws/releases/latest)** — then run it.

That's the entire install. One file, no .NET runtime, no git, nothing to unzip — the cats are
embedded in the executable. It puts an icon in your system tray; right-click it for settings.

> Windows will likely warn about an unrecognised app the first time (the build isn't code-signed).
> Choose **More info → Run anyway**.

Once it's running it checks for updates on its own and can install them with one click.

### Building from source

You'll need the **.NET 8 SDK** (`winget install Microsoft.DotNet.SDK.8`).

```powershell
git clone https://github.com/jordansbc/pixelpaws
cd pixelpaws
dotnet build -c Release
.\src\DesktopPet\bin\Release\net8.0-windows\PixelPaws.exe
```

Or double-click **`install.bat`**, which builds it, puts a shortcut on your Desktop, and enables
auto-start. Other helpers: `build.bat` (build only), `uninstall.bat` (remove shortcut + autostart),
`update.bat` (pull + rebuild — source checkouts update via git rather than by downloading a release).

Run the tests with `dotnet test`, and build the release artifact with `.\scripts\publish.ps1`.

## What it does

- **Wanders & watches** — strolls around, and turns to face your mouse cursor while idle; occasionally chases it.
- **Walks on windows** — detects the top edges of your open windows and walks/sits along them. Falls and lands when it steps off.
- **Drag** — left-click and drag to pick it up; release to drop (it falls to the nearest surface).
- **Pet it** — hover the mouse over the cat and it shows a happy face with floating hearts.
- **Naps & eats** — random sleep (with Zzz) and eating animations.
- **Typing reaction** — when you type, the cat types along at a tiny keyboard; type *fast* and it turns **red** and frantic. Calms down shortly after you stop.
- **Stretch reminder** — on a timer (off / 15 / 30 / 45 / 60 min) the cat stretches and a cute "stretch time!" popup nudges you to stretch too.
- **Toilet paper** 🧻 — scroll the mouse wheel and the cat unrolls a trail of toilet paper beside it (it retracts when you stop).
- **Knows when to behave** 🤫 — while you're presenting, screen sharing, gaming full-screen, or have
  focus assist on, the cat settles down: no zoomies, no hunting, no talking. It can hide entirely
  if you'd rather. (Settings → *Behaviour* → Quiet mode.)
- **Picks up where it left off** — wakes up on the monitor you left it on, still sleepy if it was sleepy.
- **Multi-monitor** — roams your whole desktop, including screens at different resolutions, DPI
  scalings and heights.
- **Gets out of the way** — pausing from the tray, or hiding during a presentation, detaches the
  render loop entirely rather than just freezing the cat, so a hidden pet really does cost nothing.
- **Tray icon** → Settings / Pause / AI companion / Support / Quit. Settings persist to `%AppData%\PixelPaws\settings.json`. Single instance only.

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
  you're doing) when you're around. Stays quiet during quiet mode. Toggle in Settings.
- **Types its replies out** as they stream in, rather than popping in all at once.

### Two brains to choose from

| | **Gemini** (default) | **Ollama** |
|---|---|---|
| Cost | Free tier | Free |
| Needs | An API key | [Ollama](https://ollama.com) running locally |
| Privacy | Prompts go to Google | Nothing leaves your machine |
| Offline | No | Yes |

**Gemini:** get a free key at [aistudio.google.com](https://aistudio.google.com/app/apikey), open
**Settings → AI**, tick *"Let me chat with the cat"*, and paste it in.

**Ollama:** install Ollama, `ollama pull llama3.2`, then pick *Ollama* as the brain in Settings.

Either way, hit **Test connection** — it tells you exactly what's wrong (bad key, out of quota,
server not running) instead of leaving the cat mysteriously silent.

Your key is stored **only** in `%AppData%\PixelPaws\settings.json` on your PC. It is **never** sent
anywhere except your chosen provider, and is **never** committed (the repo's `.gitignore` blocks
`settings.json` and `*.key`; see `settings.example.json` for the schema). The provider is pluggable
via `Services/Ai/IAiProvider.cs`.

## How it works

| Piece | File |
|-------|------|
| Win32 P/Invoke (window enum, cursor, DPI, hooks, click-through) | `src/DesktopPet/Native/Win32.cs` |
| Per-frame simulation, physics, collision, behaviours | `src/DesktopPet/Engine/PetEngine.cs` |
| Behaviour transitions / personality | `src/DesktopPet/Engine/StateMachine.cs` |
| Window-edge "platforms" detection | `src/DesktopPet/Engine/SurfaceProvider.cs` |
| Multi-monitor layout (bounds + per-screen floors) | `src/DesktopPet/Engine/DesktopGeometry.cs` |
| Standing / landing maths (pure, unit-tested) | `src/DesktopPet/Engine/Platforms.cs` |
| Sprite-sheet slicing & animation | `src/DesktopPet/Engine/SpriteAnimator.cs` |
| Transparent always-on-top window (the cat) | `src/DesktopPet/UI/PetWindow.xaml(.cs)` |
| Click-through overlay for hearts / toilet paper | `src/DesktopPet/UI/EffectsOverlay.cs` |
| Global keyboard / mouse-wheel hooks | `src/DesktopPet/Services/KeyboardMonitor.cs`, `MouseMonitor.cs` |
| Tray / settings / autostart | `src/DesktopPet/Services/` |
| Embedded art loading (exe or disk) | `src/DesktopPet/Services/AssetSource.cs` |
| Hook liveness watchdog | `src/DesktopPet/Services/HookWatchdog.cs` |
| Update check + self-replace from GitHub Releases | `src/DesktopPet/Services/UpdateService.cs` |
| Tests | `tests/DesktopPet.Tests/` |
| AI companion (chat, emotion map, cute tools, provider) | `src/DesktopPet/Services/Ai/`, `src/DesktopPet/UI/ChatInputWindow.xaml(.cs)` |

The pet is a borderless, transparent, top-most, no-taskbar window sized to one sprite cell; the engine
moves it by setting `Left`/`Top`. Surfaces (the desktop floor above the taskbar + the top edge of every
eligible visible window) refresh several times a second; the pet falls when it walks off an edge.
Hearts and toilet paper are drawn on one persistent click-through overlay window (not per-effect
windows — that throws inside the render loop).

## The art

Art lives in `src/DesktopPet/Assets/pets/cat/` and is **compiled into the exe**, which is what
makes a release a single file. A matching folder on disk still wins, so you can drop an
`Assets\pets\<name>\` folder next to `PixelPaws.exe` to override a cat or add your own without
rebuilding.

- `spritesheet.png` — a transparent 4×5 grid of 128px cells (20 frames).
- `manifest.json` — describes the grid and which cell indices make up each animation.

Frames are addressed row-major (`index = row * columns + col`) and must face **right** (the engine
mirrors them for leftward movement). Frame order: idle, blink, eat×2, walk×4, sleep×2, fall, drag,
pet×2, stretch×2, typing×2, fast-typing×2.

To regenerate from a new AI-generated collage (e.g. from Gemini), drop the image at
`tools/gemini_cat.png` and run `python tools/slice_gemini.py` — it keys out the background, slices the
frames, normalizes and bottom-aligns them, and writes a clean sheet.

## Notes / next steps

- The sprite sheet is **full** (4×11 = 44 cells, all used). Adding a new animation means extending
  the sheet and re-running the recolour tooling across all seven coats first.
- The pet gets onto window tops by being dragged there or when a window appears under it; it doesn't yet
  climb window sides autonomously (Shimeji-style).
- Releases aren't code-signed, so Windows SmartScreen warns on first run until the build earns
  reputation.

### Troubleshooting

Set `PIXELPAWS_DEBUG=1` before launching to write a log to `%Temp%\pixelpaws_debug.log`
(monitor layout, quiet-mode changes, AI failures). Crashes always land in
`%Temp%\pixelpaws_crash.log`. **Settings → System → Open log folder** gets you there.

## Credits

- **Inspiration** — the *mechanism* is inspired by [Comnyang](https://comnyang.com/) and the
  broader Shimeji-style desktop-pet genre. All of PixelPaws' code and sprite art are original
  (the cats were generated/edited from scratch, not taken from Comnyang).
- **Emotion → animation idea** — borrowed from
  [Open-LLM-VTuber](https://github.com/Open-LLM-VTuber/Open-LLM-VTuber): the LLM reply carries a
  hidden emotion tag that drives the cat's animation.
- **AI** — the optional companion runs on Google's free [Gemini](https://aistudio.google.com/)
  API and is off by default; your key stays on your machine.

## License

[MIT](LICENSE) — free to use, modify, and share. If you enjoy it, a ⭐ on GitHub is appreciated.
