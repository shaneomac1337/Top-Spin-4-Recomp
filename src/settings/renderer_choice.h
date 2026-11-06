// Which renderer is asked for (ticket 10, ADR-0005): the ts4_renderer setting, unless Native is
// not available in this build, when Classic draws and the log says why. A Native start that
// fails later ends the session (native/native_unavailable.h).

#pragma once

#include <string>
#include <string_view>

#include "settings/registry.h"

namespace topspin4::settings {

enum class Renderer { kClassic, kNative };

struct RendererChoice {
  Renderer renderer;
  std::string note;  // why the setting was not followed; empty when it was
};

inline RendererChoice ChooseRenderer(std::string_view setting, bool native_available) {
  if (setting != kRendererNative) {
    return {Renderer::kClassic, {}};
  }
  if (!native_available) {
    return {Renderer::kClassic,
            "ts4_renderer is native, but this build has no Native renderer yet; drawing with "
            "Classic"};
  }
  return {Renderer::kNative, {}};
}

}  // namespace topspin4::settings
