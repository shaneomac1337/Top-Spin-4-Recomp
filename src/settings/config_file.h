// Saving one setting into the flat TOML settings file without touching the rest of the file,
// unlike the SDK's SaveConfig, which rewrites every non-default cvar (debug and command-line
// values included). Adapted from WWE All Stars' settings/config_file.h (ADR-0004). The file
// carries a top-level `version` and an ordered list of migrations (below).

#pragma once

#include <algorithm>
#include <charconv>
#include <filesystem>
#include <fstream>
#include <functional>
#include <optional>
#include <sstream>
#include <string>
#include <string_view>
#include <system_error>
#include <vector>

namespace topspin4::settings {

// The player's settings file, in the userdata folder (src/user_paths.h), so that replacing the
// game folder keeps it.
inline std::filesystem::path SettingsPath(const std::filesystem::path& user_data_root) {
  return user_data_root / "topspin4.settings.toml";
}

// A path in UTF-8, for logs: path::string() goes through the ANSI code page and throws on a name
// it cannot map (a user name in the per-user folder can be one).
inline std::string Utf8(const std::filesystem::path& path) {
  const std::u8string text = path.u8string();
  return std::string(text.begin(), text.end());
}

namespace detail {

// The text's lines, each split into its body and its line end ("\n", "\r\n" or nothing at the
// end of the text). fn(body, ending) returns false to stop.
template <typename Fn>
inline void ForEachLine(std::string_view text, Fn&& fn) {
  size_t start = 0;
  while (start < text.size()) {
    size_t end = text.find('\n', start);
    end = end == std::string_view::npos ? text.size() : end + 1;
    std::string_view current = text.substr(start, end - start);
    std::string_view ending;
    if (current.ends_with("\r\n")) {
      ending = "\r\n";
    } else if (current.ends_with('\n')) {
      ending = "\n";
    }
    if (!fn(current.substr(0, current.size() - ending.size()), ending)) return;
    start = end;
  }
}

// Whether the line is `key = ...` (blanks before the key allowed); rest is what follows the
// equals sign.
inline bool IsKeyLine(std::string_view body, std::string_view key, std::string_view* rest = nullptr) {
  const size_t first = body.find_first_not_of(" \t");
  if (first == std::string_view::npos || !body.substr(first).starts_with(key)) return false;
  std::string_view after = body.substr(first + key.size());
  const size_t equals = after.find_first_not_of(" \t");
  if (equals == std::string_view::npos || after[equals] != '=') return false;
  if (rest) *rest = after.substr(equals + 1);
  return true;
}

}  // namespace detail

// The text with key set to the TOML literal value: its line replaced (keeping the line end), or
// appended if the text has none.
inline std::string SetTomlKey(std::string_view text, std::string_view key, std::string_view value) {
  const std::string line = std::string(key) + " = " + std::string(value);
  std::string out;
  bool found = false;
  detail::ForEachLine(text, [&](std::string_view body, std::string_view ending) {
    if (!found && detail::IsKeyLine(body, key)) {
      out += line;
      out += ending;
      found = true;
    } else {
      out += body;
      out += ending;
    }
    return true;
  });
  if (!found) {
    if (!out.empty() && out.back() != '\n') {
      out += '\n';
    }
    out += line;
    out += '\n';
  }
  return out;
}

// The text with the line of key `from` renamed to `to`, keeping its value and the rest of the
// line. If the text already has `to`, the old line is dropped instead, so the newer key wins.
// For migrations (below).
inline std::string RenameTomlKey(std::string_view text, std::string_view from, std::string_view to) {
  bool has_target = false;
  detail::ForEachLine(text, [&](std::string_view body, std::string_view) {
    has_target = detail::IsKeyLine(body, to);
    return !has_target;
  });
  std::string out;
  detail::ForEachLine(text, [&](std::string_view body, std::string_view ending) {
    std::string_view rest;
    if (detail::IsKeyLine(body, from, &rest)) {
      if (has_target) return true;
      out += std::string(to) + " =";
      out += rest;
    } else {
      out += body;
    }
    out += ending;
    return true;
  });
  return out;
}

// The text with key's value `from` (a string, in either quote, or a bare number) changed to `to`
// in the same form, keeping the rest of the line (indent, spacing, comment, line end). Any other
// value of the key is left alone. For migrations (below).
inline std::string ReplaceTomlValue(std::string_view text, std::string_view key,
                                    std::string_view from, std::string_view to) {
  std::string out;
  detail::ForEachLine(text, [&](std::string_view body, std::string_view ending) {
    std::string_view rest;
    if (detail::IsKeyLine(body, key, &rest)) {
      // rest is the end of body, so the value starts where the blanks after the equals sign end.
      const size_t blanks = rest.find_first_not_of(" \t");
      const std::string_view value =
          blanks == std::string_view::npos ? std::string_view() : rest.substr(blanks);
      size_t length = 0;
      std::string_view quote;
      if (value.starts_with('"') || value.starts_with('\'')) {
        const size_t close = value.find(value[0], 1);
        if (close != std::string_view::npos) {
          length = close + 1;
          quote = value.substr(0, 1);
        }
      } else {
        length = std::min(value.find_first_of(" \t#\r"), value.size());
      }
      const std::string_view inner = quote.empty() ? value.substr(0, length)
                                                   : value.substr(1, length - 2);
      if (length != 0 && inner == from) {
        out += body.substr(0, body.size() - value.size());
        out += quote;
        out += to;
        out += quote;
        out += value.substr(length);
        out += ending;
        return true;
      }
    }
    out += body;
    out += ending;
    return true;
  });
  return out;
}

// The settings file's version: the top-level integer `version`. Empty if the file has none (an
// unversioned file counts as version 1) or it is not an integer.
inline std::optional<long long> FileVersion(std::string_view text) {
  std::optional<long long> version;
  detail::ForEachLine(text, [&](std::string_view body, std::string_view) {
    std::string_view rest;
    if (!detail::IsKeyLine(body, "version", &rest)) return true;
    const size_t first = rest.find_first_not_of(" \t");
    if (first == std::string_view::npos) return false;
    rest = rest.substr(first);
    long long value = 0;
    const auto [end, error] = std::from_chars(rest.data(), rest.data() + rest.size(), value);
    if (error != std::errc{}) return false;
    const size_t after = rest.substr(end - rest.data()).find_first_not_of(" \t\r");
    if (after == std::string_view::npos || rest[(end - rest.data()) + after] == '#') version = value;
    return false;
  });
  return version;
}

// Sets the file's version: the `version` line replaced, or added first (a top-level key must
// come before any table).
inline std::string WithFileVersion(std::string_view text, long long version) {
  bool has_line = false;
  detail::ForEachLine(text, [&](std::string_view body, std::string_view) {
    has_line = detail::IsKeyLine(body, "version");
    return !has_line;
  });
  if (has_line) return SetTomlKey(text, "version", std::to_string(version));
  return "version = " + std::to_string(version) + "\n" + std::string(text);
}

// Settings migrations, in order: the one at index i takes a file of version i + 1 to version
// i + 2 (text in, text out, with RenameTomlKey and the like), so a renamed or removed setting
// never misreads an old file. The file's version is the build's after them. The launcher
// mirrors this list (launcher/src/Launcher.Core/SettingsFile.cs, Migrations): change both.
using Migration = std::function<std::string(std::string_view text)>;

inline const std::vector<Migration>& Migrations() {
  static const std::vector<Migration> migrations = {
      // 1 -> 2: the Resolution scale's 4x was removed (the largest is 3x, kMaxResolutionScale in
      // native/image_quality.h), so a saved 4 becomes 3 instead of falling back to the default.
      [](std::string_view text) {
        return ReplaceTomlValue(text, "ts4_resolution_scale", "4", "3");
      },
      // 2 -> 3: players no longer choose Classic (ADR-0005), so a saved one becomes Native instead
      // of leaving them on a renderer the Settings page no longer lists.
      [](std::string_view text) {
        return ReplaceTomlValue(text, "ts4_renderer", "classic", "native");
      },
  };
  return migrations;
}

// The version this build writes: one more than its migrations.
inline long long CurrentVersion(const std::vector<Migration>& migrations = Migrations()) {
  return 1 + static_cast<long long>(migrations.size());
}

// Whether the file was written by a newer build. Such a file is read as well as possible and
// never rewritten.
inline bool IsNewerThanBuild(std::string_view text,
                             const std::vector<Migration>& migrations = Migrations()) {
  return FileVersion(text).value_or(1) > CurrentVersion(migrations);
}

// The text brought to this build's version: its migrations run from the file's version on, and
// the version line is set. A file of a newer build is returned as it is.
inline std::string MigrateSettingsText(std::string_view text,
                                       const std::vector<Migration>& migrations = Migrations()) {
  long long version = FileVersion(text).value_or(1);
  if (version < 1) version = 1;
  if (version > CurrentVersion(migrations)) return std::string(text);
  std::string migrated(text);
  for (size_t i = static_cast<size_t>(version - 1); i < migrations.size(); ++i) {
    migrated = migrations[i](migrated);
  }
  return WithFileVersion(migrated, CurrentVersion(migrations));
}

// Reads text, or empty if the file cannot be read.
inline std::string ReadText(const std::filesystem::path& path) {
  std::ifstream in{path, std::ios::binary};
  if (!in) {
    return {};
  }
  std::ostringstream buffer;
  buffer << in.rdbuf();
  return buffer.str();
}

// Writes the text through a temporary file and a rename, so a crash never leaves the file half
// written.
inline bool WriteTextAtomically(const std::filesystem::path& path, const std::string& text) {
  std::error_code error;
  std::filesystem::create_directories(path.parent_path(), error);
  std::filesystem::path temporary = path;
  temporary += ".tmp";
  {
    std::ofstream out{temporary, std::ios::binary | std::ios::trunc};
    if (!out) {
      return false;
    }
    out << text;
    if (!out) {
      return false;
    }
  }
  std::filesystem::rename(temporary, path, error);
  if (error) {
    std::filesystem::remove(temporary, error);
    return false;
  }
  return true;
}

// Writes key = value (a TOML literal) into the settings file, which carries this build's version
// (a new file is created at it; an older one is migrated first). Only that line changes
// otherwise. A file of a newer build is left alone: false.
inline bool SaveSetting(const std::filesystem::path& path, std::string_view key,
                        std::string_view value,
                        const std::vector<Migration>& migrations = Migrations()) {
  const std::string text = ReadText(path);
  if (IsNewerThanBuild(text, migrations)) {
    return false;
  }
  return WriteTextAtomically(path, SetTomlKey(MigrateSettingsText(text, migrations), key, value));
}

}  // namespace topspin4::settings
