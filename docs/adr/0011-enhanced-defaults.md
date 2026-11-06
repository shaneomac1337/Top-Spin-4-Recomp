---
status: accepted
---

# Ship enhanced defaults; original behaviour stays one choice away

A fresh install starts on the Native renderer with Resolution scale Auto, Anisotropic filtering 16x and Fix original glitches on (the Default Preset). This replaces ADR-0001's rule that every Enhancement defaults to original behaviour: players expect a PC port to look its best without a trip to the settings, and Native is the renderer the port is built around (ADR-0005). The faithful reference ADR-0001 protected is kept by name instead of by default: the Original Preset holds the 360's values, and Safe start runs every setting at original behaviour, so the recovery session is the most conservative one rather than the shipped look. (Both used Classic until ADR-0005's 2026-11 change.)

MSAA stays at Original by default, so the Default Preset is not Enhanced. The shipped pipeline key list holds keys at the default sample count only (ADR-0010); a 4x default would leave a fresh install compiling its match pipelines during play. 60 fps cutscenes and Sharpening also stay off.
