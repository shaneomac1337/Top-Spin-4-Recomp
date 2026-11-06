# Top Spin 4 Recomp: help

What the Launcher's Help button opens: setting up, playing, and what to do when something goes wrong.

## What you need

Two files, both from your own copy of the game:

1. **A disc image (`.iso`)** of your own Top Spin 4 Xbox 360 disc. Only one edition works: the disc with media ID `60819732`. The Launcher tells you if the file you picked is another edition or another game.
2. **Title Update 1**, the update file Top Spin 4 received on Xbox 360. The port is built on it and does not run without it.

The Launcher never changes your disc image or update file. It copies the game files it needs (about 6 GB) into the game folder (beside `topspin4.exe`) once. Everything the game and the Launcher write, including your settings and saves, stays in that folder, so copying it copies the whole install and deleting it removes everything. The folder must be one you can write to (not Program Files).

## Dumping your own disc

Make an image of your own Top Spin 4 disc on an Xbox 360 or a PC with a compatible disc drive, using a disc-dumping tool of your choice, and save it as an `.iso` file. Pick that file in the Launcher's setup.

## Getting Title Update 1 from your own console

Title Update 1 is a content package your console stored when it downloaded the update. Copy it from your console's storage (from the `Content` or `Cache` folder on its hard drive or USB drive) to your PC. The file is named like `TU_1A5822P_000000C000000.0000000000083`, with no extension. Pick it in the Launcher's setup.

## Changing settings

Every setting is in the Launcher's **Settings**, one tab per section, so you can set the game up before you play. You can also change them in the game, while playing, on its own Settings page (all but the gamertag):

- In the game's main menu, choose **PC SETTINGS**.
- On a keyboard, press **F4**.
- On a controller, hold **Back** and **Start** together.

To quit, choose **EXIT TO DESKTOP** in the main menu, or close the window.

A fresh install starts on the **Default** Graphics preset: Resolution scale Auto, 16x anisotropic filtering, and the original game's loading-screen flash fixed. **Original** sets the image settings back to the Xbox 360 game's own look, and **Enhanced** adds 4x MSAA. **60 fps cutscenes** (off by default) plays the match intro, replays and changeovers at 60 fps instead of 30, at their original speed.

In either Settings page, press A (or Enter) on a row to change it. To remap a key or button, press A on its row, then the key or button to use; Esc or B cancels. Changes are saved at once to the Settings file in the game folder (`userdata`), so the Launcher and the game show the same values.

Your **gamertag**, the name the game shows for your profile, is on the Launcher's **Profile** tab: type it and press Enter. It can have up to 15 letters, digits and spaces, starts with a letter, and shows from the next start. Your saves stay where they are.

## Achievements

The Launcher's **Achievements** page lists every achievement on your disc. The ones you have earned come first, newest first, with the date you earned them; locked ones are greyed out with a padlock and say how to earn them. When you earn one in the game, an "Achievement unlocked" notice slides in at the bottom of the screen. Your unlocks are saved in the game folder (`userdata\achievements`).

## If the game will not start

Open the Launcher and use **Safe start** under "Trouble starting?". It runs one session with every setting at the original game's behaviour, and leaves your Settings file unchanged.

If the game says it can't run on your graphics card, Safe start won't help: that card lacks a Vulkan feature the game needs. Update the graphics driver. On a PC with two graphics cards, the game uses the discrete one.

After a crash or a graphics failure the Launcher comes back with Safe start up front, and after a crash with **Open crash report** to attach to your issue. Attach the report before you use Safe start: the button goes away once a session closes normally. The report stays in the `logs` folder as `crash-*.zip`.

## Starting without the Launcher

**Create shortcut** on the Launcher's home screen puts "Top Spin 4" on your Desktop and in the Start menu. It starts the game directly with your saved settings, and creating it again replaces the old one. If setup is missing or the game files were moved away, the game opens the Launcher on the setup page and says why.

## Logs

The Launcher's **Open logs** button opens the `logs` folder beside the game. When you report a problem, attach the newest log from the session it happened in, and for a crash the newest `crash-*.zip`. If you used Safe start since then, the newest log is from Safe start, not from the problem.
