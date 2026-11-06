// Native can't start: what the player is told (src/native/native_unavailable.h).
#include <catch_amalgamated.hpp>

#include <string>

#include "native/native_unavailable.h"
#include "settings/config_file.h"
#include "settings/presets.h"

using namespace topspin4;
using native::Fallback;
using native::NativeUnavailableMessage;

TEST_CASE("An unsupported GPU is named, with the features it lacks") {
  const std::string message =
      NativeUnavailableMessage(Fallback::kUnsupportedGpu, "Intel(R) UHD Graphics", "shaderInt64");
  CHECK(message.find("Intel(R) UHD Graphics") != std::string::npos);
  CHECK(message.find("shaderInt64") != std::string::npos);
  CHECK(NativeUnavailableMessage(Fallback::kUnsupportedGpu, "", "shaderInt64")
            .find("this graphics card") != std::string::npos);
}

TEST_CASE("Other reasons say what to do, and none offers Classic") {
  for (const Fallback fallback : {Fallback::kVulkanSetup, Fallback::kMissingShaderPack,
                                  Fallback::kStaleShaderPack, Fallback::kCorruptShaderPack}) {
    const std::string message = NativeUnavailableMessage(fallback, "", "");
    CHECK_FALSE(message.empty());
    CHECK(message.find("Classic") == std::string::npos);
  }
  CHECK(NativeUnavailableMessage(Fallback::kMissingShaderPack, "", "").find("Launcher") !=
        std::string::npos);
}

TEST_CASE("A saved Classic becomes Native, and only that") {
  CHECK(settings::MigrateSettingsText("version = 2\nts4_renderer = \"classic\"\n") ==
        "version = 3\nts4_renderer = \"native\"\n");
  CHECK(settings::MigrateSettingsText("version = 2\nts4_renderer = \"native\"\n") ==
        "version = 3\nts4_renderer = \"native\"\n");
}

TEST_CASE("Safe start and the Original preset stay on Native, and the page has no renderer row") {
  const settings::Setting* renderer = settings::FindSetting("ts4_renderer");
  REQUIRE(renderer != nullptr);
  CHECK(settings::OriginalValue(*renderer) == settings::kRendererNative);
  CHECK_FALSE(settings::ShownOnSettingsPage(*renderer));
  CHECK(settings::ShownOnSettingsPage(*settings::FindSetting("ts4_msaa")));
}
