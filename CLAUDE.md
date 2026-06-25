# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

This is a Unity 6 (6000.0.53f1) mobile AR project. It uses **Vuforia Engine** (`com.ptc.vuforia.engine` 11.4.4, vendored as a local `.tgz` in `Packages/manifest.json`) for image-target tracking, combined with AR Foundation/ARCore for device AR. The app recognizes a printed image target and overlays a live-streamed video and a real-time weather panel on top of it.

There is no separate backend — all "server" interaction is:
- An HLS video stream pulled directly via `VideoPlayer` (`Assets/LivePlayer.cs`).
- A free, keyless weather REST API (Open-Meteo) polled directly from the device (`Assets/Weather stuff/WeatherManager.cs`).

## Working with this codebase

This is a Unity Editor project, not a CLI-buildable app in the usual sense — there are no npm/make/gradle scripts checked in for day-to-day iteration. Builds, play-mode testing, and asset/scene editing happen inside the Unity Editor (version pinned in `ProjectSettings/ProjectVersion.txt`: `6000.0.53f1`). When asked to "run" or "test" the project, that means opening it in the Unity Editor and pressing Play, or producing an Android build — there is no headless test runner configured in this repo (`com.unity.test-framework` is a package dependency but no `Tests/` assemblies exist yet).

C# scripts can be edited directly with normal tools; Unity will recompile them when the Editor regains focus. `.unity` scene files and `.prefab` files are YAML — avoid hand-editing them unless making a small, well-understood change (e.g. a single field tweak); prefer doing scene/prefab wiring from within the Editor.

The active scene is `Assets/Scenes/ARScene.unity` (set in `ProjectSettings/EditorBuildSettings.asset`); `CameraScene.unity` also exists in the project.

## Architecture

**Tracking → reaction pattern.** `Observer.cs` and `Observer_Model.cs` are near-identical Vuforia `ObserverBehaviour` event handlers (the duplication is from Vuforia's own handler template, customized per-use-case rather than shared). They subscribe to `ObserverBehaviour.OnTargetStatusChanged`, translate Vuforia's `Status` enum (`TRACKED` / `EXTENDED_TRACKED` / `LIMITED`) through a `TrackingStatusFilter` into a simple found/lost boolean, and drive scene-specific reactions:
- `Observer.cs` plays/pauses a `VideoPlayer` and toggles a `hideUI` GameObject when the image target is found/lost.
- `Observer_Model.cs` instead shows/hides a 3D model (`hideModel`) — used where the augmentation is a model rather than a video.

Both also implement Vuforia's optional pose-smoothing (interpolating transform pose across frames when tracking degrades to `EXTENDED_TRACKED`) via an internal `PoseSmoother`/`PoseLerp` pair — this logic is copied from Vuforia's sample code, not custom.

**Activation gating.** `VuforiaActivationController.cs` keeps `VuforiaBehaviour` disabled until explicitly activated (e.g. from a "start AR" UI button), so the camera/tracking pipeline doesn't run before the user opts in. `UI_Manager.cs` is a thin helper for hiding intro/onboarding UI once AR starts.

**Video playback.** `Assets/LivePlayer.cs` configures a `VideoPlayer` to stream from a hardcoded HLS URL (`videoplayback`/live stream with a signed, time-limited token — this URL will expire and need regenerating). `ARVideoPlayerController.cs` provides play/pause via `Button`s wired to a `VideoPlayer`/`Renderer`. `Assets/Scripts/PlayPauseToggle.cs` is a separate, simpler play/pause control that swaps an `Image` sprite and drives global `Time.timeScale` (0 = paused) rather than calling the `VideoPlayer` directly — the two controllers are not wired together, so check which one a given button is actually bound to before assuming play/pause state. `TouchRotate.cs` lets the user rotate the tracked content with touch/mouse drag.

**Weather overlay.** `Assets/Weather stuff/WeatherManager.cs` polls the Open-Meteo API on a timer (default every 5 minutes) for a hardcoded lat/lon and writes results into TextMeshPro fields. `BillboardFaceCamera.cs` keeps that panel facing the AR camera every frame (falls back through `Camera.main` → GameObject named `"ARCamera"` → first active camera, retried on a timer rather than failing). `Assets/Editor/WeatherPanelBuilder.cs` is an Editor-only tool (`Tools > Create Weather Panel` menu) that procedurally builds the weather panel's UI hierarchy and TMP text fields and wires them to a new `WeatherManager` — use it to regenerate the panel rather than hand-building the UI tree.

**Vuforia package management.** `Assets/Editor/Migration/AddVuforiaEnginePackage.cs` is Vuforia's own `[InitializeOnLoad]` migration helper: on Editor load it checks `Packages/manifest.json` for the pinned Vuforia tarball version and prompts to update/move package files and resolve sample dependencies. This is vendored Vuforia tooling, not project-specific — avoid modifying it when upgrading Vuforia; replace the `.tgz` in `Assets/Editor/Migration/` instead.

## Notable repo state

- `Assets/Editor.meta`, scripts under `Assets/Editor/` (not `Editor/`-suffixed assemblies elsewhere) follow Unity's convention that anything under a folder named `Editor` is editor-only and stripped from player builds.
- The large Vuforia package was previously committed directly and has since been removed from version control in favor of the gitignored `Packages` cache (see git history) — don't re-commit large `.tgz`/package binaries.
- `.claudeignore` excludes Unity's generated/cache directories (`Library`, `Temp`, `.utmp`, `Logs`, `UserSettings`) and binary asset types from Claude's context — `.utmp` in particular is a Gradle/CMake build-cache directory that is tracked in git (historical artifact) but should not be treated as source when reviewing diffs.
- `com.coplaydev.coplay` (in `Packages/manifest.json`, git-sourced from `CoplayDev/unity-plugin#beta`) is the Coplay Editor plugin that exposes this Unity Editor session to Claude Code via MCP. `Packages/Coplay/` holds its runtime data (logs, settings) and is gitignored except for the package's own `.gitignore`.
