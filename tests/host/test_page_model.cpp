// src/settings/page_model.h: the Settings page's rows, marks and changes, and its pad
// navigation (tickets 03 and 04). No ImGui, no GPU.

#include <catch_amalgamated.hpp>

#include "settings/page_model.h"

using namespace topspin4::settings;

namespace {

// The cvar lifecycles the game declares: the image Enhancements are live, the renderer is not.
std::optional<Applies> Lifecycles(std::string_view key) {
  return key == "ts4_renderer" || key.starts_with("ts4_pad_") ? Applies::kNextStart : Applies::kNow;
}

ValueMap Defaults() {
  ValueMap values;
  for (const Setting& setting : PlayerSettings()) {
    values[std::string(setting.key)] = std::string(setting.default_value);
  }
  return values;
}

ModelContext Context(std::string_view renderer = kRendererNative) {
  ModelContext context;
  context.renderer = std::string(renderer);
  context.applies = Lifecycles;
  return context;
}

}  // namespace

TEST_CASE("The page lists the Graphics settings in registry order with labels, values and help") {
  SettingsModel model(Context(), {}, Defaults());
  const std::vector<Row> rows = model.Rows(Section::kGraphics);
  REQUIRE(rows.size() == 7);  // the Preset row, then the six settings; the renderer has no row
  CHECK(rows[0].kind == RowKind::kPreset);
  CHECK(rows[1].label == "Resolution scale");
  CHECK(rows[1].value_label == "Auto");
  CHECK(rows[1].choices.size() == 4);  // 1x, Auto, 2x, 3x
  CHECK(rows[2].value_label == "Original");
  CHECK(rows[4].label == "Sharpening");
  CHECK(rows[4].value_label == "Off");
  CHECK(rows[5].label == "Fix original glitches");
  CHECK(rows[6].label == "60 fps cutscenes");
  CHECK(rows[6].value_label == "Off");
  for (const Row& row : rows) {
    CHECK_FALSE(row.help.empty());
    CHECK(row.available);
    CHECK_FALSE(row.pending);
  }
}

TEST_CASE("A live change sets the cvar and is saved; its row moves to the new value") {
  SettingsModel model(Context(), {}, Defaults());
  const auto changes = model.Change("ts4_anisotropic_filtering", "8");
  REQUIRE(changes.size() == 1);
  CHECK(changes[0] == SettingChange{"ts4_anisotropic_filtering", "8", true, true});
  const Row row = model.RowFor("ts4_anisotropic_filtering");
  CHECK(row.value == "8");
  CHECK(row.value_label == "8x");
  CHECK(row.mark == Mark::kNow);
  CHECK(row.note.empty());
  CHECK(model.Change("ts4_anisotropic_filtering", "8").empty());    // unchanged
  CHECK(model.Change("ts4_anisotropic_filtering", "99").empty());   // not allowed
}

TEST_CASE("A next-start change is saved, pending and not applied") {
  SettingsModel model(Context(), {}, Defaults());
  Row row = model.RowFor("ts4_renderer");
  CHECK(row.mark == Mark::kNextStart);
  CHECK(row.note == "Applies the next time the game starts.");
  CHECK_FALSE(row.pending);
  const auto changes = model.Change("ts4_renderer", "classic");
  REQUIRE(changes.size() == 1);
  CHECK(changes[0] == SettingChange{"ts4_renderer", "classic", false, true});
  row = model.RowFor("ts4_renderer");
  CHECK(row.value == "classic");
  CHECK(row.pending);
  // Changing it back to what is in effect leaves nothing pending (and the file saved again).
  CHECK(model.Change("ts4_renderer", "native")[0].save);
  CHECK_FALSE(model.RowFor("ts4_renderer").pending);
}

TEST_CASE("A next-start value saved in the file is shown as pending at the next page open") {
  SettingsModel model(Context(), {{"ts4_renderer", "classic"}}, Defaults());
  const Row row = model.RowFor("ts4_renderer");
  CHECK(row.value == "classic");
  CHECK(row.pending);
  // A live setting shows what is in effect, not the file.
  ValueMap in_effect = Defaults();
  in_effect["ts4_sharpening"] = "high";
  SettingsModel live(Context(), {{"ts4_sharpening", "low"}}, in_effect);
  CHECK(live.RowFor("ts4_sharpening").value == "high");
}

TEST_CASE("A setting given as a Launch option is marked, and a change to it is never saved") {
  ModelContext context = Context();
  context.pinned = {"ts4_anisotropic_filtering", "ts4_renderer"};
  ValueMap in_effect = Defaults();
  in_effect["ts4_anisotropic_filtering"] = "16";
  SettingsModel model(context, {{"ts4_anisotropic_filtering", "4"}}, in_effect);

  Row row = model.RowFor("ts4_anisotropic_filtering");
  CHECK(row.mark == Mark::kLaunchOption);
  CHECK(row.value == "16");
  CHECK(row.note == "Set by launch options: a change here lasts until the game closes.");
  const auto changes = model.Change("ts4_anisotropic_filtering", "8");
  REQUIRE(changes.size() == 1);
  CHECK(changes[0] == SettingChange{"ts4_anisotropic_filtering", "8", true, false});

  // A pinned next-start setting can't apply this session or at the next start.
  const auto renderer = model.Change("ts4_renderer", "classic");
  REQUIRE(renderer.size() == 1);
  CHECK_FALSE(renderer[0].set_cvar);
  CHECK_FALSE(renderer[0].save);
  CHECK(model.RowFor("ts4_renderer").note ==
        "Set by launch options: a change here does not apply at the next start.");
}

TEST_CASE("Native-only rows are unavailable on Classic, with the reason, and cannot be stepped") {
  SettingsModel model(Context(kRendererClassic), {}, Defaults());
  const Row scale = model.RowFor("ts4_resolution_scale");
  CHECK_FALSE(scale.available);
  CHECK_FALSE(scale.editable);
  CHECK(scale.note.find("Native renderer") != std::string::npos);
  CHECK(model.Step("ts4_resolution_scale", 1).empty());
  CHECK(model.RowFor("ts4_anisotropic_filtering").available);
  CHECK(model.Step("ts4_anisotropic_filtering", 1).size() == 1);
}

TEST_CASE("Safe start rows say a change is not kept; a live one still applies") {
  ModelContext context = Context();
  context.safe_start = true;
  // Safe start runs on the original values (presets.h), whatever the file says.
  ValueMap in_effect = Defaults();
  in_effect["ts4_renderer"] = "classic";
  in_effect["ts4_msaa"] = "original";
  SettingsModel model(context, {{"ts4_renderer", "native"}}, in_effect);
  CHECK(model.RowFor("ts4_renderer").value == "classic");  // the file is ignored
  CHECK(model.RowFor("ts4_msaa").mark == Mark::kSafeStart);
  const auto live = model.Change("ts4_msaa", "4");
  REQUIRE(live.size() == 1);
  CHECK(live[0].set_cvar);
  CHECK_FALSE(live[0].save);
  // A next-start setting could neither apply nor be saved: the row is fixed and says why.
  const Row renderer = model.RowFor("ts4_renderer");
  CHECK_FALSE(renderer.editable);
  CHECK(renderer.note.find("cannot be changed here") != std::string::npos);
  CHECK(model.Change("ts4_renderer", "native").empty());
  CHECK(model.Step("ts4_renderer", 1).empty());
  CHECK(model.RowFor("ts4_renderer").value == "classic");
}

TEST_CASE("Stepping wraps around the choices both ways") {
  SettingsModel model(Context(), {}, Defaults());
  CHECK(model.Step("ts4_sharpening", -1)[0].value == "high");
  CHECK(model.Step("ts4_sharpening", 1)[0].value == "off");
  // A value the registry doesn't list is shown as it is.
  ValueMap in_effect = Defaults();
  in_effect["ts4_msaa"] = "2";
  SettingsModel odd(Context(), {}, in_effect);
  CHECK(odd.RowFor("ts4_msaa").value_label == "2x");
}

TEST_CASE("The page holds the game's input until it is closed and the pad released") {
  PageModel page{SettingsModel(Context(), {}, Defaults())};
  CHECK_FALSE(page.holds_input());
  page.Open(1000);
  CHECK(page.holds_input());
  // The pad that opened it must go idle first; presses before that do nothing.
  CHECK(page.Update(Pad{PageModel::kRight, 0, 0}, 1010).empty());
  CHECK(page.Update(Pad{0, 0, 0}, 1200).empty());

  CHECK(page.Update(Pad{PageModel::kDown, 0, 0}, 1210).empty());
  CHECK(page.selected_index() == 1);
  CHECK(page.Update(Pad{0, 0, 0}, 1220).empty());
  const auto changes = page.Update(Pad{PageModel::kRight, 0, 0}, 1230);
  REQUIRE(changes.size() == 1);
  CHECK(changes[0].key == "ts4_resolution_scale");  // row 0 is the Preset; the renderer has no row

  CHECK(page.Update(Pad{PageModel::kB, 0, 0}, 1300).empty());
  CHECK_FALSE(page.is_open());
  CHECK(page.holds_input());  // B is still down
  page.Update(Pad{0, 0, 0}, 1310);
  CHECK_FALSE(page.holds_input());
}

TEST_CASE("The left stick moves like the D-pad, and a held direction repeats") {
  PageModel page{SettingsModel(Context(), {}, Defaults())};
  page.Open(0);
  page.Update(Pad{}, 200);
  page.Update(Pad{0, 0, -30000}, 210);  // stick down
  CHECK(page.selected_index() == 1);
  page.Update(Pad{0, 0, -30000}, 300);  // before the repeat delay
  CHECK(page.selected_index() == 1);
  page.Update(Pad{0, 0, -30000}, 700);
  CHECK(page.selected_index() == 2);
}
