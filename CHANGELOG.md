# Changelog

All notable changes to this project are recorded here, in the [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format.

## [Unreleased]

### Fixed

- On a PC with two GPUs, such as a gaming laptop, the game picks the discrete card instead of the first one Windows lists, which was often the integrated one. The shaders' pipeline cache is rebuilt once on those PCs.

### Changed

- Help says to attach the crash report before using Safe start, and that the newest log may be Safe start's own.

## [1.0.0] - 2026-11-01

### Added

- The Launcher's Achievements page shows which achievements you have earned: earned ones come first with their unlock date and an accent edge, locked ones are greyed with a padlock and show how to earn them. The header gives earned and total achievements and gamerscore, with a progress bar.
- An "Achievement unlocked" notice in the manner of the Xbox 360 dashboard's (a capsule with a green trophy badge at the bottom of the screen) replaces the small corner notice.
- Scene scripts can show any achievement's notice without unlocking it: `set achievement_toast <id>`.
- The game's main menu has two new rows: **PC SETTINGS** opens the Settings page, and **EXIT TO DESKTOP** quits the game as closing its window does.
- A **60 fps cutscenes** setting (Graphics, off by default) runs the match intro, close-ups between points, replays and changeovers at 60 fps instead of 30, at their original speed.
- A **Default** Graphics preset, beside Original and Enhanced: the settings a fresh install starts on.
- A **Gamertag** setting on the Launcher's new Profile tab: the name the game shows for your profile instead of "User". Saves stay in the same folder whatever the name.

### Changed

- The player help moved from README.md to HELP.md, which the release ships and the Launcher's Help opens. README.md is now the project page.
- A fresh install now starts on the Native renderer with Resolution scale Auto, 16x anisotropic filtering and Fix original glitches on (the Default preset). MSAA stays at the game's own. A setting you never changed in an existing install moves to the new default too.
- The Original preset is the Xbox 360 game's own look, whatever the defaults are.
- Safe start runs the Classic renderer with every setting at the original game's behaviour, instead of the defaults.
- The in-game Settings page looks like the game's own settings pages: grey and white row cells, an orange selected row, a grey help bar and the button prompts at the bottom.
- The Windows cursor is hidden over the game; it shows while the mouse moves and hides again when it rests.

- The art folder's achievement list keeps each achievement's ID and its "how to earn it" text. An art folder written by an earlier Launcher is read again from the prepared disc on the next start.

### Fixed

- The Launcher's **Help** opens in Notepad when Windows has no app for `.md` files, instead of failing.
- Setup's pipeline step runs once more when the game crashes during it. On AMD the driver could crash now and then while building pipelines; the same step then finished.
