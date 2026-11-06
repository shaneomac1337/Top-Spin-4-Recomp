# TODO

Open work, one line each. The old `.scratch/` tickets these came from are in git history:
`git show 9d16e91:.scratch/<feature>/issues/<file>.md`. Some statuses there were stale, so check
before starting.

## Next

- **Issue #3, Steam Deck court graphics.** Doesn't show on the RX 7900 XT: Kiev and Seoul are clean
  on Native at 2560x1440, and Seoul at `--ts4_native_window_size=1280x800` (2026-11-06). Seoul also
  looked right to the owner on a Deck, handheld at 1280x800, GE-Proton11-7, on the `deck-test` build
  with 0048 and 0049. Kiev and the third clay court are unchecked there. Next: the reporter's session
  log, launch method (Quiver) and whether Classic shows it.
- **Steam Deck frame drops.** The same session (about 13 minutes, Native) averaged about 56 fps, with
  7% of frames logged as hitches at a median of 33 ms and 1,058 in its last minute. The last hitch
  compiled no pipelines and spent about 3 ms in the renderer. The cause isn't known.
- **Issue #2, two-GPU laptops.** Asked the reporter for the crash zip and a `REX_VULKAN_DEVICE=1`
  run. The discrete-GPU fix is on `fix/prefer-discrete-gpu`, unconfirmed on a real hybrid laptop.
- **Issue #5, Classic crash on 11th-gen Intel.** The 1.0.0 crash zip ran on the Intel (`0x9A68`,
  driver 101.2079, May 2022), not the RTX 3060. Native refused it: `shaderInt64` is the only gate
  feature missing from its log (`src/native/vk_context.cpp:153`). Classic then faulted inside
  `igvk64` at the first swap, reading `FFFFFFFFFFFFFFFF` in `vkCmdBeginRendering`
  (`deferred_command_buffer.cpp:225`). The call on the stack has one colour attachment and no depth
  or stencil, and looks valid. Driver bug or bad handle is unknown. Patch 0047 moves this player to
  the NVIDIA, which avoids the crash path but doesn't fix it for Intel-only PCs.
  Reproduced 2026-11-06 on the owner's ProBook 650 G2 (HD 520 `0x1916`, driver 101.2111, Windows
  build 26100, `deck-test`). Classic on Safe start crashes about 2 s in with the same call and
  arguments. `--vulkan_dynamic_rendering=false` crashes in `vkCmdDraw` instead, and
  `--async_shader_compilation=false` crashes in `vkCmdBeginRendering` again, so neither is the
  cause. Native ran 5.6 minutes there, Exhibition included (match on the owner's word). The #2
  UHD 630 survived Classic on Safe start on driver 101.2141. Next: `--vulkan_validation_enabled`
  on the ProBook, then driver 101.2115.
- **Visual C++ runtime isn't shipped.** On a PC without it, `topspin4.exe` and
  `xenosrecomp\XenosRecomp.exe` can't start (`0xC0000135`, seen on the ProBook). The Launcher reports
  "The game stopped before it had read the shaders" and that Play still works on Classic. Ship the
  DLLs beside both, or otherwise cover both, and name a missing DLL in that message. Check on a clean
  PC; the ProBook now has the runtime installed.
- **2-core CPUs and integrated GPUs.** "Too few processor cores" logs on every thread CPU change
  below 6 logical CPUs (`xthread.cpp:914`): 44,209 lines in 5.6 minutes on the ProBook. Its cost isn't
  measured. The default Resolution scale also picked 2x (2560x1440) on the HD 520 and on #5's Intel.
- **Launcher wording for Native's gate.** "Vulkan could not start on this graphics card"
  (`launcher/src/Launcher.Core/PipelineOutput.cs:84`) also shows when Vulkan works and only Native's
  feature gate refuses the GPU. The #5 reporter read it as no Vulkan support.
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
