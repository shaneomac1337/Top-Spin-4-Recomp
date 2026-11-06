# Classic renderer first, then Native renderer; Vulkan only

The Classic renderer comes first, to reach playable, and it stays as the reference renderer. The Native renderer, ported from WWE All Stars, follows and owns render targets, which resolution scaling and MSAA need: upscaling crashes the emulation-style path in Xenia. Both renderers stay in the build side by side. D3D12 is not supported. SVR2011 hit D3D12 device hangs, Proton runs Vulkan natively for Steam Deck, and a second backend would double the test matrix for a solo developer.

## 2026-11: players get Native only

Classic is no longer a player option. It crashed inside Intel's Vulkan driver within seconds of starting, on an HD 520 (driver 101.2111) and on 11th-gen UHD Graphics (101.2079), while Native played a match on the same HD 520. Players reached it by accident: Safe start, the Original preset, and an automatic fallback whenever Native could not start.

So Safe start and the Original preset use Native, the Settings pages have no Renderer row, and a saved `classic` migrates to `native`. When Native can't start, a player's session ends with the reason instead of falling back. A GPU that lacks a feature Native's shaders need, such as `shaderInt64`, can't run the game until the shaders stop needing it.

The code stays. `--ts4_renderer=classic` still selects it, because `scripts/scene_runner.py` checks Native's frames against it.
