# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
the downloads; this file covers how the code is put together and the rules that keep it working.

## What this is

A frontier trading game in the spirit of Ozark Softscape's 1983 M.U.L.E., in C# on .NET 10 and Avalonia 12:
one human colonist and three computer colonists on an original world, Verdance. One self-contained
executable per platform (Windows, macOS and Linux, x64 and ARM). The pictures are drawn in code
(`Views/Sprites.cs`), and the sounds and tunes are synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVAMule.slnx                         # the SDK version is in global.json (10.0.400 or later)
dotnet test AVAMule.slnx                          # every test runs headless: no window opens
dotnet run --project src/AVAMule                  # play it; --no-audio for silence
MULE_SHOTS=$PWD/Images dotnet test tests/AVAMule.Tests --filter ScreenshotTests   # refresh the README's pictures
python3 tools/make_sounds.py                      # rebuild src/AVAMule/Sounds (standard-library Python only)
```

`Directory.Build.props` sets `net10.0`, `TreatWarningsAsErrors`, nullable reference types and implicit usings
for every project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVAMule/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, attract, play, initials, high scores), the phases of a month and
    the fixed-step loop.
  - `GameEngine.Month.cs` covers the land grant, the development turn in town and on the map, production and
    events. `GameEngine.Auction.cs` runs the live auctions.
  - `Colony.cs` has `Controls`, the terrain, goods and races, and `Colony.Generate` for the map.
  - `Computer.cs` decides for the computer colonists: land, what to make, and how to haggle.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVAMule/Views/`: `GameCanvas` draws the map, town and auctions (`GameCanvas.Draw.cs`) and runs the
  timer; `Sprites.cs` holds the 16x16 pictures.
- `src/AVAMule/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `src/AVAMule/Assets/Fonts/`: the VT323 font, under the OFL.
- `tests/AVAMule.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`: gameplay, screenshots and the Shift release
  case. `Helpers.cs` adds `Idle`, which ticks the engine with no input.

## Rules that keep it working

- **Fixed steps.** `GameEngine` collects frame time (at most 0.25 s a frame) and advances in fixed 1/60 s steps
  (`GameEngine.Step`), so tests can play whole months deterministically from a seed. Draw random numbers from
  the engine's seeded `_rng`, never from `Random.Shared`. `Colony.Generate` takes its `Random` from the caller.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the game and draws it.
- **Input goes through `InputHandler`.** `Controls.Dy` is -1 for up and 1 for down. Keys, D-pad and stick must
  all keep that sign. Test new controls through `InputHandler`, not by building `Controls` directly.
  - Keys are tracked by `PhysicalKey`, because on some Linux setups Avalonia reports the release of Shift as
    CapsLock. `ShiftReleaseTests` covers this.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up; a player's own `ALSOFT_CONF` is left alone.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVAMule`); tests load
  the table from a fresh temp folder and set `Silent = true` on the engine.
- **Original content.** The planet, peoples, goods and wording are this game's own. Keep new text and names
  original too.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as
workflow artifacts. Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a
proper release. A release also asks the portfolio site to rebuild.
