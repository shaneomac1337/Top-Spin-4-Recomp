// Native can't start, so a player's session ends with the reason instead of drawing with Classic
// (ADR-0005). The game logs one line and exits with its own exit code. The launcher reads both
// (launcher/src/Launcher.Core/GameExitReport.cs): keep the wording and the code.

#pragma once

#include <string>
#include <string_view>

#include "native/renderer_fallback.h"

namespace topspin4::native {

// "NATV" in ASCII; GameExitReport.NativeUnavailableExitCode in the launcher.
inline constexpr int kNativeUnavailableExitCode = 0x4E415456;

// The launcher shows what follows this in the log line.
inline constexpr std::string_view kNativeUnavailableLogPrefix = "native renderer: cannot start: ";

// The reason in the player's words. `gpu` and `missing` matter for an unsupported GPU only.
inline std::string NativeUnavailableMessage(Fallback fallback, std::string_view gpu,
                                            std::string_view missing) {
  switch (fallback) {
    case Fallback::kUnsupportedGpu:
      return "Top Spin 4 can't run on " + std::string(gpu.empty() ? "this graphics card" : gpu) +
             " yet: it lacks a Vulkan feature the game needs (" + std::string(missing) +
             "). Updating the graphics driver may add it.";
    case Fallback::kVulkanSetup:
      return "The game could not set up Vulkan on this graphics card. Update the graphics driver "
             "and try again.";
    default:
      return "The game's shaders are missing or damaged. Close the Launcher if it's open, then open "
             "it: it prepares them again when it starts.";
  }
}

}  // namespace topspin4::native
