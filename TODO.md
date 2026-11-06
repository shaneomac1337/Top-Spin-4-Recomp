# TODO

Open work, one line each. The old `.scratch/` tickets these came from are in git history:
`git show 9d16e91:.scratch/<feature>/issues/<file>.md`. Some statuses there were stale, so check
before starting.

## Next

- **Issue #3, Steam Deck court graphics.** Doesn't show on the RX 7900 XT: Kiev and Seoul are clean
  on Native at 2560x1440, and Seoul at `--ts4_native_window_size=1280x800` (2026-11-06). Next: the
  owner's own Deck under Proton, then the reporter's session log and whether Classic shows it.
- **Issue #2, two-GPU laptops.** Asked the reporter for the crash zip and a `REX_VULKAN_DEVICE=1`
  run. The discrete-GPU fix is on `fix/prefer-discrete-gpu`, unconfirmed on a real hybrid laptop.
- **Intermittent hang entering Swing, one form unexplained.** Patch 0048 fixes the ring race behind
  the 2026-11-06 freeze (packet overflows at the Swing load, GPU thread in `WAIT_REG_MEM`, loading
  thread in `sub_88292718`), though that freeze itself was never reproduced on demand. The
  2026-11-05 form differs: the main thread spins in `sub_88291558` (from `sub_88292000`) right after
  `SetInterruptCallback(88292A60, ...)`, polling what looks like the GPU's ring read position. The
  same race could cause it, unproven. If any hang recurs, leave the game open, take stacks with
  `scripts\lldb.cmd`, and read `poll_reg_addr`, `ref` and `mask` in `WAIT_REG_MEM`.
- **Ring move drops old-ring work.** 0048 abandons packets still queued on the old ring when a
  Module moves it. A dropped swap would leave `GuestSwapCount` one ahead for the session, and a
  dropped fence write could strand a waiting thread. Neither has been seen, and the logs can't
  rule them out, since `ExecutePrimaryBuffer` returns the write index even when it abandons a batch.
- **Online play (issue #1).** Revive Player Match (`docs/goals.md`, After 1.0). WWE All Stars'
  online work is the starting point (`docs/adr/0021-online-play-revived.md` in that repo).
- **Code-sign the release.** Defender's ML verdict that deleted XenosRecomp came from the game exe
  having no version information (docs/research/defender-false-positive.md, fixed); a later build
  can still be flagged. Signing `topspin4.exe`, the Launcher and `XenosRecomp.exe` is the lasting fix.
- **Memexport on Native.** The 64 Module vertex shaders XenosRecomp patches 0010-0012 let into
  the pack are memexport shaders (GPU compute writing to memory, explicit fetches from fetch
  constant 95 with no vertex elements). Native converts them but drops the exports and reads 0
  for those fetches, so a draw with one does nothing useful. None was met in an exhibition match.
- The Launcher has no Repair button; `SetupFlow.Repair()` exists in the model only.

## 1.0 (from pc-port-1-0)

- Title screen on Classic with no server wait (08, needed info)
- Settings system and renderer switch (10)
- Full Exhibition match vs CPU (13)
- Saves (14)
- Online menus fail cleanly (15); the message in the game's own popup (31)
- Locked 60 with even frame pacing (16)
- Local 2P and doubles (17)
- Academy (18)
- Venue and roster sweep (19)
- Cutscenes and cross-fades (20)
- Career start (21) and complete (22)
- King of the Court (23)
- Remapping and keyboard/mouse (24)
- Agassi outfit unlock, 3D forced off (25)
- Native parity everywhere, made default (29)
- Release candidate and Steam Deck (30)
- Boot without an audio device (32)

## Owner checks waiting

- 60 fps cutscenes: a replay at 60 at its original length, and faces in close-ups (no one-frame flicker once per clip loop)
- Game settings 01-09, 11-15: owner run of each
- Launcher UX 01-03, 07, 08 and Launcher look 04: owner run of the new Launcher
- Launcher redesign (rail, hero, card row, Achievements page; launcher-ux.md §6): owner review of the look, with keyboard and pad
- 4x Resolution scale without stutter (image-quality 14, needed info)
