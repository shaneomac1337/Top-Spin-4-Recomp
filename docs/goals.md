# Goals and way of work

Settled 2026-10-24. Terms are defined in [GLOSSARY.md](../GLOSSARY.md); the lasting decisions are in [docs/adr/](adr/).

## What 1.0 is

An enhanced port (ADR-0001) of the Reference build (ADR-0002), recompiled with ReXGlue (ADR-0003), for Windows x64 and Steam Deck via Proton.

- **Modes that must work start to finish:** Exhibition, Career, Academy, King of the Court, with saves.
- **Online:** stubbed. Title-server login and the DLC check are short-circuited, so boot doesn't wait on a dead server. Player Match and World Tour fail with a clean "unavailable" message.
- **Frame rate:** locked at the game's 60 fps tick. The simulation is tied to frame rate, so unlocking it changes game speed.
- **Renderer:** the Native renderer for players, with the Classic renderer kept as the reference for scene runs (ADR-0005). 1.0 waits until the Native renderer matches Classic in every 1.0 mode.
- **Enhancements in 1.0:** resolution scaling, MSAA, anisotropic filtering and CAS sharpening, and button remapping.
- **Extras:** the Agassi preorder outfit is available through an unlock option. Stereoscopic 3D stays disabled.
- **Saves and settings:** stored in `userdata` beside the game exe (portable install, ADR-0009), in the game's own save format, so Career saves can be copied between the Oracle and the port.

## After 1.0

Mods (designed in, shipped later), frame rates above 60 (needs a decoupled tick or interpolation), reviving Player Match, skipping intro videos, an XP / career-pace slider, and a native Linux build once ReXGlue's Linux module loading works.

## Out of scope

PS3, Wii, macOS, D3D12, the base (pre-TU1) build, loading new guest code from DLC, and reimplementing the World Tour server.

## Milestones

| | Milestone | Done when |
|---|---|---|
| M0 | Oracle Baseline | Edge baseline of the Reference build is in `docs/research/`: a full match, glitches, fps, load times, native resolution |
| M1 | Codegen | All three Modules generate and compile |
| M2 | Boot | Reaches the title screen on the Classic renderer |
| M3 | First match | A full Exhibition match plays through |
| M4 | Offline complete | Every 1.0 mode works, with saves |
| M5 | Native parity | Native renderer matches Classic across 1.0 modes |
| M6 | Release candidate | Enhancements, launcher and disc ingestion, Steam Deck pass |

## Way of work

- **Roles:** Claude does research, RE, manifest and seed work, hooks, and runtime and renderer code. The owner makes the final calls on gameplay feel, tests on hardware and Steam Deck, and accepts milestones.
- **Done means verified:** Check passes, and a change that can alter runtime behaviour gets one game run (smoke or scene). An Oracle comparison is made when the owner asks for one or a visual difference needs settling. The owner play-tests at each milestone.
- **Oracle:** Xenia Edge on the Reference build, with Canary as a second opinion. Original bugs are kept; the owner may pick some to fix behind the Fix original glitches setting, off by default (the white flash after a loading screen, `src/load_flash.h`).
- **Repository:** contains no game data or generated code (ADR-0006). Upstream tools are fetched by `scripts/bootstrap.ps1` at pinned versions, and our changes to them live as numbered files in `patches/`.
- **Commits:** Claude commits small, verified steps to `main` without asking. Pushing, and anything else outward-facing, needs the owner's OK. The repo stays private on GitHub until M2, then goes public.
- **Open work:** [TODO.md](../TODO.md), one line per item.
- **Shared code:** copied from WWE All Stars and allowed to diverge (ADR-0004).
