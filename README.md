# Immersive AR

A Unity 6 mobile AR app that brings a printed pamphlet to life. Point your phone at the front of the pamphlet and a live video stream and real-time weather panel appear anchored to it in augmented reality.

---

## What it does

| Step | What happens |
|------|-------------|
| Launch | Onboarding screen with a **Start AR** button |
| Tap Start AR | Camera activates, scanning viewfinder appears with animated corner brackets |
| Point at pamphlet | Vuforia detects the image target — video and weather panel appear on top of it |
| Move pamphlet away | Scanning viewfinder returns automatically, AR controls hide |
| Target re-found | Experience resumes seamlessly |

---

## Features

- **Live video overlay** — HLS stream rendered onto a quad anchored to the image target
- **Real-time weather panel** — pulls current conditions from the Open-Meteo API (no API key required); updates every 5 minutes; panel floats and animates in/out with tracking
- **Scanning viewfinder** — animated corner brackets and pulsing status text guide the user to the target; disappears on lock, reappears on loss
- **Play / Pause** — on-screen button pauses and resumes the video stream
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
| Video | Unity `VideoPlayer` — HLS stream |
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
│   ├── LivePlayer.cs          # HLS video stream setup
│   ├── VuforiaActivationController.cs  # Gates AR until user opts in
│   ├── ScanningViewfinder.cs  # Animated scanning UI, hides/shows on tracking
│   ├── ScanningPulse.cs       # Pulse scale animation for scanning icon
│   ├── SafeAreaFitter.cs      # Notch / home bar safe area
│   ├── PlayPauseToggle.cs     # Play/pause button driving VideoPlayer
│   ├── TouchRotate.cs         # Touch/mouse drag rotation
│   ├── WeatherPanelCollapse.cs  # Collapse/expand weather panel
│   ├── WeatherPanelEntrance.cs  # Scale-in/out animation on tracking
│   └── WeatherPanelFloat.cs     # Subtle float bob effect
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
4. The Vuforia package resolves automatically from `Packages/com.ptc.vuforia.engine-11.4.4.tgz`

### Building

1. **File → Build Settings** → switch platform to Android
2. Set minimum API level to 26
3. Enable **IL2CPP** scripting backend
4. Build & Run to a connected device

### Updating the video stream URL

The HLS stream URL in `Assets/LivePlayer.cs` contains a time-limited signed token — update it when it expires:

```csharp
videoPlayer.url = "https://your-stream-url/chunks.m3u8?...";
```

---

## How tracking works

`Observer.cs` subscribes to Vuforia's `ObserverBehaviour.OnTargetStatusChanged`. It maps the raw `Status` enum (`TRACKED` / `EXTENDED_TRACKED` / `LIMITED`) through a configurable `TrackingStatusFilter` and fires two `UnityEvent`s:

- `OnTargetFound` — plays video, shows weather panel (animated), hides scanning viewfinder, shows UI buttons
- `OnTargetLost` — pauses video, hides weather panel, shows scanning viewfinder, hides UI buttons

`ScanningViewfinder` subscribes to these events at `Start()` (not `OnEnable`) so it continues listening even while inactive and correctly reactivates whenever the target leaves view.

---

## Screenshots

Development captures are in `Assets/Screenshots/`. On-device recordings better represent the final experience.

---

## Notes

- The weather panel lat/lon is hardcoded in `WeatherManager.cs` — update it for a different location
- Vuforia licence key is stored in `Assets/Resources/VuforiaConfiguration.asset`
- `Assets/Editor/WeatherPanelBuilder.cs` can regenerate the weather UI hierarchy via **Tools → Create Weather Panel**
