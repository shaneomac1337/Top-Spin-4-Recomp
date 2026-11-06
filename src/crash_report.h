// Crash report (ticket 11, adapted from WWE All Stars; ADR-0004), game side: an unhandled
// exception on any thread writes one zip in the logs folder (a small dump with no guest memory,
// the session log, the settings file and a manifest) and tells the player where it is. What goes
// in is crash_report_plan.h.

#pragma once

#include <cstdint>
#include <filesystem>
#include <string>

namespace topspin4::crash {

// Installs the handler, and routes abort, pure virtual calls and bad CRT
// parameters through it; call as early as possible. In the dump helper
// (--ts4-write-crash-dump, crash_dump_request.h), writes the dump and exits.
void Install();

// Facts the report needs, known only later in startup.
void SetSettingsFile(const std::filesystem::path& path);
// The game window (an HWND), hidden before the report's message shows.
void SetWindow(void* native_window);
void SetGpu(const std::string& name, const std::string& driver_version);
// A message box that ends a session before it could draw; nothing under --headless.
void ShowFatalMessage(const std::string& utf8);
// The guest's memory views, left out of the dump (they hold game data).
void SetGuestMemory(const uint8_t* virtual_membase, const uint8_t* physical_membase);

// --ts4_debug_force_crash / --ts4_debug_force_abort: crashes the calling thread.
void CrashIfAsked();
// --ts4_debug_force_crash_at_frame: the guest frame to crash at (-1 for none), and the crash
// itself, called once each frame is shown (frame_dumps.cpp, on the GPU thread).
int64_t CrashFrameAsked();
void CrashIfAskedAtFrame(uint64_t frame);

}  // namespace topspin4::crash
