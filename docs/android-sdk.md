# Android (arm64) support for the SDK

`patches/rexglue-android/` is a series of 35 patches that, applied **after** `patches/rexglue/`,
makes the ReXGlue SDK build and run on Android arm64. `patches/sdl3/` holds one patch for the SDL3
submodule.

They come from the Android port of Top Spin 4
([hjcoggan/TS4-android](https://github.com/hjcoggan/TS4-android)), where the result runs the game at
a locked 60 fps on a Retroid Pocket Nova (Snapdragon 8 Gen 2 / Adreno 740, Android 16, 1280x960).

## Applying

Base: SDK `f5337cd`, then `patches/rexglue/` in name order, then this series in name order, then
the SDL patch inside `thirdparty/sdl3`:

```sh
git clone https://github.com/rexglue/rexglue-sdk.git work/rexglue-sdk
git -C work/rexglue-sdk checkout f5337cd
git -C work/rexglue-sdk checkout -b android

for p in patches/rexglue/*.patch; do
    git -C work/rexglue-sdk apply --whitespace=nowarn "$p"
    git -C work/rexglue-sdk add -A
    git -C work/rexglue-sdk commit -q -m "ref: $(basename "$p" .patch)"
done
git -C work/rexglue-sdk am --whitespace=nowarn patches/rexglue-android/*.patch

git -C work/rexglue-sdk submodule update --init --recursive
git -C work/rexglue-sdk/thirdparty/sdl3 apply \
    "$PWD/patches/sdl3/0001-do-not-wake-the-waiter-from-the-waiter.patch"
```

Each patch keeps its original commit message and authorship.

## What each group does

| Patches | Area |
| --- | --- |
| 0001-0010, 0015, 0018 | CMake: Android as its own platform, no cross-compiled host tools, no X11/Wayland, no `librt`/`libpthread`, SDL's Linux backends off |
| 0005, 0006, 0022 | UI: an `ANativeWindow` surface, created from the SDL window, reconnected across background/foreground |
| 0011 | Core: an aarch64 fiber context switch (bionic has no `makecontext`/`swapcontext`) |
| 0012, 0017, 0024 | Core: Android API level, executable/user folders, `REX_APP_FOLDER` |
| 0013, 0020 | Core: `ASharedMemory_create` / `memfd_create` instead of `/dev/ashmem` |
| 0014 | Core: no robust mutexes on bionic |
| 0016 | Core: `OpenAndroidContentFileDescriptor` (content URIs) |
| 0019, 0021 | Core: chain unclaimed SIGSEGV/SIGBUS/SIGILL, append-read streams |
| 0023, 0026 | Load the GPU plugin and the recompiled modules by soname |
| 0025 | Align Android ELF segments to 16 KB (Android 15+ devices) |
| 0027 | Load a custom Vulkan driver (Turnip) through libadrenotools, from an ADPKG |
| 0028 | GPU: reset the write pointer on ring init and stop faking completion |
| 0029 | Video: report exactly-4:3 modes as non-widescreen |
| 0030 | Core: cache page protections instead of parsing `/proc/self/maps` per fault |
| 0031 | Core: read the arm64 virtual counter instead of `clock_gettime` |
| 0032, 0034 | GPU: do not spin the command processor when idle; wake it for queued callbacks |
| 0033 | UI: diagnostics for the event loop (`log_event_loop`, `log_input_pump`) |
| 0035 | Build: stop rewriting the version header on every configure (it invalidated every object) |

Two of these are **not Android-specific** and are worth taking even without the rest:

- **0030** (`PageProtectionTable`): the write-watch fault path parsed `/proc/self/maps` on every
  fault. Caching it took the port from 14 to 60 fps.
- **0028** (ring-buffer write pointer) and **0032/0034** (command-processor idle spin and wakeup).

## The SDL patch

`patches/sdl3/0001-do-not-wake-the-waiter-from-the-waiter.patch` fixes a real SDL3 bug: on Android
`SDL_PumpEventsInternal` pushes its poll sentinel through `SDL_PushEvent`, every push signals the
lifecycle semaphore to wake the waiter, and when the pusher *is* the waiter `SDL_WaitEvent` finds
its own wakeup and never blocks. Measured on the Retroid: one whole core of userspace CPU, ~50k
loop iterations/s, 14.12 ms of CPU inside a 14.19 ms call, with zero syscalls (bionic's semaphore
fast path is userspace).

A wakeup is only needed for a push from another thread, so the patch skips it on the main thread.
Android input arrives on the Android UI thread and still signals. This affects any SDL3 app on
Android, not just this one.

## Licence

These patches are derived from the ReXGlue SDK (BSD-3-Clause) and are offered under the same terms.
The Android *app layer* of the port (the Gradle project and the activity) is a separate matter: it
is GPL-3.0, because it derives from `nfsmw-android`'s GPL-3.0 app layer, and is **not** part of this
series.
