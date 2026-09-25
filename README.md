# Immersive AR

A Unity 6 mobile AR app that brings a printed pamphlet to life. Point your phone at the front of the pamphlet and a video overlay and real-time weather panel appear anchored to it in augmented reality.

---

## What it does

| Step | What happens |
|------|-------------|
| Launch | Onboarding screen with a **Start AR** button |
| Tap Start AR | Camera activates, scanning viewfinder appears with animated corner brackets |
| Point at pamphlet | Vuforia detects the image target — video and weather panel appear on top of it |
| Tap ⛶ Fullscreen | Phone rotates to landscape; the video expands to a letterboxed fullscreen view with playback controls |
| Tap ✕ Exit | Returns to portrait and the AR view, restoring the tracked or scanning state |
| Move pamphlet away | Scanning viewfinder returns automatically, AR controls hide (fullscreen playback is unaffected) |
| Target re-found | Experience resumes seamlessly |

---

## Features

- **Video overlay** — local MP4 (`Assets/StreamingAssets/video.mp4`) rendered through a shared RenderTexture (`Assets/VideoRT.renderTexture`) onto a quad anchored to the image target
- **Fullscreen video mode** — ⛶ button rotates the device to landscape and expands the video to a letterboxed fullscreen view with runtime-built controls: play/pause, seek bar, time readout and an ✕ Exit button back to AR; playback survives tracking loss while fullscreen
- **Real-time weather panel** — pulls current conditions from the Open-Meteo API (no API key required); updates every 5 minutes; panel floats and animates in/out with tracking
- **Scanning viewfinder** — animated corner brackets and pulsing status text guide the user to the target; disappears on lock, reappears on loss
- **Play / Pause** — on-screen button pauses and resumes the video stream
- **Adaptive button color** — AR buttons sample the camera image behind them and adjust tint for contrast
- **Touch rotation** — drag to rotate the AR content
- **Safe area aware** — UI respects device notches and home indicators
- **Collapsible weather panel** — chevron toggle collapses/expands the weather detail rows

---

## Tech stack

| | |
|---|---|
| Engine | Unity 6 (6000.0.53f1) |
| AR tracking | Vuforia Engine 11.4.4 (image target) |
| Device AR | AR Foundation / ARCore |
| Video | Unity `VideoPlayer` → RenderTexture (local MP4 in `StreamingAssets`) |
| Weather | [Open-Meteo](https://open-meteo.com/) REST API |
| Platform | Android (mobile) |
| Language | C# |

---

## Project structure

```
Assets/
├── Scenes/
│   └── ARScene.unity          # Main scene
├── Scripts/
│   ├── Observer.cs            # Vuforia tracking events → video + UnityEvents
│   ├── Observer_Model.cs      # Vuforia tracking events → 3D model variant
│   ├── LivePlayer.cs          # Video setup (RenderTexture / material override) + retry
│   ├── FullscreenVideoToggle.cs  # Landscape fullscreen video with runtime-built controls
│   ├── AdaptiveButtonColor.cs # Contrast-aware button tinting from camera image
│   ├── VuforiaActivationController.cs  # Gates AR until user opts in
│   ├── ScanningViewfinder.cs  # Animated scanning UI, hides/shows on tracking
│   ├── ScanningPulse.cs       # Pulse scale animation for scanning icon
│   ├── SafeAreaFitter.cs      # Notch / home bar safe area
│   ├── PlayPauseToggle.cs     # Play/pause button driving VideoPlayer
│   ├── TouchRotate.cs         # Touch/mouse drag rotation
│   ├── WeatherPanelCollapse.cs  # Collapse/expand weather panel
│   ├── WeatherPanelEntrance.cs  # Scale-in/out animation on tracking
│   └── WeatherPanelFloat.cs     # Subtle float bob effect
├── StreamingAssets/
│   └── video.mp4              # Video content (gitignored — supply your own)
├── VideoRT.renderTexture      # Shared RT: VideoPlayer → AR quad + fullscreen UI
├── Weather stuff/
│   ├── WeatherManager.cs      # Open-Meteo polling + TMP field updates
│   └── BillboardFaceCamera.cs # Keeps weather panel facing the AR camera
└── Editor/
    ├── WeatherPanelBuilder.cs # Tools > Create Weather Panel menu
    └── Migration/
        └── AddVuforiaEnginePackage.cs  # Vuforia migration helper
```

---

## Setup

### Requirements

- Unity **6000.0.53f1**
- Android Build Support module (with NDK/JDK)
- A physical print of the registered image target
- A device with ARCore support (Android 8.0+)

### Opening the project

1. Clone the repo
2. Open in Unity Hub — select Unity 6000.0.53f1
3. Open `Assets/Scenes/ARScene.unity`
4. Place the Vuforia Engine tarball at `Packages/com.ptc.vuforia.engine-11.4.4.tgz` (it is gitignored, not in the repo — download it from the [Vuforia developer portal](https://developer.vuforia.com/downloads/sdk); the editor migration helper in `Assets/Editor/Migration/` also offers to set it up)
5. Supply your own Vuforia license key. The key lives in `Assets/Resources/VuforiaConfiguration.asset`, which is gitignored and not included in the repo. Get a free Basic key from the [Vuforia License Manager](https://developer.vuforia.com/license-manager), then paste it into **Vuforia Engine Configuration → App License Key** (open it from the `ARCamera`'s Vuforia Behaviour, or via **Window → Vuforia Configuration**). Unity creates the asset the first time you do this.

### Building

1. **File → Build Settings** → switch platform to Android
2. Set minimum API level to 26
3. Enable **IL2CPP** scripting backend
4. Build & Run to a connected device

### Supplying the video

`Assets/LivePlayer.cs` plays a local file from `Assets/StreamingAssets/` (default `video.mp4`, configurable via the `videoFileName` field in the inspector). The file itself is gitignored — drop your own MP4 there before building.

---

## How tracking works

`Observer.cs` subscribes to Vuforia's `ObserverBehaviour.OnTargetStatusChanged`. It maps the raw `Status` enum (`TRACKED` / `EXTENDED_TRACKED` / `LIMITED`) through a configurable `TrackingStatusFilter` and fires two `UnityEvent`s:

- `OnTargetFound` — plays video, shows weather panel (animated), hides scanning viewfinder, shows UI buttons
- `OnTargetLost` — pauses video, hides weather panel, shows scanning viewfinder, hides UI buttons

`ScanningViewfinder` subscribes to these events at `Start()` (not `OnEnable`) so it continues listening even while inactive and correctly reactivates whenever the target leaves view.

While fullscreen video is active, `FullscreenVideoToggle` sets `Observer.SuppressTrackingReactions` — tracking events still fire (keeping the viewfinder and weather panel in sync behind the video) but no longer pause playback or toggle the tracked-state UI. On exit the scene is handed back in its current tracked or scanning state, pausing the video until the target is found again if it was lost while watching.

---

## Screenshots

Development captures are in `Assets/Screenshots/`. On-device recordings better represent the final experience.

---

## Notes

- The weather panel lat/lon is hardcoded in `WeatherManager.cs` — update it for a different location
- Vuforia license key is stored in `Assets/Resources/VuforiaConfiguration.asset`, which is gitignored (see Setup)
- `Assets/Editor/WeatherPanelBuilder.cs` can regenerate the weather UI hierarchy via **Tools → Create Weather Panel**

---

## License

This project is released under the MIT License. See [LICENSE](LICENSE) for details.

Third-party components keep their own licenses. This includes Vuforia Engine, Unity packages, and the Open-Meteo API.
