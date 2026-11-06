// How a session starts with the player's settings (tickets 04, 09, 10): the Settings file is
// read (or ignored in a Safe start), a setting given as a Launch option is pinned, and the
// Settings page is told. Game only (needs the cvar registry and the page).

#pragma once

#include <string>
#include <string_view>

#include <rex/cvar.h>
#include <rex/logging.h>

#include "settings/config_file.h"
#include "settings/presets.h"
#include "settings/registry.h"
#include "settings/settings_file.h"
#include "settings/settings_page.h"

namespace topspin4::settings {

// Whether the setting's cvar was given on the command line or as REX_<NAME>: a Launch option,
// which wins over the Settings file and is never saved.
inline bool IsLaunchOption(std::string_view key) {
  const rex::cvar::FlagEntry* flag = rex::cvar::GetFlagInfo(key);
  return flag && (flag->source == rex::cvar::Source::kCommandLine ||
                  flag->source == rex::cvar::Source::kEnvironment);
}

// Reads the Settings file into the cvars, and hands the Settings page what it needs: the file's
// values and the pinned settings. Safe start (--safe): the file is neither read, migrated, set
// aside nor written, every setting runs at the 360 game's own behaviour (the Original
// values, presets.h) unless given as a Launch option, and the page's changes last until the game
// closes. Call once, from OnPostInitLogging.
inline LoadReport StartSession(const std::filesystem::path& path, bool safe_start) {
  SessionStart session;
  session.safe_start = safe_start;
  LoadReport report;
  if (safe_start) {
    REXLOG_WARN("Safe start: every setting is at its original value, and the Settings file {} "
                "is neither applied nor written",
                Utf8(path));
    for (const auto& [key, value] : kOriginalValues) {
      if (!IsLaunchOption(key)) rex::cvar::SetFlagByName(key, value);
    }
  } else {
    report = LoadSettingsFile(path, [&session](std::string_view key, std::string_view value) {
      session.file_values[std::string(key)] = std::string(value);
      if (IsLaunchOption(key)) {
        session.pinned.insert(std::string(key));
        return true;
      }
      return rex::cvar::SetFlagByName(key, value);
    });
  }
  // A setting given as a Launch option but absent from the file is pinned too.
  for (const Setting& setting : PlayerSettings()) {
    if (IsLaunchOption(setting.key)) session.pinned.insert(std::string(setting.key));
  }
  BeginSession(std::move(session));
  return report;
}

}  // namespace topspin4::settings
