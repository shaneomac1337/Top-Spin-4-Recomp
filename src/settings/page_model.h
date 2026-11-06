// The Settings page as a pure model (ticket 03/04, spec "Settings page model"): no ImGui, no
// GPU, no cvars. Adapted from WWE All Stars' settings/page_model.h (ADR-0004, ADR-0007), and
// from wwe-svr2011-pc's GRAPHICS page navigation (KaikoClanworth1, used with the author's
// permission).
//
// Two parts:
//  * SettingsModel: the registry, the values the Settings file holds, the values in effect, the
//    settings pinned by Launch options and whether this is a Safe start in; rows (value,
//    choices, availability, mark) and, for a change, what to carry out (set the cvar now, save
//    to the file).
//  * PageModel: a controller snapshot and the time in; navigation, changes and the game's input
//    hold out.
// The ImGui page only draws these and carries the changes out (settings_page.cpp).

#pragma once

#include <algorithm>
#include <cctype>
#include <cstddef>
#include <cstdint>
#include <functional>
#include <map>
#include <set>
#include <string>
#include <string_view>
#include <vector>

#include "settings/presets.h"
#include "settings/registry.h"
#include "settings/schema.h"

namespace topspin4::settings {

// What a row is: a registry setting, the Graphics Preset row (sets several at once, ticket 05) or
// the Controls reset action (ticket 06).
enum class RowKind { kSetting, kPreset, kAction };

// The Controls reset row's key. It names no setting.
inline constexpr std::string_view kResetControlsKey = "reset_controls";

// When a row's change takes effect, or why it does not follow the Settings file.
enum class Mark {
  kNow,           // a live setting: applies at once and is saved
  kNextStart,     // saved, applies the next time the game starts
  kLaunchOption,  // given on the command line or as REX_*: a change lasts until the game closes
  kSafeStart,     // Safe start session: a change is never saved
};

struct ChoiceView {
  std::string value;  // as the cvar parses it
  std::string label;  // as the page shows it
};

struct Row {
  RowKind kind = RowKind::kSetting;
  const Setting* setting = nullptr;  // null for a Preset or action row, and for an unknown key
  std::string key, label, help;
  bool capture = false;       // a key binding: A starts waiting for a key press
  Section section = Section::kGraphics;
  Mark mark = Mark::kNow;
  bool available = true;      // does anything on this session's renderer
  bool editable = true;       // the page can step it (not text, not unavailable)
  bool pending = false;       // kNextStart: the shown value is not the one in effect
  std::string value;          // shown
  std::string value_label;
  std::vector<ChoiceView> choices;
  size_t choice = 0;          // the index in choices of value
  std::string note;           // when the change applies; the reason if unavailable
};

// One setting's change, for the caller to carry out.
struct SettingChange {
  std::string key, value;
  bool set_cvar = false;  // set the cvar now
  bool save = false;      // write it to the Settings file
  bool operator==(const SettingChange&) const = default;
};

struct ModelContext {
  std::string renderer{kRendererClassic};  // the renderer this session draws with
  // Settings given as Launch options: they win over the file and are never saved.
  std::set<std::string, std::less<>> pinned;
  bool safe_start = false;
  // The sections the page shows, in order. Controls join when their rows can be edited.
  std::vector<Section> sections{Section::kGraphics};
  AppliesLookup applies = AppliesFromCvar;
};

class SettingsModel {
 public:
  // file_values: what the Settings file holds (key to value; ignored in a Safe start).
  // in_effect: each setting's value now (the cvars').
  SettingsModel(ModelContext context, ValueMap file_values, ValueMap in_effect)
      : context_(std::move(context)), in_effect_(std::move(in_effect)) {
    for (const Setting* setting : SettingsInPageOrder()) {
      if (!Shown(setting->section)) continue;
      settings_.push_back(setting);
      std::string value = ValueOf(in_effect_, setting->key, setting->default_value);
      // A next-start setting shows what is saved, which is not yet in effect.
      if (!context_.safe_start && !Pinned(*setting) && AppliesNext(*setting)) {
        const auto saved = file_values.find(setting->key);
        if (saved != file_values.end() && setting->Allows(saved->second)) value = saved->second;
      }
      shown_[std::string(setting->key)] = std::move(value);
    }
  }

  const ModelContext& context() const { return context_; }
  const std::vector<Section>& sections() const { return context_.sections; }

  // The rows of one section, in page order. Graphics opens with the Preset row; Controls hides the
  // key bindings unless keyboard and mouse is on, and ends with the reset row.
  std::vector<Row> Rows(Section section) const {
    std::vector<Row> rows;
    if (section == Section::kGraphics && Shown(section)) rows.push_back(PresetRow());
    for (const Setting* setting : settings_) {
      if (setting->section != section || !ShownOnSettingsPage(*setting)) continue;
      if (IsKeyBinding(*setting) && shown_.at("mnk_mode") != "true") continue;
      rows.push_back(MakeRow(*setting));
    }
    if (section == Section::kControls && Shown(section)) rows.push_back(ResetRow());
    return rows;
  }

  // The row for a key, or none (setting null, kind kSetting) if the page does not show it.
  Row RowFor(std::string_view key) const {
    if (key == kPresetKey && Shown(Section::kGraphics)) return PresetRow();
    if (key == kResetControlsKey && Shown(Section::kControls)) return ResetRow();
    for (const Setting* setting : settings_) {
      if (setting->key == key) return MakeRow(*setting);
    }
    return {};
  }

  // The Graphics Preset in effect on the shown values: original, enhanced or custom.
  std::string ActivePresetValue() const { return ActivePreset(shown_, context_.renderer); }

  // Changes a setting to a value the registry allows; returns what to carry out, empty if the
  // value is refused or already shown. A Preset (its key and a Preset's value) sets all its
  // settings at once. A pad binding another binding already uses swaps with it, as
  // pad::Rebind does (src/pad_bindings.h): nothing is left unbound.
  std::vector<SettingChange> Change(std::string_view key, std::string_view value) {
    if (key == kPresetKey) return ChoosePreset(value);
    const Setting* setting = FindSetting(key);
    if (!setting || shown_.find(key) == shown_.end() || !setting->Allows(value)) return {};
    if (!IsPadBinding(*setting)) return SetOne(*setting, value);
    const std::string old = shown_.at(std::string(key));
    if (old == value) return {};
    std::vector<SettingChange> changes = SetOne(*setting, value);
    for (const Setting* other : settings_) {
      if (other != setting && IsPadBinding(*other) && shown_.at(std::string(other->key)) == value) {
        for (const SettingChange& swapped : SetOne(*other, old)) changes.push_back(swapped);
      }
    }
    return changes;
  }

  // Binds a key binding row to one key (as the SDK names it, with modifiers: "Shift+W"), replacing
  // the keys it had. A key another binding already uses is taken from it; a binding that would be
  // left with no key takes this one's old keys, so nothing is left unbound.
  std::vector<SettingChange> BindKey(std::string_view key, std::string_view binding) {
    const Setting* setting = FindSetting(key);
    if (!setting || !IsKeyBinding(*setting) || binding.empty() || !setting->Allows(binding)) {
      return {};
    }
    const std::string old = shown_.at(std::string(key));
    if (old == binding) return {};
    std::vector<SettingChange> changes = SetOne(*setting, binding);
    for (const Setting* other : settings_) {
      if (other == setting || !IsKeyBinding(*other)) continue;
      std::vector<std::string> keys = SplitKeys(shown_.at(std::string(other->key)));
      const auto used = std::find(keys.begin(), keys.end(), binding);
      if (used == keys.end()) continue;
      keys.erase(used);
      std::string rest;
      for (const std::string& kept : keys) rest += (rest.empty() ? "" : ",") + kept;
      for (const SettingChange& taken : SetOne(*other, rest.empty() ? old : rest)) {
        changes.push_back(taken);
      }
    }
    return changes;
  }

  // Every pad and key binding back to its original value (the Controls reset row). Keyboard and
  // mouse on or off is not a binding and stays.
  std::vector<SettingChange> ResetControls() {
    std::vector<SettingChange> changes;
    for (const Setting* setting : settings_) {
      if (!IsPadBinding(*setting) && !IsKeyBinding(*setting)) continue;
      for (const SettingChange& change : SetOne(*setting, setting->default_value)) {
        changes.push_back(change);
      }
    }
    return changes;
  }

  // The next (direction 1) or previous (-1) choice of an editable row, wrapping. On the Preset row
  // it is the next Preset (never Custom); on the reset row, the reset itself.
  std::vector<SettingChange> Step(std::string_view key, int direction) {
    const Row row = RowFor(key);
    if (row.kind == RowKind::kAction) return ResetControls();
    if (row.kind == RowKind::kPreset) {
      const std::vector<Preset>& presets = GraphicsPresets();
      const size_t count = presets.size();
      const size_t at = row.choice;  // count when Custom
      const size_t next = direction < 0 ? (at == 0 ? count - 1 : (at >= count ? count - 1 : at - 1))
                                        : (at >= count - 1 ? 0 : at + 1);
      return ChoosePreset(presets[next].value);
    }
    if (!row.setting || !row.editable || row.choices.empty() || row.capture) return {};
    const size_t count = row.choices.size();
    const size_t next = (row.choice + (direction < 0 ? count - 1 : 1)) % count;
    return Change(key, row.choices[next].value);
  }

 private:
  static bool IsPadBinding(const Setting& setting) {
    return setting.kind == Setting::Kind::kChoice && setting.key.starts_with("ts4_pad_");
  }
  static bool IsKeyBinding(const Setting& setting) {
    return setting.kind == Setting::Kind::kText && setting.key.starts_with("keybind_");
  }
  static std::vector<std::string> SplitKeys(std::string_view text) {
    std::vector<std::string> keys;
    while (!text.empty()) {
      const size_t comma = text.find(',');
      const std::string_view token = text.substr(0, comma);
      if (!token.empty()) keys.emplace_back(token);
      text = comma == std::string_view::npos ? std::string_view() : text.substr(comma + 1);
    }
    return keys;
  }

  // One setting to a value, with no swapping: what to carry out, or nothing if it is shown.
  std::vector<SettingChange> SetOne(const Setting& setting, std::string_view value) {
    const auto shown = shown_.find(setting.key);
    if (shown == shown_.end() || !setting.Allows(value) || shown->second == value) return {};
    if (Fixed(setting)) return {};
    shown->second = std::string(value);
    SettingChange change{std::string(setting.key), std::string(value)};
    const bool now = !AppliesNext(setting);
    change.set_cvar = now;  // a next-start change is saved but not applied
    // A Launch option's change lasts until the game closes and a Safe start's too; neither is saved.
    change.save = !Pinned(setting) && !context_.safe_start;
    if (change.set_cvar) in_effect_[std::string(setting.key)] = change.value;
    return {change};
  }

  // Sets every covered setting of the Preset whose value differs. Custom is shown, never chosen.
  std::vector<SettingChange> ChoosePreset(std::string_view value) {
    std::vector<SettingChange> changes;
    for (const Preset& preset : GraphicsPresets()) {
      if (preset.value != value) continue;
      for (const auto& [key, member] : preset.values) {
        const Setting* setting = FindSetting(key);
        if (!setting || shown_.find(key) == shown_.end()) continue;
        for (const SettingChange& change : SetOne(*setting, member)) changes.push_back(change);
      }
    }
    return changes;
  }

  Row PresetRow() const {
    Row row;
    row.kind = RowKind::kPreset;
    row.key = std::string(kPresetKey);
    row.label = std::string(kPresetLabel);
    row.section = Section::kGraphics;
    bool next = false, pinned = false;
    std::string unavailable;  // the covered settings this renderer cannot use
    const std::vector<Preset>& presets = GraphicsPresets();
    for (size_t m = 0; m < presets[0].values.size(); ++m) {
      const Setting* member = FindSetting(presets[0].values[m].first);
      if (!member) continue;
      bool varies = false;
      for (const Preset& preset : presets) varies |= preset.values[m].second != presets[0].values[m].second;
      if (!varies) continue;
      next |= AppliesNext(*member);
      pinned |= Pinned(*member);
      if (!member->AvailableOn(context_.renderer)) {
        unavailable += (unavailable.empty() ? "" : " and ") + std::string(member->label);
      }
    }
    const std::string active = ActivePresetValue();
    for (const Preset& preset : presets) row.choices.push_back({preset.value, preset.label});
    row.choices.push_back({std::string(kCustomPreset), "Custom"});
    row.choice = row.choices.size() - 1;
    for (size_t i = 0; i < row.choices.size(); ++i) {
      if (row.choices[i].value == active) row.choice = i;
    }
    row.value = active;
    row.value_label = row.choices[row.choice].label;
    row.help = PresetHelp();
    row.mark = pinned ? Mark::kLaunchOption : context_.safe_start ? Mark::kSafeStart
               : next ? Mark::kNextStart : Mark::kNow;
    row.note = NoteFor(row, next);
    if (!unavailable.empty()) {
      row.note += (row.note.empty() ? "" : " ") + unavailable +
                  (context_.renderer == kRendererClassic
                       ? " need the Native renderer and do not count on Classic."
                       : " do not apply on this renderer.");
    }
    return row;
  }

  Row ResetRow() const {
    Row row;
    row.kind = RowKind::kAction;
    row.key = std::string(kResetControlsKey);
    row.label = "Reset controls";
    row.section = Section::kControls;
    row.help = "Put every controller and keyboard binding back to the original layout.";
    row.value = "reset";
    row.value_label = "Original layout";
    row.mark = Mark::kNow;
    return row;
  }

  bool Shown(Section section) const {
    return std::find(context_.sections.begin(), context_.sections.end(), section) !=
           context_.sections.end();
  }
  bool Pinned(const Setting& setting) const {
    return context_.pinned.find(setting.key) != context_.pinned.end();
  }
  bool AppliesNext(const Setting& setting) const {
    return context_.applies(setting.key).value_or(Applies::kNextStart) == Applies::kNextStart;
  }
  // A next-start setting in a Safe start session (not given as a Launch option): a change could
  // neither apply now nor be saved, so the row is not editable.
  bool Fixed(const Setting& setting) const {
    return context_.safe_start && AppliesNext(setting) && !Pinned(setting);
  }
  static std::string ValueOf(const ValueMap& values, std::string_view key, std::string_view fallback) {
    const auto found = values.find(key);
    return found != values.end() ? found->second : std::string(fallback);
  }

  Row MakeRow(const Setting& setting) const {
    Row row;
    row.setting = &setting;
    row.key = std::string(setting.key);
    row.label = std::string(setting.label);
    row.help = std::string(setting.help);
    row.section = setting.section;
    row.value = shown_.at(row.key);
    const bool next = AppliesNext(setting);
    row.available = setting.AvailableOn(context_.renderer);
    const bool fixed = Fixed(setting);
    row.capture = row.available && !fixed && IsKeyBinding(setting);
    row.editable = row.available && !fixed && (setting.kind != Setting::Kind::kText || row.capture);
    if (Pinned(setting)) {
      row.mark = Mark::kLaunchOption;
    } else if (context_.safe_start) {
      row.mark = Mark::kSafeStart;
    } else {
      row.mark = next ? Mark::kNextStart : Mark::kNow;
    }
    row.pending = row.mark == Mark::kNextStart &&
                  row.value != ValueOf(in_effect_, setting.key, setting.default_value);
    if (setting.kind == Setting::Kind::kBool) {
      row.choices = {{"false", "Off"}, {"true", "On"}};
    } else if (setting.kind == Setting::Kind::kChoice) {
      for (std::string_view choice : setting.choices) {
        row.choices.push_back({std::string(choice), ChoiceLabel(setting, choice)});
      }
    }
    row.choice = row.choices.size();
    for (size_t i = 0; i < row.choices.size(); ++i) {
      if (row.choices[i].value == row.value) row.choice = i;
    }
    if (row.choice == row.choices.size()) {
      // A value no choice names is shown as it is.
      row.choices.insert(row.choices.begin(), {row.value, ChoiceLabel(setting, row.value)});
      row.choice = 0;
    }
    row.value_label = row.choices[row.choice].label;
    row.note = NoteFor(row, next);
    return row;
  }

  std::string NoteFor(const Row& row, bool next) const {
    if (!row.available) {
      return "Only the Native renderer uses this setting; this session draws with Classic.";
    }
    switch (row.mark) {
      case Mark::kLaunchOption:
        return next ? "Set by launch options: a change here does not apply at the next start."
                    : "Set by launch options: a change here lasts until the game closes.";
      case Mark::kSafeStart:
        if (!next) return "Safe start: a change here lasts until the game closes.";
        return row.kind == RowKind::kSetting
                   ? "Safe start: this applies at the next start and Safe start saves nothing, so it "
                     "cannot be changed here."
                   : "Safe start: a change here is not kept.";
      case Mark::kNextStart: return "Applies the next time the game starts.";
      case Mark::kNow: break;
    }
    return {};
  }

  ModelContext context_;
  ValueMap in_effect_;
  ValueMap shown_;
  std::vector<const Setting*> settings_;  // the shown ones, in page order
};

// One controller snapshot, in XInput's layout.
struct Pad {
  uint16_t buttons = 0;
  int16_t thumb_lx = 0;
  int16_t thumb_ly = 0;
};

class PageModel {
 public:
  static constexpr uint16_t kUp = 0x0001, kDown = 0x0002, kLeft = 0x0004, kRight = 0x0008;
  static constexpr uint16_t kStart = 0x0010, kBack = 0x0020, kA = 0x1000, kB = 0x2000;
  static constexpr uint16_t kLB = 0x0100, kRB = 0x0200;
  static constexpr int kStickDeadZone = 16000;
  static constexpr uint64_t kOpenGuardMs = 150;
  static constexpr uint64_t kRepeatDelayMs = 400;
  static constexpr uint64_t kRepeatIntervalMs = 130;

  explicit PageModel(SettingsModel model) : model_(std::move(model)) {}

  // Opens on the first row of the first section. Input stays held from the game until the page
  // closes and every button is released (the pad that opened the page cannot reach the game).
  void Open(uint64_t now_ms) {
    open_ = true;
    holding_ = true;
    waiting_release_ = true;
    opened_ms_ = now_ms;
    section_ = 0;
    row_ = 0;
    previous_ = 0;
  }

  // Closes the page from outside (its key pressed again). Input stays held until every button is
  // released, as when B closes it.
  void Close() {
    open_ = false;
    capture_key_.clear();
  }

  // One poll of the pad. Returns the setting changes it made.
  std::vector<SettingChange> Update(const Pad& pad, uint64_t now_ms) {
    const uint16_t held = Held(pad);
    std::vector<SettingChange> changes;
    if (!holding_) return changes;
    if (waiting_release_) {
      if (held == 0 && now_ms >= opened_ms_ + kOpenGuardMs) waiting_release_ = false;
      previous_ = held;
      return changes;
    }
    if (!open_) {
      if (held == 0) holding_ = false;  // released after closing: the game gets the pad back
      previous_ = held;
      return changes;
    }
    const uint16_t pressed = held & ~previous_;
    if (capturing()) {
      // Waiting for a key: the pad does nothing but cancel (a key press arrives as BindKey).
      previous_ = held;
      if (pressed & (kB | kStart | kBack)) capture_key_.clear();
      return changes;
    }
    if (pressed & (kB | kStart | kBack)) {
      open_ = false;
      previous_ = held;
      return changes;
    }
    const uint16_t directions = held & (kUp | kDown | kLeft | kRight);
    uint16_t act = pressed & (kUp | kDown | kLeft | kRight | kA);
    if (directions != (previous_ & (kUp | kDown | kLeft | kRight))) {
      repeat_at_ms_ = now_ms + kRepeatDelayMs;  // a new direction starts its delay
    } else if (directions && now_ms >= repeat_at_ms_) {
      act |= directions;
      repeat_at_ms_ = now_ms + kRepeatIntervalMs;
    }
    previous_ = held;
    const std::vector<Section>& sections = model_.sections();
    if (sections.empty()) return changes;
    if (pressed & (kLB | kRB)) {
      if (pressed & kLB) section_ = (section_ + sections.size() - 1) % sections.size();
      if (pressed & kRB) section_ = (section_ + 1) % sections.size();
      row_ = 0;
      return changes;
    }
    const std::vector<Row> rows = this->rows();
    if (rows.empty()) return changes;
    if (row_ >= rows.size()) row_ = rows.size() - 1;  // key rows hide when keyboard and mouse goes off
    if (act & kUp) row_ = (row_ + rows.size() - 1) % rows.size();
    if (act & kDown) row_ = (row_ + 1) % rows.size();
    const Row& selected = rows[row_];
    if (selected.kind == RowKind::kAction) {
      if (pressed & kA) changes = model_.Step(selected.key, 1);  // a reset needs a deliberate A
    } else if (selected.capture) {
      if (pressed & kA) capture_key_ = selected.key;
    } else {
      if (act & kLeft) changes = model_.Step(selected.key, -1);
      if (act & (kRight | kA)) changes = model_.Step(selected.key, 1);
    }
    return changes;
  }

  // Whether a key binding row waits for a key press (A on it). The page then reads the keyboard
  // and hands the key to BindKey; the pad can only cancel (B).
  bool capturing() const { return !capture_key_.empty(); }
  const std::string& capture_key() const { return capture_key_; }
  void CancelCapture() { capture_key_.clear(); }

  // The key pressed while capturing, as the SDK names it ("Shift+W"). Ends the capture.
  std::vector<SettingChange> BindKey(std::string_view binding) {
    const std::string key = std::move(capture_key_);
    capture_key_.clear();
    return key.empty() ? std::vector<SettingChange>{} : model_.BindKey(key, binding);
  }

  // A change from outside the pad (a script's `set`), through the same rules as the page's own.
  std::vector<SettingChange> Change(std::string_view key, std::string_view value) {
    return model_.Change(key, value);
  }

  bool is_open() const { return open_; }
  // Whether the game must see an idle controller.
  bool holds_input() const { return holding_; }
  const SettingsModel& model() const { return model_; }
  Section section() const { return model_.sections()[section_]; }
  size_t section_index() const { return section_; }
  std::vector<Row> rows() const { return model_.Rows(section()); }
  size_t selected_index() const { return row_; }

 private:
  // Buttons plus the left stick as D-pad directions.
  static uint16_t Held(const Pad& pad) {
    uint16_t held = pad.buttons;
    if (pad.thumb_ly > kStickDeadZone) held |= kUp;
    if (pad.thumb_ly < -kStickDeadZone) held |= kDown;
    if (pad.thumb_lx < -kStickDeadZone) held |= kLeft;
    if (pad.thumb_lx > kStickDeadZone) held |= kRight;
    return held;
  }

  SettingsModel model_;
  size_t section_ = 0;
  size_t row_ = 0;
  std::string capture_key_;  // the key binding row waiting for a key
  bool open_ = false;
  bool holding_ = false;
  bool waiting_release_ = false;
  uint64_t opened_ms_ = 0;
  uint64_t repeat_at_ms_ = 0;
  uint16_t previous_ = 0;
};

}  // namespace topspin4::settings
