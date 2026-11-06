// src/settings: the player settings file in the userdata folder (ticket 10), adapted from
// WWE All Stars' settings (ADR-0004).

#include <catch_amalgamated.hpp>

#include <filesystem>
#include <fstream>
#include <random>
#include <map>
#include <optional>
#include <string>
#include <vector>

#include "settings/config_file.h"
#include "settings/presets.h"
#include "settings/registry.h"
#include "settings/renderer_choice.h"
#include "settings/schema.h"
#include "settings/settings_file.h"

namespace fs = std::filesystem;
using namespace topspin4::settings;

namespace {

// A fresh, empty folder under the temp folder, removed at the end of the test.
struct TempFolder {
  TempFolder() {
    std::random_device random;
    path = fs::temp_directory_path() / ("ts4-settings-" + std::to_string(random()));
    fs::create_directories(path);
  }
  ~TempFolder() {
    std::error_code error;
    fs::remove_all(path, error);
  }
  fs::path path;
};

void WriteFile(const fs::path& path, const std::string& text) {
  std::ofstream(path, std::ios::binary) << text;
}

// Loads the file, collecting what it would set.
struct Loaded {
  explicit Loaded(const fs::path& path)
      : report(LoadSettingsFile(path, [this](std::string_view key, std::string_view value) {
          values[std::string(key)] = std::string(value);
          return true;
        })) {}
  std::map<std::string, std::string> values;
  LoadReport report;
};

}  // namespace

TEST_CASE("A gamertag is 1 to 15 ASCII letters, digits and spaces, starting with a letter") {
  const Setting* gamertag = FindSetting(kGamertagKey);
  REQUIRE(gamertag != nullptr);
  CHECK(gamertag->Allows(gamertag->default_value));
  for (std::string_view good : {"User", "Ace Server 9", "A", "Abcdefghijklmno"}) {
    CHECK(gamertag->Allows(good));
  }
  for (std::string_view bad : {"", "9Lives", " Lead", "Trailing ", "Abcdefghijklmnop", "Tab\tName",
                               "Quote\"", "\xC5\xBD" "ofie"}) {
    CHECK_FALSE(gamertag->Allows(bad));
  }
}

TEST_CASE("Saving a setting replaces its line and keeps every other line") {
  CHECK(SetTomlKey("# mine\nts4_renderer = \"classic\"\nother = 1\n", "ts4_renderer",
                   "\"native\"") == "# mine\nts4_renderer = \"native\"\nother = 1\n");
}

TEST_CASE("Saving a setting the file does not have appends it") {
  CHECK(SetTomlKey("other = 1", "ts4_renderer", "\"native\"") ==
        "other = 1\nts4_renderer = \"native\"\n");
  CHECK(SetTomlKey("", "ts4_renderer", "\"native\"") == "ts4_renderer = \"native\"\n");
}

TEST_CASE("A key that only starts with the setting's name is left alone") {
  CHECK(SetTomlKey("ts4_renderer_old = 1\r\n", "ts4_renderer", "\"native\"") ==
        "ts4_renderer_old = 1\r\nts4_renderer = \"native\"\n");
}

TEST_CASE("A saved setting is written whole, through a temporary file") {
  TempFolder folder;
  const fs::path path = SettingsPath(folder.path);
  CHECK(path == folder.path / "topspin4.settings.toml");
  REQUIRE(SaveSetting(path, "ts4_renderer", "\"native\""));
  REQUIRE(SaveSetting(path, "mnk_mode", "true"));
  // A new file is written at this build's version.
  CHECK(ReadText(path) == "version = " + std::to_string(CurrentVersion()) +
                              "\nts4_renderer = \"native\"\nmnk_mode = true\n");
  CHECK_FALSE(fs::exists(path.string() + ".tmp"));
}

TEST_CASE("A fresh install starts on Native with the enhancements, every default allowed") {
  // ADR-0011: Native and the Enhanced look by default; keyboard and mouse were no 360 input.
  REQUIRE(FindSetting("ts4_renderer"));
  CHECK(FindSetting("ts4_renderer")->default_value == "native");
  REQUIRE(FindSetting("ts4_fix_glitches"));
  CHECK(FindSetting("ts4_fix_glitches")->default_value == "true");
  REQUIRE(FindSetting("mnk_mode"));
  CHECK(FindSetting("mnk_mode")->default_value == "false");
  for (const Setting& setting : PlayerSettings()) {
    INFO(setting.key);
    CHECK(setting.Allows(setting.default_value));
  }
}

TEST_CASE("Without a settings file nothing is set and the defaults stay") {
  TempFolder folder;
  Loaded loaded(SettingsPath(folder.path));
  CHECK(loaded.report.file == LoadReport::File::kMissing);
  CHECK(loaded.values.empty());
}

TEST_CASE("A saved setting is set again at the next start") {
  TempFolder folder;
  const fs::path path = SettingsPath(folder.path);
  REQUIRE(SaveSetting(path, "ts4_renderer", TomlLiteral(*FindSetting("ts4_renderer"), "native")));
  REQUIRE(SaveSetting(path, "mnk_mode",
                      TomlLiteral(*FindSetting("mnk_mode"), "true")));
  Loaded loaded(path);
  CHECK(loaded.report.file == LoadReport::File::kLoaded);
  CHECK(loaded.values == std::map<std::string, std::string>{
                             {"ts4_renderer", "native"}, {"mnk_mode", "true"}});
  CHECK(loaded.report.warnings.empty());
}

TEST_CASE("A value a setting does not allow is skipped with a warning; unknown keys are ignored") {
  TempFolder folder;
  const fs::path path = SettingsPath(folder.path);
  WriteFile(path,
            "ts4_renderer = \"vulkan\"\n"
            "mnk_mode = 1\n"
            "some_removed_setting = true\n");
  Loaded loaded(path);
  CHECK(loaded.report.file == LoadReport::File::kLoaded);
  CHECK(loaded.values.empty());
  CHECK(loaded.report.warnings.size() == 2);
}

TEST_CASE("A corrupt settings file is set aside and the defaults are used") {
  TempFolder folder;
  const fs::path path = SettingsPath(folder.path);
  const std::string corrupt = "ts4_renderer = \"native\"\nthis is not toml\n";
  WriteFile(path, corrupt);
  Loaded loaded(path);
  CHECK(loaded.report.file == LoadReport::File::kCorrupt);
  CHECK(loaded.values.empty());
  CHECK_FALSE(fs::exists(path));
  CHECK(loaded.report.set_aside == fs::path(path.string() + ".corrupt"));
  CHECK(ReadText(loaded.report.set_aside) == corrupt);
  REQUIRE(loaded.report.warnings.size() == 1);
  // The next save starts a clean file.
  REQUIRE(SaveSetting(path, "ts4_renderer", "\"native\""));
  CHECK(Loaded(path).values == std::map<std::string, std::string>{{"ts4_renderer", "native"}});
}

TEST_CASE("Classic draws when Native is asked for but not available") {
  CHECK(ChooseRenderer("classic", false).renderer == Renderer::kClassic);
  CHECK(ChooseRenderer("classic", false).note.empty());
  const RendererChoice fallback = ChooseRenderer("native", false);
  CHECK(fallback.renderer == Renderer::kClassic);
  CHECK_FALSE(fallback.note.empty());
  CHECK(ChooseRenderer("native", true).renderer == Renderer::kNative);
  CHECK(ChooseRenderer("native", true).note.empty());
}

TEST_CASE("Controller and keyboard bindings are player settings that default to the pad as it is") {
  REQUIRE(FindSetting("ts4_pad_a"));
  CHECK(FindSetting("ts4_pad_a")->default_value == "a");
  CHECK(FindSetting("ts4_pad_a")->Allows("rt"));
  CHECK_FALSE(FindSetting("ts4_pad_a")->Allows("z"));
  // Keyboard and mouse stay off by default; the SDK's own key bindings apply when on.
  REQUIRE(FindSetting("mnk_mode"));
  CHECK(FindSetting("mnk_mode")->default_value == "false");
  REQUIRE(FindSetting("keybind_a"));
  CHECK(FindSetting("keybind_a")->default_value == "Semicolon,Space");
  CHECK(FindSetting("keybind_a")->Allows("Shift+J"));
  CHECK_FALSE(FindSetting("keybind_a")->Allows("bad\"quote"));
}

TEST_CASE("A saved key binding is set again at the next start") {
  TempFolder folder;
  const fs::path path = SettingsPath(folder.path);
  REQUIRE(SaveSetting(path, "keybind_a", TomlLiteral(*FindSetting("keybind_a"), "J,Space")));
  REQUIRE(SaveSetting(path, "ts4_pad_a", TomlLiteral(*FindSetting("ts4_pad_a"), "b")));
  CHECK(Loaded(path).values ==
        std::map<std::string, std::string>{{"keybind_a", "J,Space"}, {"ts4_pad_a", "b"}});
}

TEST_CASE("Key bindings may name any key, and may be cleared") {
  CHECK(FindSetting("keybind_dpad_right")->Allows("Shift+Right"));
  CHECK(FindSetting("keybind_a")->Allows(""));
  CHECK_FALSE(FindSetting("keybind_a")->Allows("a\\b"));
}

TEST_CASE("The image settings default to the shipped look and list their levels") {
  const Setting* scale = FindSetting("ts4_resolution_scale");
  REQUIRE(scale);
  CHECK(scale->default_value == "auto");
  CHECK(scale->choices == std::vector<std::string_view>{"1", "auto", "2", "3"});
  const Setting* msaa = FindSetting("ts4_msaa");
  REQUIRE(msaa);
  CHECK(msaa->default_value == "original");  // the shipped pipeline list's keys (ADR-0010)
  CHECK(msaa->choices == std::vector<std::string_view>{"original", "4", "8"});
  const Setting* filtering = FindSetting("ts4_anisotropic_filtering");
  REQUIRE(filtering);
  CHECK(filtering->default_value == "16");
  CHECK(filtering->choices == std::vector<std::string_view>{"original", "4", "8", "16"});
  const Setting* sharpening = FindSetting("ts4_sharpening");
  REQUIRE(sharpening);
  CHECK(sharpening->default_value == "off");
  CHECK(sharpening->choices == std::vector<std::string_view>{"off", "low", "medium", "high"});
}

TEST_CASE("Image settings round-trip through the settings file, and a typo keeps the default") {
  TempFolder folder;
  const fs::path path = SettingsPath(folder.path);
  REQUIRE(SaveSetting(path, "ts4_anisotropic_filtering",
                      TomlLiteral(*FindSetting("ts4_anisotropic_filtering"), "16")));
  REQUIRE(SaveSetting(path, "ts4_sharpening", TomlLiteral(*FindSetting("ts4_sharpening"), "high")));
  CHECK(Loaded(path).values == std::map<std::string, std::string>{
                                   {"ts4_anisotropic_filtering", "16"}, {"ts4_sharpening", "high"}});
  REQUIRE(SaveSetting(path, "ts4_resolution_scale",
                      TomlLiteral(*FindSetting("ts4_resolution_scale"), "auto")));
  CHECK(Loaded(path).values.at("ts4_resolution_scale") == "auto");
  REQUIRE(SaveSetting(path, "ts4_msaa", TomlLiteral(*FindSetting("ts4_msaa"), "8")));
  CHECK(Loaded(path).values.at("ts4_msaa") == "8");
  WriteFile(path, "ts4_anisotropic_filtering = \"15\"\nts4_sharpening = \"hgih\"\n");
  Loaded typo(path);
  CHECK(typo.values.empty());
  CHECK(typo.report.warnings.size() == 2);
}

TEST_CASE("A number written without quotes is accepted where the setting's values are numbers") {
  TempFolder folder;
  const fs::path path = SettingsPath(folder.path);
  WriteFile(path, "ts4_anisotropic_filtering = 16\nts4_resolution_scale = 2\nts4_msaa = 4\n");
  CHECK(Loaded(path).values == std::map<std::string, std::string>{{"ts4_anisotropic_filtering", "16"},
                                                                  {"ts4_msaa", "4"},
                                                                  {"ts4_resolution_scale", "2"}});
  // A number the setting does not allow is still a typo, and so is one for a setting of words.
  WriteFile(path, "ts4_anisotropic_filtering = 15\nts4_sharpening = 3\nts4_resolution_scale = 5\n");
  Loaded typo(path);
  CHECK(typo.values.empty());
  CHECK(typo.report.warnings.size() == 3);
}

TEST_CASE("Resolution scale and MSAA are Native only; every other setting is available on both") {
  for (const Setting& setting : PlayerSettings()) {
    CAPTURE(setting.key);
    CHECK(setting.AvailableOn(kRendererNative));
    const bool native_only = setting.key == "ts4_resolution_scale" || setting.key == "ts4_msaa";
    CHECK(setting.AvailableOn(kRendererClassic) == !native_only);
  }
  CHECK(FindSetting("ts4_anisotropic_filtering")->AvailableOn(kRendererClassic));
  CHECK(FindSetting("ts4_sharpening")->AvailableOn(kRendererClassic));
  CHECK_FALSE(FindSetting("ts4_resolution_scale")->AvailableOn(kRendererClassic));
  CHECK_FALSE(FindSetting("ts4_msaa")->AvailableOn(kRendererClassic));
}

// --- The file version and migrations (ticket 01) ---

namespace {

// A migration that exists only for the tests: ts4_renderer used to be called old_renderer.
const std::vector<Migration> kRenameMigration = {
    [](std::string_view text) { return RenameTomlKey(text, "old_renderer", "ts4_renderer"); }};

}  // namespace

TEST_CASE("A file with no version loads as version 1, keeps its values and is versioned") {
  TempFolder folder;
  const fs::path path = SettingsPath(folder.path);
  WriteFile(path, "# mine\nts4_renderer = \"native\"\n");
  Loaded loaded(path);
  CHECK(loaded.report.file_version == 1);
  CHECK(loaded.values == std::map<std::string, std::string>{{"ts4_renderer", "native"}});
  CHECK(loaded.report.rewritten);
  CHECK(ReadText(path) ==
        "version = " + std::to_string(CurrentVersion()) + "\n# mine\nts4_renderer = \"native\"\n");
  // Once versioned it is left alone.
  Loaded again(path);
  CHECK_FALSE(again.report.rewritten);
  CHECK(again.values == loaded.values);
}

TEST_CASE("A migration renames a key and keeps its value") {
  TempFolder folder;
  const fs::path path = SettingsPath(folder.path);
  WriteFile(path, "version = 1\nold_renderer = \"native\"  # kept\nmnk_mode = true\n");
  const LoadReport report = LoadSettingsFile(
      path, [](std::string_view, std::string_view) { return true; }, kRenameMigration);
  CHECK(report.rewritten);
  CHECK(ReadText(path) == "version = 2\nts4_renderer = \"native\"  # kept\nmnk_mode = true\n");
  std::map<std::string, std::string> values;
  LoadSettingsFile(path,
                   [&](std::string_view key, std::string_view value) {
                     values[std::string(key)] = std::string(value);
                     return true;
                   },
                   kRenameMigration);
  CHECK(values == std::map<std::string, std::string>{{"mnk_mode", "true"},
                                                     {"ts4_renderer", "native"}});
  // An unversioned file starts at version 1, so it gets the migration too.
  WriteFile(path, "old_renderer = \"native\"\n");
  LoadSettingsFile(path, [](std::string_view, std::string_view) { return true; }, kRenameMigration);
  CHECK(ReadText(path) == "version = 2\nts4_renderer = \"native\"\n");
}

TEST_CASE("A saved setting migrates an older file first") {
  TempFolder folder;
  const fs::path path = SettingsPath(folder.path);
  WriteFile(path, "version = 1\nold_renderer = \"native\"\n");
  REQUIRE(SaveSetting(path, "mnk_mode", "true", kRenameMigration));
  CHECK(ReadText(path) == "version = 2\nts4_renderer = \"native\"\nmnk_mode = true\n");
}

TEST_CASE("A file of a newer build is read as well as possible and never rewritten") {
  TempFolder folder;
  const fs::path path = SettingsPath(folder.path);
  const std::string newer = "version = 99\nts4_renderer = \"native\"\nfuture_setting = 1\n";
  WriteFile(path, newer);
  Loaded loaded(path);
  CHECK(loaded.report.newer_than_build);
  CHECK(loaded.report.file_version == 99);
  CHECK_FALSE(loaded.report.rewritten);
  CHECK(loaded.values == std::map<std::string, std::string>{{"ts4_renderer", "native"}});
  CHECK(loaded.report.warnings.size() == 1);
  CHECK_FALSE(SaveSetting(path, "mnk_mode", "true"));
  CHECK(ReadText(path) == newer);
}

TEST_CASE("Saving one key rewrites only that line, keeping the version and every other line") {
  TempFolder folder;
  const fs::path path = SettingsPath(folder.path);
  const std::string version = "version = " + std::to_string(CurrentVersion()) + "\r\n";
  WriteFile(path, version + "# mine\r\nts4_renderer = \"classic\"\r\nother = 1\r\n");
  REQUIRE(SaveSetting(path, "ts4_renderer", "\"native\""));
  CHECK(ReadText(path) == version + "# mine\r\nts4_renderer = \"native\"\r\nother = 1\r\n");
  CHECK_FALSE(fs::exists(path.string() + ".tmp"));
}

// --- The build's own migrations: Resolution scale 4x was removed (the maximum is 3x) ---

TEST_CASE("The build is at settings file version 3, two migrations past version 1") {
  CHECK(Migrations().size() == 2);
  CHECK(CurrentVersion() == 3);
}

TEST_CASE("A version 1 file with a Resolution scale of 4 loads as 3 and is rewritten at version 3") {
  TempFolder folder;
  const fs::path path = SettingsPath(folder.path);
  WriteFile(path, "version = 1\n# mine\nts4_resolution_scale = \"4\"  # big\nts4_msaa = \"4\"\n");
  Loaded loaded(path);
  CHECK(loaded.report.file_version == 1);
  CHECK(loaded.report.rewritten);
  CHECK(loaded.report.warnings.empty());
  CHECK(loaded.values == std::map<std::string, std::string>{{"ts4_msaa", "4"},
                                                            {"ts4_resolution_scale", "3"}});
  // Line-preserving: the comment stays, and the MSAA's own "4" is not a scale.
  CHECK(ReadText(path) ==
        "version = 3\n# mine\nts4_resolution_scale = \"3\"  # big\nts4_msaa = \"4\"\n");
  Loaded again(path);
  CHECK_FALSE(again.report.rewritten);
  CHECK(again.values == loaded.values);
}

TEST_CASE("A version 1 file with any other Resolution scale keeps it") {
  for (const char* scale : {"1", "auto", "2", "3"}) {
    CAPTURE(scale);
    TempFolder folder;
    const fs::path path = SettingsPath(folder.path);
    WriteFile(path, std::string("version = 1\nts4_resolution_scale = \"") + scale + "\"\n");
    Loaded loaded(path);
    CHECK(loaded.report.rewritten);
    CHECK(ReadText(path) ==
          std::string("version = 3\nts4_resolution_scale = \"") + scale + "\"\n");
    CHECK(loaded.values == std::map<std::string, std::string>{{"ts4_resolution_scale", scale}});
  }
}

TEST_CASE("The 4x migration also takes an unversioned file and a single-quoted or bare 4") {
  CHECK(MigrateSettingsText("ts4_resolution_scale = '4'\n") ==
        "version = 3\nts4_resolution_scale = '3'\n");
  CHECK(MigrateSettingsText("version = 1\r\nts4_resolution_scale = 4\r\n") ==
        "version = 3\r\nts4_resolution_scale = 3\r\n");
  CHECK(MigrateSettingsText("version = 1\nts4_resolution_scale=\"4\"") ==
        "version = 3\nts4_resolution_scale=\"3\"");
  // 40 is no scale and is not touched; a file with no scale at all gains none.
  CHECK(MigrateSettingsText("version = 1\nts4_resolution_scale = \"40\"\n") ==
        "version = 3\nts4_resolution_scale = \"40\"\n");
  CHECK(MigrateSettingsText("version = 1\nmnk_mode = true\n") == "version = 3\nmnk_mode = true\n");
}

TEST_CASE("A version 3 file is not migrated again: its 4 is a value the setting refuses") {
  TempFolder folder;
  const fs::path path = SettingsPath(folder.path);
  const std::string text = "version = 3\nts4_resolution_scale = \"4\"\n";
  WriteFile(path, text);
  Loaded loaded(path);
  CHECK_FALSE(loaded.report.rewritten);
  CHECK(ReadText(path) == text);
  CHECK(loaded.values.empty());
  CHECK(loaded.report.warnings.size() == 1);
}

TEST_CASE("Saving a setting into a version 1 file migrates its Resolution scale first") {
  TempFolder folder;
  const fs::path path = SettingsPath(folder.path);
  WriteFile(path, "version = 1\nts4_resolution_scale = \"4\"\n");
  REQUIRE(SaveSetting(path, "mnk_mode", "true"));
  CHECK(ReadText(path) == "version = 3\nts4_resolution_scale = \"3\"\nmnk_mode = true\n");
}

TEST_CASE("The file version reads only an integer `version` key") {
  CHECK(FileVersion("version = 3\n") == 3);
  CHECK(FileVersion("a = 1\n  version=4 # note\n") == 4);
  CHECK_FALSE(FileVersion("a = 1\n").has_value());
  CHECK_FALSE(FileVersion("version = \"2\"\n").has_value());
  CHECK_FALSE(FileVersion("version_old = 2\n").has_value());
  // A version that is not an integer is replaced, not added to: the file stays valid TOML.
  CHECK(WithFileVersion("version = \"2\"\na = 1\n", 1) == "version = 1\na = 1\n");
}

// --- The registry's page data and the settings export (ticket 02) ---

TEST_CASE("Every setting has a section, a label and an order, and the page order is stable") {
  for (const Setting& setting : PlayerSettings()) {
    CAPTURE(setting.key);
    CHECK_FALSE(SectionName(setting.section).empty());
    CHECK_FALSE(setting.label.empty());
    CHECK(setting.order > 0);
  }
  std::map<std::pair<Section, int>, std::string_view> places;
  for (const Setting& setting : PlayerSettings()) {
    CAPTURE(setting.key);
    CHECK(places.emplace(std::make_pair(setting.section, setting.order), setting.key).second);
  }
  const std::vector<const Setting*> ordered = SettingsInPageOrder();
  REQUIRE(ordered.size() == PlayerSettings().size());
  CHECK(ordered.front()->key == "ts4_renderer");
  CHECK(ordered[1]->key == "ts4_resolution_scale");
  CHECK(FindSetting("keybind_lstick_up")->label == "Keys: left stick up");
  CHECK(FindSetting("keybind_a")->label == "Keys: A");
  CHECK(FindSetting("ts4_pad_ls")->label == "Pad: LS click");
}

TEST_CASE("When a setting applies is its cvar's lifecycle, and its help text says the same") {
  size_t checked = 0;
  for (const Setting& setting : PlayerSettings()) {
    CAPTURE(setting.key);
    const std::optional<Applies> applies = AppliesFromCvar(setting.key);
    if (!applies) continue;  // an SDK cvar this test does not link
    ++checked;
    const bool help_says_next_start = setting.help.find("next start") != std::string_view::npos;
    CHECK(help_says_next_start == (*applies == Applies::kNextStart));
  }
  CHECK(checked >= 5 + 16);
  CHECK(*AppliesFromCvar("ts4_renderer") == Applies::kNextStart);
  CHECK(*AppliesFromCvar("ts4_pad_a") == Applies::kNextStart);
  CHECK(*AppliesFromCvar("ts4_msaa") == Applies::kNow);
  CHECK(*AppliesFromCvar("ts4_sharpening") == Applies::kNow);
}

namespace {

// Just enough JSON to read the export back: objects, arrays, strings and integers.
struct Json {
  std::string text;
  std::vector<Json> items;           // an array
  std::vector<std::string> names;    // an object's member names...
  std::vector<Json> members;         // ...and their values
  const Json& at(std::string_view key) const {
    for (size_t i = 0; i < names.size(); ++i) {
      if (names[i] == key) return members[i];
    }
    FAIL("no member " << key);
    return *this;
  }
};

Json ParseJson(std::string_view& in) {
  const auto skip = [&] {
    while (!in.empty() && (in[0] == ' ' || in[0] == '\n' || in[0] == ',' || in[0] == ':')) {
      in.remove_prefix(1);
    }
  };
  skip();
  Json value;
  if (in[0] == '"') {
    in.remove_prefix(1);
    while (in[0] != '"') {
      if (in[0] == '\\') {
        in.remove_prefix(1);
        value.text += in[0] == 'n' ? '\n' : in[0] == 't' ? '\t' : in[0] == 'r' ? '\r' : in[0];
      } else {
        value.text += in[0];
      }
      in.remove_prefix(1);
    }
    in.remove_prefix(1);
  } else if (in[0] == '[' || in[0] == '{') {
    const char close = in[0] == '[' ? ']' : '}';
    const bool object = close == '}';
    in.remove_prefix(1);
    for (skip(); in[0] != close; skip()) {
      if (object) {
        value.names.push_back(ParseJson(in).text);
        value.members.push_back(ParseJson(in));
      } else {
        value.items.push_back(ParseJson(in));
      }
    }
    in.remove_prefix(1);
  } else {
    while (!in.empty() && in[0] != ',' && in[0] != '}' && in[0] != ']' && in[0] != '\n') {
      value.text += in[0];
      in.remove_prefix(1);
    }
  }
  return value;
}

}  // namespace

TEST_CASE("The settings export reads back as the same registry") {
  const std::string text = SchemaJson();
  std::string_view in = text;
  const Json root = ParseJson(in);
  CHECK(root.at("format").text == "2");
  CHECK(root.at("settings_file_version").text == std::to_string(CurrentVersion()));
  REQUIRE(root.at("sections").items.size() == std::size(kSections));
  for (size_t i = 0; i < std::size(kSections); ++i) {
    CHECK(root.at("sections").items[i].text == SectionName(kSections[i]));
  }
  const std::vector<Setting>& registry = PlayerSettings();
  const Json& settings = root.at("settings");
  REQUIRE(settings.items.size() == registry.size());
  for (size_t i = 0; i < registry.size(); ++i) {
    const Setting& setting = registry[i];
    const Json& row = settings.items[i];
    CAPTURE(setting.key);
    CHECK(row.at("key").text == setting.key);
    CHECK(row.at("kind").text == KindName(setting.kind));
    CHECK(row.at("default").text == setting.default_value);
    REQUIRE(row.at("choices").items.size() == setting.choices.size());
    for (size_t c = 0; c < setting.choices.size(); ++c) {
      CHECK(row.at("choices").items[c].text == setting.choices[c]);
    }
    const std::optional<Applies> applies = AppliesFromCvar(setting.key);
    CHECK(row.at("applies_when").text == AppliesName(applies.value_or(Applies::kNextStart)));
    CHECK(row.at("renderers").text ==
          (setting.renderers == Setting::Renderers::kBoth ? "both" : "native"));
    CHECK(row.at("section").text == SectionName(setting.section));
    CHECK(row.at("label").text == setting.label);
    CHECK(row.at("order").text == std::to_string(setting.order));
    CHECK(row.at("help").text == setting.help);
    // Each choice's label as the in-game page shows it; the bindings say whose they are.
    REQUIRE(row.at("choice_labels").items.size() == setting.choices.size());
    for (size_t c = 0; c < setting.choices.size(); ++c) {
      CHECK(row.at("choice_labels").items[c].text == ChoiceLabel(setting, setting.choices[c]));
    }
  }
}

TEST_CASE("The export carries the Graphics Presets with each covered setting's value") {
  const std::string text = SchemaJson();
  std::string_view in = text;
  const Json root = ParseJson(in);
  CHECK(root.at("preset_section").text == "Graphics");
  CHECK(root.at("preset_label").text == kPresetLabel);
  const std::vector<Preset>& presets = GraphicsPresets();
  const Json& exported = root.at("presets");
  REQUIRE(exported.items.size() == presets.size());
  for (size_t i = 0; i < presets.size(); ++i) {
    CAPTURE(presets[i].value);
    CHECK(exported.items[i].at("value").text == presets[i].value);
    CHECK(exported.items[i].at("label").text == presets[i].label);
    const Json& values = exported.items[i].at("values");
    REQUIRE(values.names.size() == presets[i].values.size());
    for (size_t v = 0; v < presets[i].values.size(); ++v) {
      CHECK(values.names[v] == presets[i].values[v].first);
      CHECK(values.members[v].text == presets[i].values[v].second);
    }
  }
  // The three the page offers, as literals: Original the 360's look, Default a fresh install's,
  // Enhanced the best look.
  CHECK(exported.items[0].at("value").text == "original");
  CHECK(exported.items[0].at("values").at("ts4_resolution_scale").text == "1");
  CHECK(exported.items[1].at("value").text == "default");
  CHECK(exported.items[1].at("values").at("ts4_msaa").text == "original");
  CHECK(exported.items[1].at("values").at("ts4_anisotropic_filtering").text == "16");
  CHECK(exported.items[2].at("values").at("ts4_msaa").text == "4");
  CHECK(exported.items[2].at("values").at("ts4_resolution_scale").text == "auto");
  // The help is the in-game row's own.
  CHECK(root.at("preset_help").text == PresetHelp());
  CHECK(PresetHelp().find("Enhanced: Resolution scale Auto, MSAA 4x") != std::string::npos);
}

TEST_CASE("The export says which settings are bindings, for which device and for which action") {
  const std::string text = SchemaJson();
  std::string_view in = text;
  const Json root = ParseJson(in);
  size_t pads = 0, keys = 0, others = 0;
  for (const Json& row : root.at("settings").items) {
    const Json& binding = row.at("binding");
    const std::string key = row.at("key").text;
    CAPTURE(key);
    if (key.starts_with("ts4_pad_")) {
      ++pads;
      CHECK(binding.at("device").text == "pad");
    } else if (key.starts_with("keybind_")) {
      ++keys;
      CHECK(binding.at("device").text == "keyboard");
    } else {
      ++others;
      CHECK(binding.names.empty());  // null: not a binding
      continue;
    }
    CHECK_FALSE(binding.at("action").text.empty());
  }
  CHECK(pads == 16);
  CHECK(keys == 24);
  CHECK(others == 10);
  for (const Json& row : root.at("settings").items) {
    if (row.at("key").text == "ts4_pad_ls") CHECK(row.at("binding").at("action").text == "LS click");
    if (row.at("key").text == "keybind_lstick_up") {
      CHECK(row.at("binding").at("action").text == "left stick up");
    }
    if (row.at("key").text == "ts4_pad_ls") CHECK(row.at("choice_labels").items[8].text == "LS click");
  }
}

TEST_CASE("The export takes applies_when from the lookup, and the switch is found on the command line") {
  const std::string text = SchemaJson([](std::string_view key) -> std::optional<Applies> {
    return key == "ts4_msaa" ? std::optional(Applies::kNow) : std::nullopt;
  });
  CHECK(text.find("\"key\": \"ts4_msaa\", \"kind\": \"choice\", \"default\": \"original\", "
                  "\"choices\": [\"original\", \"4\", \"8\"], "
                  "\"choice_labels\": [\"Original\", \"4x\", \"8x\"], \"applies_when\": \"now\"") !=
        std::string::npos);
  CHECK(text.find("\"key\": \"ts4_renderer\", \"kind\": \"choice\", \"default\": \"native\", "
                  "\"choices\": [\"classic\", \"native\"], "
                  "\"choice_labels\": [\"Classic\", \"Native\"], \"applies_when\": \"next_start\"") !=
        std::string::npos);
  CHECK(SchemaRequested({"topspin4.exe", "--print-settings-schema"}));
  CHECK_FALSE(SchemaRequested({"topspin4.exe", "--print-settings-schema-now"}));
  CHECK(JsonString("a\"b\\c\n") == "\"a\\\"b\\\\c\\n\"");
}

