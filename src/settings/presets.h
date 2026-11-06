// Graphics Presets (ticket 05, spec "Presets"): named sets of values for the settings the
// Graphics section covers. Original is every covered setting at the 360 game's own behaviour;
// Default is what a fresh install starts on (the registry's defaults, ADR-0011); Enhanced is the
// best look the port offers. A Preset is derived, never saved: the row shows the Preset whose values all match, else Custom.
// Pure; the Settings page model (page_model.h) and the session report both read it. Adapted from
// WWE All Stars' Graphics preset (ADR-0004).

#pragma once

#include <functional>
#include <map>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

#include "settings/registry.h"

namespace topspin4::settings {

using ValueMap = std::map<std::string, std::string, std::less<>>;

// The Preset row's key on the page. It names no setting and is never saved.
inline constexpr std::string_view kPresetKey = "graphics_preset";
inline constexpr std::string_view kCustomPreset = "custom";

struct Preset {
  std::string value;  // original, enhanced
  std::string label;
  std::vector<std::pair<std::string, std::string>> values;  // each covered setting's value
};

// The 360 game's own behaviour for each setting whose default is not (ADR-0011). The Original
// Preset takes its covered settings from here, and a Safe start runs on all of them. A setting
// not listed already defaults to the original. The renderer isn't listed: a Safe start draws with
// Native too, since Classic crashes on Intel's Vulkan drivers (ADR-0005).
inline constexpr std::pair<std::string_view, std::string_view> kOriginalValues[] = {
    {"ts4_resolution_scale", "1"},
    {"ts4_anisotropic_filtering", "original"},
    {"ts4_fix_glitches", "false"},
};

// A setting's original value: from kOriginalValues, else its default.
inline std::string_view OriginalValue(const Setting& setting) {
  for (const auto& [key, value] : kOriginalValues) {
    if (key == setting.key) return value;
  }
  return setting.default_value;
}

// The covered settings and each Preset's values. Default comes from the registry's defaults, so
// it cannot drift from them.
inline const std::vector<Preset>& GraphicsPresets() {
  static const std::vector<Preset> presets = [] {
    // Each covered setting with its Enhanced value.
    constexpr std::pair<std::string_view, std::string_view> kCoveredEnhanced[] = {
        {"ts4_resolution_scale", "auto"},
        {"ts4_msaa", "4"},
        {"ts4_anisotropic_filtering", "16"},
        {"ts4_sharpening", "off"}};
    Preset original{"original", "Original", {}};
    Preset shipped{"default", "Default", {}};
    Preset enhanced{"enhanced", "Enhanced", {}};
    for (const auto& [key, enhanced_value] : kCoveredEnhanced) {
      const Setting* setting = FindSetting(key);
      original.values.emplace_back(std::string(key),
                                   setting ? std::string(OriginalValue(*setting)) : std::string());
      shipped.values.emplace_back(std::string(key),
                                  setting ? std::string(setting->default_value) : std::string());
      enhanced.values.emplace_back(std::string(key), std::string(enhanced_value));
    }
    return std::vector<Preset>{original, shipped, enhanced};
  }();
  return presets;
}

// The Preset the values match, or custom. A Preset whose every covered value matches wins. Else
// a setting that does nothing on this renderer (Native only on Classic) is left out of the match,
// so it cannot make the row read Custom. A key the values lack counts as its default.
inline std::string ActivePreset(const ValueMap& values, std::string_view renderer) {
  for (const bool all_settings : {true, false}) {
    for (const Preset& preset : GraphicsPresets()) {
      bool all = true;
      for (const auto& [key, value] : preset.values) {
        const Setting* setting = FindSetting(key);
        if (!setting || (!all_settings && !setting->AvailableOn(renderer))) continue;
        const auto found = values.find(key);
        all &= (found != values.end() ? found->second : std::string(setting->default_value)) ==
               value;
      }
      if (all) return preset.value;
    }
  }
  return std::string(kCustomPreset);
}

// The Preset row's label, and its help: each Preset with the values it sets. Here so the page and
// the settings export say the same.
inline constexpr std::string_view kPresetLabel = "Graphics preset";

inline std::string PresetHelp() {
  std::string help;
  for (const Preset& preset : GraphicsPresets()) {
    help += (help.empty() ? "" : " ") + preset.label + ":";
    for (size_t m = 0; m < preset.values.size(); ++m) {
      const Setting* member = FindSetting(preset.values[m].first);
      if (!member) continue;
      help += std::string(m ? ", " : " ") + std::string(member->label) + " " +
              ChoiceLabel(*member, preset.values[m].second);
    }
    help += ".";
  }
  return help;
}

// The session report's settings object, written after "renderer" (tickets 05 and 09): whether
// this was a Safe start and the Preset in effect at the end (original, enhanced or custom).
inline std::string SessionSettingsJson(bool safe_start, std::string_view preset) {
  return std::string("\"settings\": {\"safe_start\": ") + (safe_start ? "true" : "false") +
         ", \"preset\": \"" + std::string(preset) + "\"}";
}

}  // namespace topspin4::settings
