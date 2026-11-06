# Top Spin 4 Recomp

An unofficial PC port of the Xbox 360 game Top Spin 4 (2011), built by recompiling the game's own code and supplying the game data from the player's disc.

## Inputs

**Reference build**:
The one game build the port supports: the Xbox 360 disc with media ID `60819732` and Title Update 1 applied, so every module is v0.0.1.3.
_Avoid_: base build, retail build (when TU1 is meant)

**Module**:
One of the game's three executable images: Boot (`default.xex`), Loader (`Loader_DLL.xex`) and Swing (`Swing_DLL.xex`).
_Avoid_: DLL (Boot is not one), binary

**Boot**:
The small startup module (`default.xex`, originally `Boot.exe`) that loads Loader.

**Loader**:
The module (`Loader_DLL.xex`) that drives boot and loading screens and loads Swing.

**Swing**:
The module (`Swing_DLL.xex`) that holds the game itself.

## Game modes

**Exhibition**:
An offline single match against the CPU or a local player.
_Avoid_: Player Match (that is online), quick match

**Career**:
The offline single-player mode where a created player goes through a calendar of tournaments and earns XP.
_Avoid_: World Tour (that is online), My Career

**Academy**:
The offline tutorial and practice modes (Top Spin Academy and practice ground).

**King of the Court**:
The offline party mode where players rotate in to challenge the winner of each point.

**Player Match**:
An online match against another player. Stubbed in 1.0.
_Avoid_: Exhibition

**World Tour**:
The online career-like mode. Stubbed in 1.0.
_Avoid_: Career

## Correctness

**Oracle**:
Xenia Edge running the Reference build. It is the reference for correct behaviour: a divergence from it counts as a port bug unless shown to be an Edge bug.
_Avoid_: reference emulator, ground truth

**Baseline**:
The recorded behaviour of the Oracle (match completion, glitches, frame rate, load times, resolution) that the port is measured against.

**Original bug**:
Behaviour present on the real console. It is kept by default, not fixed.

## Presentation

**Classic renderer**:
The renderer that draws the game through ReXGlue's emulation-style GPU path. It is the reference renderer: scene runs use it, and players do not (ADR-0005).

**Native renderer**:
The renderer that turns the game's Direct3D 9 calls into native Vulkan work and owns render targets.
_Avoid_: new renderer, HD renderer

**Shader pack**:
The game's shaders converted by XenosRecomp, in one file (`topspin4.shaderpack`) in the cache folder, which the Native renderer loads at start. The Launcher builds it during setup from the shaders in the player's own game files (the archives and the Loader and Swing Modules), so it is never shipped (ADR-0006). Without a usable pack the game runs on the Classic renderer.
_Avoid_: shader cache (that is the whole cache folder)

**Top-up**:
A shader the game met that the shader pack lacked. The game writes it to the cache's `shader-topup` folder; the Launcher's Module shader step fills the same folder with the Modules' shaders before the pack is built.

**Enhancement**:
A setting that changes the game away from original behaviour. Some Enhancements are on by default; each can be set back to original behaviour.
_Avoid_: fix, improvement (when it is an opt-in setting)

## Settings

**Settings file**:
The player's saved settings. It is the one source of truth that the Settings page and the Launcher both read and write.
_Avoid_: config, cvars file

**Settings page**:
The in-game screen, usable with a controller, where players change settings while playing. It opens from a PC SETTINGS row in the game's own main menu, from F4, or with a pad combo. The Launcher shows the same settings before the game starts.
_Avoid_: overlay, F6 menu, options menu (that is the game's own)

**Launcher**:
The separate program players open to start the game. On first run it prepares the disc and Title Update 1, then the shader pack; after that it is the home screen for playing and changing settings. The game can also start without it, from a Direct shortcut.
_Avoid_: setup tool (when the GUI is meant)

**Disc art**:
The pictures the Launcher shows (background, box art, logo, setup background and the title icon), read from the player's own disc and written to `userdata\art` beside the game exe. The Launcher never ships any of it. A source the disc lacks gives no picture; a damaged one is logged and gives none.
_Avoid_: assets, skin, theme (the theme only reads the Disc art)

**Direct shortcut**:
A shortcut the Launcher creates that starts the game straight away, without showing the Launcher, using the saved Settings file.
_Avoid_: unattended shortcut, quick launch

**Launch option**:
A setting given when the game is started, on the command line or in the environment. It wins over the Settings file for that session and is not saved.
_Avoid_: override, flag (when the player-facing idea is meant)

**Safe start**:
A session that runs every setting at original behaviour, on the Native renderer, without changing the Settings file.
_Avoid_: reset (that changes the file)

**Preset**:
A named group of setting values chosen together: Original (original behaviour), Default (what a fresh install starts on) and Enhanced (the best look).

**Resolution scale**:
The Enhancement that renders the game at a whole multiple of its original 1280x720 and fits the result to the window. Auto picks the smallest multiple that covers the 16:9 area of the window the frame is shown in. The largest multiple is 3x.
_Avoid_: internal resolution, render resolution, supersampling (when the setting is meant)
