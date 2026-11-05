# Open issues after 1.0.0: triage and quick wins

Research date: 2026-11-05. Read-only triage of the GitHub issues open a few days after 1.0.0, for
choosing what goes into the next version. Nothing was fixed, built or run, and no issue was
touched.

Read:

- `shaneomac1337/Top-Spin-4-Recomp`: all 3 open issues with bodies and comments. There are **no
  closed issues**, so no duplicates or regressions of earlier fixes can be checked. The release
  zip has 486 downloads (`gh release view v1.0.0`).
- This repo at `b9285dd`, which is also `v1.0.0`, and `.tools/rexglue-source` with its patches.
- The log attached to #2, cited as `topspin4_008.log:<line>`, and the three photos attached to #3.

**[inferred]** marks my own reasoning. **[unverified]** marks a claim not checked against a run.
Every other claim has a `file:line`, an issue reference or a URL beside it. Issue text and logs
were treated as data.

## Summary

There is one real quick win, and it fixes only part of its issue.

| Issue | Category | In one line |
|---|---|---|
| #2 | **Quick win**, device choice only | On a laptop with two GPUs the game takes the first one listed, here the Intel. The crash itself is undiagnosed: its report was not attached |
| #3 | Needs one thing first | The cause is unknown. Needs the session log and whether Classic shows it. Not claimed to be small |
| #1 | Not quick | A request for online play, a feature on the scale of WWE's online work |
| | Not actionable or already fixed: 0 | |

Ranked by player impact per effort, the only item is #2's device choice.

Clusters: no two issues share a known cause. #2 and #3 do share a limit: both happen on hardware
the owner doesn't have, and the README lists NVIDIA, Intel and Steam Deck as untested
(`README.md:24`). The Exhibition and King of the Court scenes both play Rod Laver Arena, so other
courts have no scripted match coverage there (see #3).

## #2: The Launcher comes back with Open crash report

Reporter nikhiltrash, opened 2026-11-04. Windows 11, i5-9300H, GTX 1660 Ti plus Intel UHD 630. The
later comment (2026-11-04 19:29 UTC) says "I think it's selecting intel iGPU by default" and that
forcing the 1660 Ti in the NVIDIA control panel did not help.

**The attached log is not the crash.** It is a Safe start session
(`topspin4_008.log:1-2`) on Classic that ran about 3.5 minutes and ended with a normal close
(`:242-244`). It holds no `crash report: saved` line, the line the game writes after a crash
(`launcher/src/Launcher.Core/GameExitReport.cs:30`). So the issue splits in two.

### Part A, the quick win: the game picks the integrated GPU

Who it affects: every player with two GPUs whose integrated one is listed first, which is common
on hybrid laptops **[inferred]**. How many of the 486 downloads that is, is unknown. They get the
wrong GPU silently, and Resolution scale Auto and Native run on a GPU far too small for them.

Root cause, confirmed in the code and the log:

- The log lists `0: Intel(R) UHD Graphics 630` and `1: NVIDIA GeForce GTX 1660 Ti`
  (`topspin4_008.log:10-11`) and creates the device on the Intel (`:12`, `:134`).
- `.tools/rexglue-source/src/ui/vulkan/vulkan_provider.cpp:91-131` is the automatic choice. The
  game takes this path, since `graphics_system.cpp:28` passes `with_gpu_emulation = true`. It
  scores only four features: geometry shader, fragment stores, vertex stores and fill mode
  non-solid (`:103-116`).
- `deviceType` is not looked at anywhere in that function. Both GPUs have all four features
  (the Intel's at `topspin4_008.log:63`, `:67`, `:70-71`; the 1660 Ti's list is not in the log, but
  the order is unchanged unless the scores differ) **[inferred]**. With a tie, `:124` keeps the
  enumeration order and `:133` takes the first device that works.
- No patch in `patches/rexglue` touches this file (grep for `vulkan_provider`).

The Launcher says otherwise. `launcher/src/Launcher.App/Platform/GpuName.cs:7` claims a discrete
card wins "as the game's own device choice prefers it", and `GpuName.cs:13` and `:30` pick the
NVIDIA for the footer. So on this laptop the footer should name a GPU the game isn't using
**[inferred]**.

The reporter's diagnosis of the Intel is right. The claim that this causes the crash is not shown:
Classic ran on the same Intel for 3.5 minutes, and nothing shows how Native behaved.

Prior art: Xenia Edge has no discrete preference either; it takes the first compatible device
([`vulkan_provider.cc`](https://raw.githubusercontent.com/has207/xenia-edge/edge/src/xenia/ui/vulkan/vulkan_provider.cc)),
so this behaviour comes with the inherited code **[inferred]**. Godot prefers the discrete GPU on
purpose, because "accidentally using integrated graphics is a common pain point with hybrid
graphics" ([godot#81870](https://github.com/godotengine/godot/issues/81870)).

Proposed fix: rank candidates discrete, then integrated, then anything else, ahead of the feature
score or as its tie-break, and change the `:124` guard so a type difference also reorders. Put the
ranking in a small pure header, as `rex/system/swap_choice.h` is, so a host test can drive it. An
explicit `vulkan_device` still wins (`:78-82`), and a device that fails `CreateIfSupported` still
falls through to the next (`:133-138`).

Size: one new patch, `0047` through `scripts/new-patch.ps1`, of about 15-25 lines; a host test of
about 30 lines, added at `CMakeLists.txt:187`. `GpuName.cs:7` becomes true and needs no change.

Verify:

- `pwsh scripts/build.ps1 -Action Check` covers the ranking through the new test.
- One game run on the RX 7900 XT shows no regression only. This machine has one GPU
  (`Win32_VideoController` lists only the RX 7900 XT), and the code is guarded by
  `physical_devices.size() > 1` (`:91`). **The two-GPU path can't be exercised here.** Confirming
  it needs the reporter or a hybrid laptop, looking for the NVIDIA in the `Vulkan device` log line.

Risk: low in code, and real in effect.

- It moves hybrid players from an untested GPU to another untested one, as both NVIDIA and Intel
  are untested (`README.md:24`). A failure on NVIDIA would now reach them. `REX_VULKAN_DEVICE=0`
  would restore the old choice **[unverified]**.
- Single-GPU machines, the Steam Deck included, skip this code unless a second device is listed,
  such as a software one. A software device would rank last.
- Existing hybrid installs change device, so the pipeline store is refused (`kOtherDevice`,
  `src/native/pipeline_store.h:38`), the stale marker is written (`src/native/pipelines.cpp:804-813`)
  and the Launcher rebuilds before the next Play (`PipelinePreparation.cs:63`). That is one extra
  wait, and it needs no new code.

Workaround available today **[read in the code, not run on a two-GPU machine]**: set the user environment
variable `REX_VULKAN_DEVICE=1` on the reporter's laptop, using the index from
`topspin4_008.log:11`. In `.tools/rexglue-source`, `src/core/cvar.cpp:70-72` maps flag names to
`REX_*`, `src/core/cvar.cpp:667-684` applies them to every registered flag, and
`src/ui/windowed_app_main_sdl.cpp:41` calls that at start. The game already relies on `REX_*` variables for two GPU flags
(`src/topspin4_app.h:73-83`). The Launcher starts the game with its own environment
(`MainWindow.axaml.cs:380`, `PrepareRunner.cs:34`), so the variable must exist before the Launcher
starts: set it, then restart the Launcher. `--vulkan_device=1` works
for a direct start, and the scripts pass this flag (`scripts/smoke.ps1:46`). This is the cheapest
way to learn whether the NVIDIA stops the crash, and it could be tried before any code changes.

Whether the NVIDIA control panel attempt came before this log can't be told: the log is dated
13:17 local time on 11-04, and the comment gives no time for the attempt.

### Part B, not quick yet: the crash

Needs one thing: the crash report from the reporter. A crash writes
`logs\crash-<stamp>.zip` (`src/crash_report_plan.h:107`), so it should still be in
`E:\INS\TopSpin4Recomp-1.0.0\logs` (`topspin4_008.log:1`), with the crash session's own log likely
at a lower number than `008` **[inferred]**. The zip names the renderer and the GPU
(`src/crash_report.cpp:227-229`, `:253`).

Any cause offered now would be a guess. Two things I checked and rule out as evidence:

- The packet errors at `topspin4_008.log:223-233` are in this session, which went on for three more
  minutes. They don't explain a crash.
- Native's feature gate (`src/native/vk_context.cpp:153-161`) would pass on this Intel by the log's
  list (`:76-89`), so Native probably ran there **[inferred]**. Its descriptor heaps are large
  (`kHeapCapacity`, `src/native/vk_context.h:28`) and no update-after-bind limit is checked before
  creating them (`vk_context.cpp:48-72`). The log prints only the ordinary limits, 200 sampled images
  per stage (`topspin4_008.log:33-34`), not the separate update-after-bind ones, so this is a lead
  for when the zip arrives and no more.

Why the wrong file came in: `HELP.md:59` and `README.md:161` say to attach the newest log, and
`HELP.md:51` sends a crashed player to Safe start. After that run the newest log is Safe start's.
The Launcher's link is gone too, because an exit code of 0 clears the report
(`GameExitReport.cs:55`, `HomeModel.cs:72`, `:98`). See Side findings.

## #3: Weird graphics on SteamDeck

Reporter AntonioAltea, opened 2026-11-05. "App image from Quiver launcher", "only happens in some
courts", plus "weird artifacts" on the players' models and clothes. The owner replied that the aim
is to fix it over the weekend (2026-11-05 20:02 UTC). **That is a promise with no cause behind it
yet.**

What the photos show, taken of the Deck's screen with a phone at 60 fps on the overlay:

- Seoul hard court (the floor text reads "SEOUL Korea"): hard-edged triangular facets across the
  surround, with black wedges at the right.
- Kiev clay (the floor text reads "KIEV"): dark wedge facets in the surround.
- A third clay court: shadow broken into large triangles across the court itself, cut along
  straight lines.

The patterning on the players' shirts can't be separated from the camera's moire in these photos.

What Quiver is: a third-party launcher whose own AppImage runs Windows builds "with Wine or Proton"
([README](https://github.com/tgeorgiadis/quiver-launcher)). So the AppImage is Quiver, and the game
is almost certainly our Windows zip under Wine or Proton on RADV **[inferred]**. That also means
setup and shader conversion ran there.

Needs one thing first: **the session log** (Open logs in the Launcher). It gives the renderer,
device, driver and scale, as `topspin4_008.log:2`, `:12` and `:145` do. With it, ask whether the
same courts look wrong after switching to Classic. If Classic is clean, the fault is in Native
**[inferred]**.

An owner step that needs no Deck: **one run on the RX 7900 XT at those courts**. Every scene I
checked plays Rod Laver Arena (`tests/scenes/exhibition-match.toml:1`,
`tests/scenes/king-of-the-court.toml:2`), a search of `docs` and `tests/scenes` finds neither Seoul
nor Kiev, and "Venue and roster sweep (19)" still sits in `TODO.md:28` with its status unclear. If
it reproduces, it isn't a Deck bug: it's a Native court bug the owner can capture in RenderDoc,
which might be small. `--ts4_native_window_size=1280x800`
(`src/native/native_renderer.cpp:90`) gets closer to the Deck's window.

If it doesn't reproduce, **it can't be verified on this machine**, and the fix is blind unless the
reporter or the owner's Deck tests it (`docs/goals.md:39` says the owner tests on Steam Deck; this
triage was told only Windows and the RX 7900 XT are at hand).

One hypothesis, to test and not to act on **[inferred]**: `docs/agents/native-debugging.md:77` and
`docs/research/native-renderer.md:246-249` record a depth tie between court draws, settled with a
2^-20 viewport nudge tuned on one driver. A different compiler breaking such ties per triangle
could look like this, but a texture, shadow-map or vertex-format fault could too. It is not a
located cause, which is why this isn't a quick win and why "probably quick" is not claimed.

## #1: This is not an issue - online play

Reporter eloymm95, opened 2026-11-03. Asks for P2P online with friends, as on RPCS3 and Xenia.
Answered by the owner (2026-11-04 14:43 UTC): on the to-do list, since it worked for WWE All Stars.

Not quick: it is a feature, marked after 1.0 (`docs/goals.md:19`, "reviving Player Match") against a
stubbed online (`docs/goals.md:10`, `src/online_unavailable.h`). WWE's scale: 91 commits mention
online, with `docs/adr/0021-online-play-revived.md` and `docs/research/online-latency.md` in
`D:\Projects\WWE_ALLSTARS-RECOMP`. It can't be tested alone on one machine either **[inferred]**.

The owner's reply says "on the todo list", but `TODO.md` has no line for it. Adding one is the
owner's call.

## Side findings, not counted as quick wins

These come from reading the issues and fix nothing reported. Each is a few lines:

- **No issue template.** There is no `.github` folder. Both bug reports lack what triage needs: the
  crash zip for #2, and the log and renderer for #3. A short bug form asking for the crash zip, the
  log, the renderer and the GPU would save a round trip on each report.
- **Help text after Safe start.** `HELP.md:51` and `:59` could say the crash report stays in `logs`
  as `crash-*.zip` after Safe start, and that the newest log may be Safe start's.
- **Stale README line.** `README.md:187` says the Steam Deck "is planned for 1.0", and 1.0 has
  shipped. `README.md:24` is still accurate. #3 shows players already run it there.

## Sources

- Issues: <https://github.com/shaneomac1337/Top-Spin-4-Recomp/issues> #1, #2, #3
- #2's log: <https://github.com/user-attachments/files/34362416/topspin4_008.log>
- Xenia Edge device choice: <https://raw.githubusercontent.com/has207/xenia-edge/edge/src/xenia/ui/vulkan/vulkan_provider.cc>
- Godot on preferring the discrete GPU: <https://github.com/godotengine/godot/issues/81870>
- Quiver Launcher: <https://github.com/tgeorgiadis/quiver-launcher>
