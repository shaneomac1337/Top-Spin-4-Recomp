# Top Spin 4 Recomp. SDK boilerplate lives in cmake/rexglue.cmake.

cmake_minimum_required(VERSION 3.25)
project(topspin4 VERSION 1.0.1 LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 23)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

include(cmake/rexglue.cmake)

set(TOPSPIN4_SOURCES
    src/main.cpp
    src/achievement_toast.cpp
    src/crash_report.cpp
    src/not_set_up.cpp
    src/d3d/census.cpp
    src/d3d/host.cpp
    src/fault_log.cpp
    src/frame_dumps.cpp
    src/load_flash_host.cpp
    src/cutaway_rate_host.cpp
    src/module_shaders.cpp
    src/native/geometry_cache.cpp
    src/native/live_topup.cpp
    src/native/native_renderer.cpp
    src/native/pipelines.cpp
    src/native/pipeline_parts.cpp
    src/native/pipeline_store.cpp
    src/native/prebuild_plan.cpp
    src/native/prepare_output.cpp
    src/native/shader_library.cpp
    src/native/shader_pack.cpp
    src/native/shader_topup.cpp
    src/native/texture_layout.cpp
    src/native/textures.cpp
    src/native/topup_converter.cpp
    src/native/vk_context.cpp
    src/native/write_watch.cpp
    src/relaunch.cpp
    src/remapped_input.cpp
    src/script_input.cpp
    src/settings/schema_export.cpp
    src/settings/settings_cvars.cpp
    src/settings/settings_file.cpp
    src/settings/settings_page.cpp
    src/user_paths.cpp
)

if(WIN32)
    # Version information (src/topspin4.rc): an exe without it was rated suspicious by Windows
    # Defender, which then deleted the shader converter the game starts.
    enable_language(RC)
    list(APPEND TOPSPIN4_SOURCES src/topspin4.rc)
    set_source_files_properties(src/topspin4.rc PROPERTIES COMPILE_DEFINITIONS
        "TOPSPIN4_VERSION_MAJOR=${PROJECT_VERSION_MAJOR};TOPSPIN4_VERSION_MINOR=${PROJECT_VERSION_MINOR};TOPSPIN4_VERSION_PATCH=${PROJECT_VERSION_PATCH};TOPSPIN4_VERSION_TEXT=\"${PROJECT_VERSION}\";TOPSPIN4_DESCRIPTION=\"Top Spin 4 v${PROJECT_VERSION}\"")
    add_executable(topspin4 WIN32 ${TOPSPIN4_SOURCES})
else()
    add_executable(topspin4 ${TOPSPIN4_SOURCES})
endif()

rexglue_setup_target(topspin4 GPU_PLUGINS xenos)
# A developer build keeps the SDK's F6 cvar overlay (src/developer_build.h). A release build
# leaves it out so players have one settings screen:
#   pwsh scripts/build.ps1 -Action Configure -CMakeArgs '-DTOPSPIN4_DEVELOPER_BUILD=OFF'
# The scripts and the scene runner use the default, a developer build.
option(TOPSPIN4_DEVELOPER_BUILD "Developer build: keep the F6 cvar overlay" ON)
if(TOPSPIN4_DEVELOPER_BUILD)
    target_compile_definitions(topspin4 PRIVATE TOPSPIN4_DEVELOPER_BUILD=1)
endif()
# D3D hooks in each Module's DLL, and the sink they reach the exe through (src/d3d/sink.h).
include(cmake/d3d_hooks.cmake)
target_link_libraries(topspin4 PRIVATE topspin4_d3d_sink)

# The Native renderer (src/native, from WWE All Stars, ADR-0004). A shader pack converted with
# another XenosRecomp patch series is stale: the converter id is the series' hash.
file(GLOB TOPSPIN4_XENOSRECOMP_PATCHES CONFIGURE_DEPENDS
    "${CMAKE_CURRENT_SOURCE_DIR}/patches/xenosrecomp/*.patch")
list(SORT TOPSPIN4_XENOSRECOMP_PATCHES)
set(TOPSPIN4_XENOSRECOMP_SERIES "")
foreach(topspin4_patch IN LISTS TOPSPIN4_XENOSRECOMP_PATCHES)
    file(READ "${topspin4_patch}" topspin4_patch_text HEX)
    string(APPEND TOPSPIN4_XENOSRECOMP_SERIES "${topspin4_patch_text}")
endforeach()
string(SHA256 TOPSPIN4_XENOSRECOMP_SHA256 "${TOPSPIN4_XENOSRECOMP_SERIES}")
string(SUBSTRING "${TOPSPIN4_XENOSRECOMP_SHA256}" 0 16 TOPSPIN4_EXPECTED_CONVERTER_ID)
target_compile_definitions(topspin4 PRIVATE
    TOPSPIN4_EXPECTED_SHADER_CONVERTER_ID=0x${TOPSPIN4_EXPECTED_CONVERTER_ID}ull
    # Live shader top-ups (src/native/topup_converter.h) in a dev build: the XenosRecomp
    # scripts/build-xenosrecomp.ps1 builds.
    TOPSPIN4_DEV_XENOSRECOMP="${CMAKE_CURRENT_SOURCE_DIR}/out/build/xenosrecomp/XenosRecomp/XenosRecomp.exe"
    TOPSPIN4_DEV_XENOSRECOMP_INCLUDE="${CMAKE_CURRENT_SOURCE_DIR}/.tools/xenosrecomp/XenosRecomp/shader_common.h")
# The shipped pipeline key list (ADR-0010) goes beside the exe; Native prebuilds its keys
# before play. scripts/pipeline_keys.py merges new keys in.
set(TOPSPIN4_PIPELINE_KEYS "${CMAKE_CURRENT_SOURCE_DIR}/data/pipeline-keys.txt")
add_custom_command(OUTPUT "${CMAKE_CURRENT_BINARY_DIR}/pipeline-keys.txt"
    COMMAND "${CMAKE_COMMAND}" -E copy_if_different "${TOPSPIN4_PIPELINE_KEYS}"
        "${CMAKE_CURRENT_BINARY_DIR}/pipeline-keys.txt"
    DEPENDS "${TOPSPIN4_PIPELINE_KEYS}"
    COMMENT "Copying the shipped pipeline key list"
    VERBATIM)
add_custom_target(topspin4_pipeline_keys ALL DEPENDS "${CMAKE_CURRENT_BINARY_DIR}/pipeline-keys.txt")
add_dependencies(topspin4 topspin4_pipeline_keys)
# The achievement toast's sound goes in sounds\ beside the exe (src/achievement_sound.h).
set(TOPSPIN4_ACHIEVEMENT_SOUND "${CMAKE_CURRENT_SOURCE_DIR}/data/sounds/achievement.wav")
add_custom_command(OUTPUT "${CMAKE_CURRENT_BINARY_DIR}/sounds/achievement.wav"
    COMMAND "${CMAKE_COMMAND}" -E copy_if_different "${TOPSPIN4_ACHIEVEMENT_SOUND}"
        "${CMAKE_CURRENT_BINARY_DIR}/sounds/achievement.wav"
    DEPENDS "${TOPSPIN4_ACHIEVEMENT_SOUND}"
    COMMENT "Copying the achievement sound"
    VERBATIM)
add_custom_target(topspin4_sounds ALL DEPENDS "${CMAKE_CURRENT_BINARY_DIR}/sounds/achievement.wav")
add_dependencies(topspin4 topspin4_sounds)
# zstd (decompression only) and smol-v unpack the shader pack; both come with XenosRecomp in
# .tools/xenosrecomp (BSD-3 and MIT).
set(TOPSPIN4_XENOSRECOMP_THIRDPARTY "${CMAKE_CURRENT_SOURCE_DIR}/.tools/xenosrecomp/thirdparty")
set(TOPSPIN4_ZSTD "${TOPSPIN4_XENOSRECOMP_THIRDPARTY}/zstd/lib")
add_library(topspin4_native_deps STATIC
    ${TOPSPIN4_ZSTD}/common/debug.c
    ${TOPSPIN4_ZSTD}/common/entropy_common.c
    ${TOPSPIN4_ZSTD}/common/error_private.c
    ${TOPSPIN4_ZSTD}/common/fse_decompress.c
    ${TOPSPIN4_ZSTD}/common/xxhash.c
    ${TOPSPIN4_ZSTD}/common/zstd_common.c
    ${TOPSPIN4_ZSTD}/decompress/huf_decompress.c
    ${TOPSPIN4_ZSTD}/decompress/zstd_ddict.c
    ${TOPSPIN4_ZSTD}/decompress/zstd_decompress.c
    ${TOPSPIN4_ZSTD}/decompress/zstd_decompress_block.c
    ${TOPSPIN4_XENOSRECOMP_THIRDPARTY}/smol-v/source/smolv.cpp)
target_compile_definitions(topspin4_native_deps PRIVATE ZSTD_DISABLE_ASM)
target_compile_options(topspin4_native_deps PRIVATE -w)
target_include_directories(topspin4_native_deps PUBLIC
    ${TOPSPIN4_ZSTD} ${TOPSPIN4_XENOSRECOMP_THIRDPARTY}/smol-v/source)
target_link_libraries(topspin4 PRIVATE topspin4_native_deps)
# The renderer un-tiles textures with the SDK's Xenos texture layout code (BSD-3), which lives
# in the GPU plugin rather than the host runtime.
if(REXSDK_DIR)
    target_sources(topspin4 PRIVATE ${REXSDK_DIR}/src/graphics/pipeline/texture/util.cpp)
    target_include_directories(topspin4 PRIVATE ${REXSDK_DIR}/thirdparty/xxHash)
endif()
# Crash report (src/crash_report.cpp): the version and commit go in its manifest.
execute_process(COMMAND git rev-parse --short HEAD WORKING_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}"
    OUTPUT_VARIABLE TOPSPIN4_COMMIT OUTPUT_STRIP_TRAILING_WHITESPACE ERROR_QUIET)
if(NOT TOPSPIN4_COMMIT)
    set(TOPSPIN4_COMMIT "unknown")
endif()
target_compile_definitions(topspin4 PRIVATE
    TOPSPIN4_VERSION="${PROJECT_VERSION}" TOPSPIN4_COMMIT="${TOPSPIN4_COMMIT}")
if(WIN32)
    target_link_libraries(topspin4 PRIVATE dbghelp shell32)
    # The SDK stages its DLLs next to the exe only when the exe relinks, and a runtime
    # change that keeps the exports (or any GPU plugin change) does not relink it, so the
    # run would use stale DLLs. Copy them on every build.
    add_custom_target(topspin4_stage_dlls ALL
        COMMAND ${CMAKE_COMMAND} -E copy_if_different
            $<TARGET_RUNTIME_DLLS:topspin4> $<TARGET_FILE:rexgpu-xenos> $<TARGET_FILE_DIR:topspin4>
        COMMAND_EXPAND_LISTS
        VERBATIM)
    add_dependencies(topspin4_stage_dlls topspin4)
endif()

# The SDK's -ffp-model=strict does not reach these targets (it is scoped to the SDK's
# directory), so contraction is off explicitly: a PPC fmul followed by fadd never fuses.
foreach(topspin4_target topspin4 topspin4_recomp topspin4_Loader_DLL topspin4_Swing_DLL)
    if(TARGET ${topspin4_target})
        target_compile_options(${topspin4_target} PRIVATE -ffp-contract=off)
    endif()
endforeach()

# Host unit tests of pure host code (scripts/build.ps1 -Action Test): Catch2 from the SDK tree,
# opt-in and outside the default build, as in WWE All Stars.
option(TOPSPIN4_BUILD_TESTS "Build the host unit tests" OFF)
if(TOPSPIN4_BUILD_TESTS AND REXSDK_DIR)
    enable_testing()
    add_executable(topspin4_host_tests EXCLUDE_FROM_ALL
        tests/host/test_frame_dumps.cpp
        tests/host/test_user_paths.cpp
        tests/host/test_command_line.cpp
        tests/host/test_settings.cpp
        tests/host/test_load_flash.cpp
        tests/host/test_module_shaders.cpp
        tests/host/test_page_model.cpp
        tests/host/test_native_unavailable.cpp
        tests/host/test_presets_controls.cpp
        tests/host/test_options_row.cpp
        tests/host/test_crash_report.cpp
        tests/host/test_pad_bindings.cpp
        tests/host/test_script_input.cpp
        tests/host/test_swap_choice.cpp
        tests/host/test_device_order.cpp
        tests/host/test_movie_clock.cpp
        tests/host/test_cutaway_rate.cpp
        tests/host/test_present_hold.cpp
        tests/host/test_achievement_toast.cpp
        tests/host/test_relaunch.cpp
        tests/native/test_core_dynamic_state.cpp
        tests/native/test_arena_pair.cpp
        tests/native/test_clear_plan.cpp
        tests/native/test_depth_staging.cpp
        tests/native/test_debug_labels.cpp
        tests/native/test_draw_skips.cpp
        tests/native/test_fault_finder.cpp
        tests/native/test_frame_dumps.cpp
        tests/native/test_gpu_failure.cpp
        tests/native/test_gpu_timer.cpp
        tests/native/test_guest_decode.cpp
        tests/native/test_image_quality.cpp
        tests/native/test_live_topup.cpp
        tests/native/test_module_table.cpp
        tests/native/test_pass_names.cpp
        tests/native/test_pipeline_key.cpp
        tests/native/test_pipeline_parts.cpp
        tests/native/test_pipeline_store.cpp
        tests/native/test_prebuild_plan.cpp
        tests/native/test_prepare_output.cpp
        tests/native/test_rect_list.cpp
        tests/native/test_renderer_fallback.cpp
        tests/native/test_sampler_choice.cpp
        tests/native/test_shader_pack.cpp
        tests/native/test_shader_topup.cpp
        tests/native/test_texture_decode.cpp
        tests/native/test_texture_formats.cpp
        tests/native/test_texture_layout.cpp
        tests/native/test_thread_budget.cpp
        tests/native/test_write_watch.cpp
        src/settings/settings_cvars.cpp
        src/settings/settings_file.cpp
        src/script_input.cpp
        # The Native renderer's pure code, as WWE All Stars' native tests build it.
        src/native/live_topup.cpp
        src/native/pipeline_parts.cpp
        src/native/pipeline_store.cpp
        src/native/prebuild_plan.cpp
        src/native/prepare_output.cpp
        src/native/shader_pack.cpp
        src/native/shader_topup.cpp
        src/native/texture_layout.cpp
        src/native/write_watch.cpp
        ${REXSDK_DIR}/src/graphics/pipeline/texture/util.cpp
        ${REXSDK_DIR}/thirdparty/catch2/extras/catch_amalgamated.cpp)
    target_include_directories(topspin4_host_tests PRIVATE src
        ${REXSDK_DIR}/thirdparty/catch2/extras ${REXSDK_DIR}/thirdparty/xxHash
        ${REXSDK_DIR}/thirdparty/tomlplusplus/include)
    target_link_libraries(topspin4_host_tests PRIVATE rex::runtime topspin4_native_deps)
    target_compile_definitions(topspin4_host_tests PRIVATE
        TOPSPIN4_EXPECTED_SHADER_CONVERTER_ID=0x${TOPSPIN4_EXPECTED_CONVERTER_ID}ull
        TOPSPIN4_PIPELINE_KEYS="${TOPSPIN4_PIPELINE_KEYS}")
    add_test(NAME topspin4_host_tests COMMAND topspin4_host_tests)
endif()

if(NOT REXGLUE_ENTRYPOINT_GENERATED_SOURCES)
    # No codegen output yet: the host starts, says so and exits (ticket 03).
    target_compile_definitions(topspin4 PRIVATE TOPSPIN4_NO_GUEST_CODE)
endif()

# The v0.10.0 source-tree runtime does not propagate ImGui's include directory
# to the host, although its public UI headers include imgui.h.
if(REXSDK_DIR)
    target_include_directories(topspin4 PRIVATE
        $<TARGET_PROPERTY:imgui::imgui,INTERFACE_INCLUDE_DIRECTORIES>)
    # The crash report reads the GPU name through the SDK's Vulkan headers, which include
    # RenderDoc's; the settings file is read with toml++ (as in WWE All Stars); the remapped
    # controller driver wraps the SDK's SDL one, whose header includes SDL3's.
    target_include_directories(topspin4 PRIVATE ${REXSDK_DIR}/thirdparty/renderdoc
        ${REXSDK_DIR}/thirdparty/tomlplusplus/include ${REXSDK_DIR}/thirdparty/sdl3/include)
endif()
