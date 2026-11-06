#include "settings/settings_page.h"

#include <algorithm>
#include <atomic>
#include <cfloat>
#include <chrono>
#include <mutex>
#include <string>
#include <vector>

#include <imgui.h>

#include <rex/cvar.h>
#include <rex/input/input_system.h>
#include <rex/logging.h>
#include <rex/ui/imgui_dialog.h>
#include <rex/ui/keybinds.h>

#include "native/native_renderer.h"
#include "settings/config_file.h"
#include "settings/main_menu_sink.h"
#include "settings/open_combo.h"
#include "settings/page_style.h"
#include "settings/registry.h"

namespace topspin4::settings {

namespace {

class PageDialog;

struct State {
  rex::ui::ImGuiDrawer* drawer = nullptr;
  rex::input::InputSystem* input = nullptr;
  std::filesystem::path settings_path;
  std::atomic<bool> hold = false;  // read on the guest thread
  PageDialog* dialog = nullptr;    // UI thread only
  std::function<void(std::function<void()>)> run_on_ui;
  std::function<void()> request_close;  // UI thread only
  SessionStart session;  // UI thread only; file_values follows what the page saves
  ImFont* heading = nullptr;
  ImFont* body = nullptr;
  pad::Bindings bindings = pad::Bindings::Default();  // what the game's input system was built with
} g;

// The page's colours are Top Spin 4's (settings/page_style.h).
constexpr const style::Palette& kPalette = style::kTopSpinPalette;
constexpr ImU32 Col(style::Rgba color) { return color.Packed(); }

// Maps the game's 1280x720 frame into the window the way Native shows the game: stretched to
// fill it. x follows the width, y and text sizes follow the height (so a 1280x800 window scales
// the text by 800/720 and the rows still fit), and text keeps its shape.
struct Frame {
  float scale_x;
  float scale;
  ImVec2 At(float x, float y) const { return {x * scale_x, y * scale}; }
};

Frame GameFrame() {
  const ImVec2 size = ImGui::GetIO().DisplaySize;
  return {size.x / 1280.0f, size.y / 720.0f};
}

// A box with a vertical gradient and the game's slightly rounded corners: a rounded fill in the
// bottom colour, the gradient over it short of the corners, then the edge.
void GradientBox(ImDrawList* draw, const Frame& f, float x0, float y0, float x1, float y1,
                 style::Rgba top, style::Rgba bottom, style::Rgba edge) {
  constexpr float kRound = 3;
  const ImVec2 a = f.At(x0, y0), b = f.At(x1, y1);
  const float r = kRound * f.scale;
  draw->AddRectFilled(a, b, Col(bottom), r);
  draw->AddRectFilledMultiColor({a.x, a.y + r}, {b.x, b.y - r}, Col(top), Col(top), Col(bottom),
                                Col(bottom));
  draw->AddRectFilled({a.x + r, a.y}, {b.x - r, a.y + r}, Col(top));
  draw->AddRect(a, b, Col(edge), r, 0, 1.0f * f.scale);
}

// The tennis ball the game puts beside a page's title.
void Ball(ImDrawList* draw, const Frame& f, float x, float y, float radius) {
  const ImVec2 c = f.At(x, y);
  const float r = radius * f.scale;
  draw->AddCircleFilled(c, r, Col(kPalette.ball), 32);
  draw->AddCircle(c, r, Col(kPalette.ball_edge), 32, 1.5f * f.scale);
  const ImU32 seam = Col(kPalette.ball_seam);
  draw->PathArcTo({c.x - r * 1.35f, c.y}, r * 0.95f, -0.75f, 0.75f, 16);
  draw->PathStroke(seam, 0, 1.6f * f.scale);
  draw->PathArcTo({c.x + r * 1.35f, c.y}, r * 0.95f, 3.14159f - 0.75f, 3.14159f + 0.75f, 16);
  draw->PathStroke(seam, 0, 1.6f * f.scale);
}

void Text(ImDrawList* draw, ImFont* font, float size, const Frame& f, float x, float y, ImU32 color,
          const std::string& text, float wrap = 0) {
  draw->AddText(font ? font : ImGui::GetFont(), size * f.scale, f.At(x, y), color, text.c_str(),
                nullptr, wrap * f.scale_x);
}

float TextWidth(ImFont* font, float size, const Frame& f, const std::string& text) {
  font = font ? font : ImGui::GetFont();
  return font->CalcTextSizeA(size, FLT_MAX, 0, text.c_str()).x * f.scale / f.scale_x;
}

// Text no wider than max_width (frame units): shrunk to fit, with an optional shadow, which the
// game draws under the white text of its selected row.
void FitText(ImDrawList* draw, ImFont* font, float size, const Frame& f, float x, float y,
             float max_width, ImU32 color, const std::string& text, const style::Rgba* shadow = nullptr) {
  const float fit = style::FitSize(size, TextWidth(font, size, f, text), max_width);
  if (shadow) Text(draw, font, fit, f, x + 1.2f, y + 1.5f, Col(*shadow), text);
  Text(draw, font, fit, f, x, y, color, text);
}

// The width the text takes once fitted to max_width.
float FittedWidth(ImFont* font, float size, const Frame& f, float max_width, const std::string& text) {
  const float width = TextWidth(font, size, f, text);
  return width * style::FitSize(size, width, max_width) / size;
}

// A controller button in the prompts at the bottom: a coloured disc with its letter (A, B), or a
// grey shoulder tab (LB, RB). Returns the x after it.
float ButtonGlyph(ImDrawList* draw, ImFont* font, const Frame& f, float x, float y_center,
                  const std::string& name) {
  const bool shoulder = name.size() == 2;
  const float size = 14;
  const float text_w = TextWidth(font, size, f, name);
  if (shoulder) {
    const float w = text_w + 12, h = 18;
    GradientBox(draw, f, x, y_center - h / 2, x + w, y_center + h / 2, kPalette.button_shoulder,
                kPalette.button_shoulder, kPalette.button_edge);
    Text(draw, font, size, f, x + 6, y_center - size * 0.62f, Col(kPalette.button_letter), name);
    return x + w;
  }
  const float r = 11;
  const style::Rgba disc = name == "A" ? kPalette.button_a : kPalette.button_b;
  const ImVec2 c = f.At(x + r, y_center);
  draw->AddCircleFilled(c, r * f.scale, Col(disc), 24);
  draw->AddCircle(c, r * f.scale, Col(kPalette.button_edge), 24, 1.0f * f.scale);
  Text(draw, font, size, f, x + r - text_w / 2, y_center - size * 0.62f, Col(kPalette.button_letter),
       name);
  return x + 2 * r;
}

void Arrow(ImDrawList* draw, const Frame& f, float x, float y, bool left, ImU32 color) {
  const float w = 7, h = 7;
  const ImVec2 tip = f.At(left ? x : x + w, y);
  const ImVec2 top = f.At(left ? x + w : x, y - h), bottom = f.At(left ? x + w : x, y + h);
  draw->AddTriangleFilled(tip, top, bottom, color);
}

uint64_t NowMs() {
  return uint64_t(std::chrono::duration_cast<std::chrono::milliseconds>(
                      std::chrono::steady_clock::now().time_since_epoch())
                      .count());
}

// The controller (user 0's real state under the hold; a scripted run's pad is one), plus the
// keyboard's arrows, Enter and Esc for players without one.
Pad ReadPad() {
  Pad pad;
  rex::input::X_INPUT_STATE state = {};
  if (g.input && g.input->HeldState(0, &state)) {
    // The held state is what the game would see, after the player's remap; the page works on the
    // physical pad, so a remap cannot move its own navigation (ticket 06).
    topspin4::pad::Pad game;
    game.buttons = state.gamepad.buttons;
    game.left_trigger = state.gamepad.left_trigger;
    game.right_trigger = state.gamepad.right_trigger;
    game.thumb_lx = state.gamepad.thumb_lx;
    game.thumb_ly = state.gamepad.thumb_ly;
    const topspin4::pad::Pad physical = topspin4::pad::Unmap(game, g.bindings);
    pad.buttons = physical.buttons;
    pad.thumb_lx = physical.thumb_lx;
    pad.thumb_ly = physical.thumb_ly;
  }
  if (ImGui::IsKeyDown(ImGuiKey_UpArrow)) pad.buttons |= PageModel::kUp;
  if (ImGui::IsKeyDown(ImGuiKey_DownArrow)) pad.buttons |= PageModel::kDown;
  if (ImGui::IsKeyDown(ImGuiKey_LeftArrow)) pad.buttons |= PageModel::kLeft;
  if (ImGui::IsKeyDown(ImGuiKey_RightArrow)) pad.buttons |= PageModel::kRight;
  if (ImGui::IsKeyDown(ImGuiKey_Enter)) pad.buttons |= PageModel::kA;
  if (ImGui::IsKeyDown(ImGuiKey_Escape)) pad.buttons |= PageModel::kB;
  return pad;
}

// The SDK's name for a key that went down this frame, with the modifiers held ("Shift+W"), or
// empty. Only keys the SDK can name and read back are offered; Escape cancels and F4 is the page's
// own key, so neither can be bound.
std::string PressedKeyBinding() {
  struct Mapped {
    ImGuiKey key;
    rex::ui::VirtualKey vk;
  };
  static const std::vector<Mapped> keys = [] {
    std::vector<Mapped> list;
    auto add = [&list](ImGuiKey key, uint16_t vk) {
      list.push_back({key, static_cast<rex::ui::VirtualKey>(vk)});
    };
    for (int i = 0; i < 26; ++i) add(ImGuiKey(ImGuiKey_A + i), uint16_t('A' + i));
    for (int i = 0; i < 10; ++i) add(ImGuiKey(ImGuiKey_0 + i), uint16_t('0' + i));
    for (int i = 0; i < 12; ++i) {
      if (i != 3) add(ImGuiKey(ImGuiKey_F1 + i), uint16_t(0x70 + i));  // F4 opens the page
    }
    add(ImGuiKey_LeftArrow, 0x25);   add(ImGuiKey_UpArrow, 0x26);
    add(ImGuiKey_RightArrow, 0x27);  add(ImGuiKey_DownArrow, 0x28);
    add(ImGuiKey_Space, 0x20);       add(ImGuiKey_Enter, 0x0D);
    add(ImGuiKey_Tab, 0x09);         add(ImGuiKey_Backspace, 0x08);
    add(ImGuiKey_Insert, 0x2D);      add(ImGuiKey_Delete, 0x2E);
    add(ImGuiKey_Home, 0x24);        add(ImGuiKey_End, 0x23);
    add(ImGuiKey_PageUp, 0x21);      add(ImGuiKey_PageDown, 0x22);
    add(ImGuiKey_Semicolon, 0xBA);   add(ImGuiKey_Apostrophe, 0xDE);
    add(ImGuiKey_Comma, 0xBC);       add(ImGuiKey_Period, 0xBE);
    add(ImGuiKey_Minus, 0xBD);       add(ImGuiKey_Equal, 0xBB);
    add(ImGuiKey_Slash, 0xBF);       add(ImGuiKey_Backslash, 0xDC);
    add(ImGuiKey_LeftBracket, 0xDB); add(ImGuiKey_RightBracket, 0xDD);
    add(ImGuiKey_GraveAccent, 0xC0);
    return list;
  }();
  for (const Mapped& mapped : keys) {
    if (!ImGui::IsKeyPressed(mapped.key, false)) continue;
    std::string name = rex::ui::VirtualKeyToString(mapped.vk);
    if (name.empty() || rex::ui::ParseVirtualKey(name) != mapped.vk) continue;
    const ImGuiIO& io = ImGui::GetIO();
    return std::string(io.KeyCtrl ? "Ctrl+" : "") + (io.KeyAlt ? "Alt+" : "") +
           (io.KeyShift ? "Shift+" : "") + name;
  }
  return {};
}

// Carries out what the model says: a live change sets the cvar, a saved one goes to the
// Settings file (a next-start setting only there, so nothing half-applies).
void Apply(const SettingChange& change) {
  if (change.set_cvar && !rex::cvar::SetFlagByName(change.key, change.value)) {
    REXLOG_WARN("settings page: {} = {} was rejected", change.key, change.value);
  }
  if (change.save) {
    const Setting* setting = FindSetting(change.key);
    if (!setting || !SaveSetting(g.settings_path, change.key, TomlLiteral(*setting, change.value))) {
      REXLOG_WARN("settings page: could not save {} to {}", change.key, Utf8(g.settings_path));
    }
    g.session.file_values[change.key] = change.value;
  }
  REXLOG_INFO("settings page: {} = {} ({})", change.key, change.value,
              change.save ? (change.set_cvar ? "applied and saved" : "saved for the next start")
                          : (change.set_cvar ? "applied, not saved" : "not applied, not saved"));
}

SettingsModel BuildModel() {
  ModelContext context;
  context.renderer = std::string(topspin4::native::IsEnabled() ? kRendererNative : kRendererClassic);
  context.pinned = g.session.pinned;
  context.safe_start = g.session.safe_start;
  context.sections = {Section::kGraphics, Section::kControls};
  ValueMap in_effect;
  for (const Setting& setting : PlayerSettings()) {
    in_effect[std::string(setting.key)] = rex::cvar::GetFlagByName(setting.key);
  }
  return SettingsModel(std::move(context), g.session.file_values, std::move(in_effect));
}

// Registered with the ImGui drawer only while the page is open or waiting for the pad to be
// released: a registered dialog moves presents to the UI thread (docs/research/movement-judder.md).
class PageDialog final : public rex::ui::ImGuiDialog {
 public:
  explicit PageDialog(rex::ui::ImGuiDrawer* drawer) : ImGuiDialog(drawer), model_(BuildModel()) {
    model_.Open(NowMs());
    g.hold = true;
  }

  void CloseFromKey() { model_.Close(); }

  // A change from a script, through the model this page shows, so its rows follow.
  std::vector<SettingChange> Change(std::string_view key, std::string_view value) {
    return model_.Change(key, value);
  }

 protected:
  void OnDraw(ImGuiIO&) override {
    // A key binding row waiting for a key takes the first key pressed after A began the wait.
    const bool was_capturing = model_.capturing();
    for (const SettingChange& change : model_.Update(ReadPad(), NowMs())) Apply(change);
    if (was_capturing && model_.capturing()) {
      const std::string binding = PressedKeyBinding();
      if (!binding.empty()) {
        for (const SettingChange& change : model_.BindKey(binding)) Apply(change);
      }
    }
    g.hold = model_.holds_input();
    if (!model_.holds_input()) {
      Close();
      return;
    }
    if (model_.is_open()) DrawPage();
  }

  void OnClose() override {
    g.hold = false;
    g.dialog = nullptr;
  }

 private:
  // Laid out as the game's settings pages, in its 1280x720 frame: the title with a ball top left,
  // the rows as a grey label cell beside a white value cell (x 330..950, 30 high, 34 apart), the
  // selected row orange and a little wider with arrows round its value, a grey help bar under the
  // rows, and the button prompts at the bottom left. The backdrop covers the whole screen in the
  // menu's blue, so the page looks the same over the menu and over a match.
  void DrawPage() {
    ImDrawList* draw = ImGui::GetForegroundDrawList();
    const Frame f = GameFrame();
    const ImVec2 screen = ImGui::GetIO().DisplaySize;
    const ImU32 back_top = Col(kPalette.backdrop_top), back_mid = Col(kPalette.backdrop_mid),
                back_bottom = Col(kPalette.backdrop_bottom);
    draw->AddRectFilledMultiColor({0, 0}, {screen.x, screen.y * 0.5f}, back_top, back_top,
                                  back_mid, back_mid);
    draw->AddRectFilledMultiColor({0, screen.y * 0.5f}, screen, back_mid, back_mid, back_bottom,
                                  back_bottom);

    const style::Rgba shadow = kPalette.selected_shadow;
    constexpr float kLeft = 330, kSplit = 640, kRight = 950;
    Ball(draw, f, 272, 88, 15);
    FitText(draw, g.heading, 24, f, 302, 73, 600, Col(kPalette.heading), "pc settings", &shadow);

    // Section names centred above the rows, between LB and RB when there are several.
    const std::vector<Section>& sections = model_.model().sections();
    const ImU32 tab_idle = Col(kPalette.tab_idle), tab_active = Col(kPalette.tab_active);
    constexpr float kTabsY = 206, kTabSize = 20, kTabGap = 26;
    if (sections.size() > 1) {
      float total = 0;
      for (const Section s : sections) total += TextWidth(g.heading, kTabSize, f, std::string(SectionName(s)));
      total += kTabGap * float(sections.size() - 1);
      float x = (kLeft + kRight) / 2 - total / 2;
      ButtonGlyph(draw, g.heading, f, x - 52, kTabsY + kTabSize * 0.62f, "LB");
      for (size_t s = 0; s < sections.size(); ++s) {
        const std::string name(SectionName(sections[s]));
        Text(draw, g.heading, kTabSize, f, x, kTabsY, s == model_.section_index() ? tab_active : tab_idle,
             name);
        x += TextWidth(g.heading, kTabSize, f, name) + kTabGap;
      }
      ButtonGlyph(draw, g.heading, f, x - kTabGap + 16, kTabsY + kTabSize * 0.62f, "RB");
    } else {
      const std::string name(SectionName(model_.section()));
      Text(draw, g.heading, kTabSize, f, (kLeft + kRight) / 2 - TextWidth(g.heading, kTabSize, f, name) / 2,
           kTabsY, tab_active, name);
    }

    // Up to eight rows; a longer list (Controls) scrolls to keep the selected row in view.
    const std::vector<Row> rows = model_.rows();
    constexpr size_t kVisible = 8;
    constexpr float kTop = 248, kHeight = 30, kStep = 34, kTextSize = 19;
    const size_t shown = std::min(rows.size(), kVisible);
    const size_t selected_row = std::min(model_.selected_index(), rows.empty() ? 0 : rows.size() - 1);
    const size_t first =
        rows.size() <= kVisible
            ? 0
            : std::min(selected_row > kVisible / 2 ? selected_row - kVisible / 2 : 0,
                       rows.size() - kVisible);
    if (rows.size() > kVisible) {
      const std::string count = std::to_string(selected_row + 1) + " / " + std::to_string(rows.size());
      Text(draw, g.body, 17, f, kRight - TextWidth(g.body, 17, f, count), kTabsY + 2, tab_idle, count);
    }
    // Labels fit in 290 of the 310 of their cell, values in 250 between the arrows.
    constexpr float kLabelWidth = 290, kValueWidth = 250;
    const float text_y = (kHeight - kTextSize * 1.2f) / 2;
    float y = kTop;
    for (size_t i = first; i < first + shown; ++i) {
      const Row& row = rows[i];
      const bool selected = i == selected_row;
      const bool waiting = model_.capturing() && model_.capture_key() == row.key;
      if (selected) {
        GradientBox(draw, f, kLeft - 6, y - 2, kRight + 6, y + kHeight + 2, kPalette.bar_top,
                    kPalette.bar_bottom, kPalette.bar_edge);
        draw->AddLine(f.At(kSplit, y + 2), f.At(kSplit, y + kHeight - 2),
                      Col(kPalette.bar_edge.WithAlpha(110)), 1.0f * f.scale);
      } else {
        GradientBox(draw, f, kLeft, y, kSplit - 1, y + kHeight, kPalette.label_top,
                    kPalette.label_bottom, kPalette.cell_edge);
        GradientBox(draw, f, kSplit + 1, y, kRight, y + kHeight, kPalette.value_top,
                    kPalette.value_bottom, kPalette.cell_edge);
      }
      // White text with a shadow on the orange row, dark text on the grey and white cells.
      const style::Rgba* text_shadow = selected ? &shadow : nullptr;
      ImU32 color = Col(selected ? kPalette.selected_text : kPalette.idle_text);
      if (!row.available) color = Col(selected ? kPalette.selected_text.WithAlpha(190) : kPalette.disabled_text);
      std::string value = row.available ? row.value_label : "Unavailable";
      if (row.pending) value += " *";
      if (waiting) value = "Press a key...";
      ImU32 value_color = Col(selected ? kPalette.selected_text : kPalette.idle_value);
      if (!row.available) value_color = color;
      if (!selected && (row.mark == Mark::kLaunchOption || row.mark == Mark::kSafeStart)) {
        value_color = Col(kPalette.launch_mark);
      }
      const std::string label = style::UpperLabel(row.label);
      // Clipped to the row as a last resort, so no text ever spills over a neighbour.
      draw->PushClipRect(f.At(kLeft - 6, y - 2), f.At(kRight + 6, y + kHeight + 2), true);
      const float label_w = FittedWidth(g.heading, kTextSize, f, kLabelWidth, label);
      FitText(draw, g.heading, kTextSize, f, (kLeft + kSplit) / 2 - label_w / 2, y + text_y, kLabelWidth,
              color, label, text_shadow);
      const float value_w = FittedWidth(g.heading, kTextSize, f, kValueWidth, value);
      FitText(draw, g.heading, kTextSize, f, (kSplit + kRight) / 2 - value_w / 2, y + text_y,
              kValueWidth, value_color, value, text_shadow);
      draw->PopClipRect();
      if (selected && row.editable && !row.capture && row.kind != RowKind::kAction) {
        const ImU32 arrow = Col(kPalette.selected_text);
        Arrow(draw, f, kSplit + 10, y + kHeight / 2, true, arrow);
        Arrow(draw, f, kRight - 17, y + kHeight / 2, false, arrow);
      }
      y += kStep;
    }

    // The help bar follows the rows, as the game's does, and grows with its text; the size drops a
    // point at a time until the help and the note fit above the prompts.
    const Row* current = rows.empty() ? nullptr : &rows[selected_row];
    if (current) {
      std::string help = current->help;
      if (current->capture) help += " Press A, then the key to use (Esc or B cancels).";
      if (current->kind == RowKind::kAction) help += " Press A to reset.";
      ImFont* body = g.body ? g.body : ImGui::GetFont();
      constexpr float kPad = 8, kWrap = 600, kGap = 4;
      auto height = [&](float size, const std::string& text) {
        if (text.empty()) return 0.0f;
        return body->CalcTextSizeA(size * f.scale, FLT_MAX, kWrap * f.scale_x, text.c_str()).y / f.scale;
      };
      // The prompts start at about 643; the bar ends above them. Text that still does not fit at
      // the smallest size moves the bar up into the space under the rows, then is clipped there.
      constexpr float kBarLimit = 636, kMinSize = 12;
      float bar_top = y - kStep + kHeight + 22;
      const float bar_top_highest = y - kStep + kHeight + 6;
      float size = 18;
      auto text_height = [&](float s) {
        return height(s, help) + (current->note.empty() ? 0 : kGap + height(s, current->note));
      };
      while (size > kMinSize && bar_top + 2 * kPad + text_height(size) > kBarLimit) size -= 1;
      bar_top = std::max(bar_top_highest, std::min(bar_top, kBarLimit - 2 * kPad - text_height(size)));
      const float bar_bottom = std::min(kBarLimit, bar_top + 2 * kPad + text_height(size));
      GradientBox(draw, f, kLeft, bar_top, kRight, bar_bottom, kPalette.help_top, kPalette.help_bottom,
                  kPalette.help_edge);
      draw->PushClipRect(f.At(kLeft, bar_top), f.At(kRight, bar_bottom), true);
      const float text_x = kLeft + (kRight - kLeft - kWrap) / 2;
      Text(draw, g.body, size, f, text_x, bar_top + kPad, Col(kPalette.help_text), help, kWrap);
      if (!current->note.empty()) {
        Text(draw, g.body, size, f, text_x, bar_top + kPad + height(size, help) + kGap,
             Col(current->pending ? kPalette.note_pending : kPalette.note), current->note, kWrap);
      }
      draw->PopClipRect();
    }

    // The prompts, as the game's: a button, then what it does, in lower case.
    constexpr float kPromptY = 654, kPromptSize = 19;
    float x = kLeft;
    auto prompt = [&](const std::string& button, const std::string& what) {
      x = ButtonGlyph(draw, g.heading, f, x, kPromptY, button) + 6;
      FitText(draw, g.heading, kPromptSize, f, x, kPromptY - kPromptSize * 0.62f, 300,
              Col(kPalette.heading), what, &shadow);
      x += TextWidth(g.heading, kPromptSize, f, what) + 16;
    };
    // While a key binding waits for its key, A does nothing: only B's cancel is shown.
    if (current && current->editable && !model_.capturing()) {
      if (current->kind == RowKind::kAction) {
        prompt("A", "reset");
      } else if (current->capture) {
        prompt("A", "set key");
      } else {
        prompt("A", "change");
      }
    }
    prompt("B", model_.capturing() ? "cancel" : "back");
    if (sections.size() > 1 && !model_.capturing()) {
      x = ButtonGlyph(draw, g.heading, f, x, kPromptY, "LB") + 4;
      prompt("RB", "section");
    }
  }

  PageModel model_;
};

// UI thread. The dialog deletes itself once input is released.
void OpenPage() {
  if (!g.dialog && g.drawer) g.dialog = new PageDialog(g.drawer);
}

void Toggle() {
  if (g.dialog) {
    g.dialog->CloseFromKey();
  } else {
    OpenPage();
  }
}

// Guest thread. Holds the game's input at once, so the press that asked never reaches the game
// (the dialog that follows also holds), and opens the page on the UI thread.
void RequestOpenFromGuest() {
  if (!g.drawer || !g.run_on_ui) return;
  if (g.hold.exchange(true)) return;  // already open, or opening
  g.run_on_ui([] {
    OpenPage();
    if (!g.dialog) g.hold = false;
  });
}

// Guest thread, on every pad poll: holds the game's input while the page is open. The pad combo is
// not decided here: the pad drivers filter it (open_combo.h) and call OnOpenCombo.
bool HoldGuestInput() { return g.hold; }

// Guest thread, from the pad driver's poll: Back and Start are both down.
void OnOpenCombo() {
  if (g.hold) return;
  REXLOG_INFO("settings page: opened with the pad combo (Back + Start)");
  RequestOpenFromGuest();
}

void OpenFromMenuRow() {
  REXLOG_INFO("settings page: opened from the main menu's PC SETTINGS row");
  RequestOpenFromGuest();
}

// Guest thread: the main menu's EXIT TO DESKTOP row.
void ExitFromMenuRow() {
  if (!g.run_on_ui || !g.request_close) return;
  g.run_on_ui([] { g.request_close(); });
}

void LogMenuRow(const char* message) { REXLOG_INFO("{}", message); }

}  // namespace

void BeginSession(SessionStart start) { g.session = std::move(start); }

void SetGamePadBindings(const pad::Bindings& bindings) { g.bindings = bindings; }

std::string CurrentPreset() {
  ValueMap in_effect;
  for (const Setting& setting : PlayerSettings()) {
    in_effect[std::string(setting.key)] = rex::cvar::GetFlagByName(setting.key);
  }
  return ActivePreset(in_effect,
                      topspin4::native::IsEnabled() ? kRendererNative : kRendererClassic);
}

std::string SessionReportJson() {
  return SessionSettingsJson(g.session.safe_start, CurrentPreset());
}

ImFont* BodyFont() { return g.body; }

void AddFonts(ImFontAtlas* atlas, const std::filesystem::path& game_data_root) {
  // The game's own UI font from the player's data; if it is missing, Segoe UI Bold when Windows
  // has it, else ImGui's built-in font (the page draws with whatever it got). Sizes cover a 1440p
  // window without upscaling the glyphs much.
  std::error_code error;
  std::filesystem::path file = style::GameFontPath(game_data_root);
  if (game_data_root.empty() || !std::filesystem::is_regular_file(file, error)) {
    REXLOG_WARN("Settings page: the game's font {} is not there, using a fallback", Utf8(file));
    file = "C:/Windows/Fonts/segoeuib.ttf";
    if (!std::filesystem::is_regular_file(file, error)) {
      REXLOG_WARN("Settings page: no fallback font either, using ImGui's built-in one");
      return;
    }
  }
  const std::string path = Utf8(file);
  g.heading = atlas->AddFontFromFileTTF(path.c_str(), 64.0f);
  g.body = atlas->AddFontFromFileTTF(path.c_str(), 40.0f);
  REXLOG_INFO("Settings page: font {}", path);
}

void Configure(rex::ui::ImGuiDrawer* drawer, rex::input::InputSystem* input,
               const std::filesystem::path& settings_path,
               std::function<void(std::function<void()>)> run_on_ui,
               std::function<void()> request_close) {
  g.drawer = drawer;
  g.input = input;
  g.settings_path = settings_path;
  g.run_on_ui = std::move(run_on_ui);
  g.request_close = std::move(request_close);
  if (input) input->SetGuestInputHold(HoldGuestInput);
  SetOpenComboHandler(OnOpenCombo);
  // The main menu rows' hooks (main_menu_rows_hooks.cpp, in the Swing Module's DLL) reach the
  // page and the window here.
  main_menu::MenuSink& menu_sink = main_menu::GetMenuSink();
  menu_sink.open_page = OpenFromMenuRow;
  menu_sink.exit_to_desktop = ExitFromMenuRow;
  menu_sink.log = LogMenuRow;
  rex::ui::RegisterBind("bind_ts4_settings_page", "F4", "Open the PC SETTINGS page", Toggle);
}

bool SetPlayerSettingFromScript(std::string_view key, std::string_view value) {
  const Setting* setting = FindSetting(key);
  if (!setting) return false;
  // The open page's model if there is one (its rows follow); else one built from this session.
  const std::vector<SettingChange> changes =
      g.dialog ? g.dialog->Change(key, value) : BuildModel().Change(key, value);
  for (const SettingChange& change : changes) Apply(change);
  if (changes.empty()) {
    if (setting->Allows(value)) {
      REXLOG_INFO("script input: set {} = {} changed nothing (already so, or fixed this session)",
                  key, value);
    } else {
      REXLOG_WARN("script input: set {} = {} was rejected, the setting does not allow it", key, value);
    }
  }
  return true;
}

bool RunScriptCommand(std::string_view name, std::string_view value) {
  if (name != kScriptKey) return false;
  if (value == "open") {
    OpenPage();
  } else if (value == "close") {
    if (g.dialog) g.dialog->CloseFromKey();
  } else if (value == "toggle") {
    Toggle();
  } else {
    REXLOG_WARN("script input: set {} wants open, close or toggle, not '{}'", name, value);
  }
  return true;
}

}  // namespace topspin4::settings
