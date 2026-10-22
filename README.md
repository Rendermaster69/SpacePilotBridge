# SpacePilotBridge

Bring the original **3Dconnexion SpacePilot** back to life on modern Windows.

SpacePilotBridge is an open-source compatibility layer for the legacy **3Dconnexion SpacePilot (USB VID:PID `046D:C625`)**, designed to make it useful again with current creative, CAD, 3D, editing, and general desktop applications.

It reads the original SpacePilot directly through HID and can expose it as a modern virtual SpaceMouse for applications that support 3Dconnexion devices.

It also adds features the original software never had, including application profiles, LCD animations, widgets, custom controls, 2D navigation, editing controls, diagnostics, LED effects, and more.

<a href='https://ko-fi.com/M2P7299RQT' target='_blank'><img height='36' style='border:0px;height:36px;' src='https://storage.ko-fi.com/cdn/kofi6.png?v=6' border='0' alt='Buy Me a Coffee at ko-fi.com' /></a>

BTW I'm like the worst developer, so this is entirely vibecoded. Please forgive me.

---

## Why?

The original SpacePilot is still a great piece of hardware:

- 6DOF controller
- Large number of physical buttons
- Monochrome 240×64 LCD
- LED illumination
- Excellent build quality

Unfortunately, modern 3Dconnexion drivers no longer properly support it.

SpacePilotBridge aims to keep the hardware useful without requiring modifications to the SpacePilot itself.

---

# Features

## Native 6DOF navigation

The physical SpacePilot is translated into a virtual modern SpaceMouse device.

This allows native 3D navigation in applications such as:

- Autodesk Fusion
- Autodesk Maya
- ZBrush
- other applications supported by current 3DxWare versions

The six motion axes remain available:

- Translation X
- Translation Y
- Translation Z
- Rotation X
- Rotation Y
- Rotation Z

SpacePilotBridge does **not** emulate the 6DOF puck as a mouse.

---

## Application Profiles

SpacePilotBridge automatically detects applications as you use them.

Detected applications are stored in an application list and sorted by **Last Used**.

Each executable can be assigned to its own profile.

Example:

```text
Resolve.exe        → DaVinci Resolve
maya.exe           → Maya
Fusion360.exe      → Fusion 360
Photoshop.exe      → Adobe Photoshop
Illustrator.exe    → Adobe Illustrator
```

Custom applications can also be added manually.

You can create your own profiles for almost any Windows application.

---

# Custom Control System

Each profile can define its own controls.

The six SpacePilot axes can be mapped independently:

```text
Tx
Ty
Tz
Rx
Ry
Rz
```

Possible actions include:

- Native SpaceMouse movement
- Vertical scrolling
- Horizontal scrolling
- Zoom
- Keyboard shortcuts
- Frame stepping
- Timeline navigation
- Playback control
- Application-specific actions
- Disabled
- Custom actions

Positive and negative axis directions can have separate actions.

Profiles can also have their own:

- deadzones
- sensitivity
- repeat speed
- axis direction
- motion filtering

---

# DaVinci Resolve

SpacePilotBridge includes a Resolve-oriented editing profile.

A typical configuration can use:

```text
Twist left/right
→ reverse / forward playback

Release twist
→ stop

Push left/right
→ previous / next frame

Push up/down
→ navigate In / Out points

Top button
→ Play / Pause
```

The controls can be changed in the SpacePilotBridge settings.

The goal is to make the SpacePilot behave more like a compact editing controller instead of a 3D mouse while Resolve is active.

---

# Photoshop & Illustrator

SpacePilotBridge can switch automatically to a 2D navigation profile when Adobe applications are active.

Possible mappings include:

```text
Tx → horizontal navigation
Ty → vertical navigation
Tz → zoom
Rz → custom rotate action
```

The Windows mouse cursor itself is intentionally **not moved by SpacePilotBridge**.

This prevents accidental cursor movement when switching between applications such as browsers, CAD software, Photoshop, and Illustrator.

Scrolling can use:

- Shift + wheel
- native horizontal wheel
- arrow-key based navigation

depending on the application.

---

# Browser / Desktop Navigation

A Global profile can provide pointerless scrolling in applications such as:

- Brave
- Chrome
- Firefox
- Windows applications
- document viewers

Example:

```text
Tx → horizontal scroll
Ty → vertical scroll
```

The mouse pointer stays in its current position.

---

# Physical Button Mapping

SpacePilotBridge reads the original physical SpacePilot buttons directly.

This means the bridge can use buttons that are no longer exposed by modern 3DxWare.

Buttons include controls such as:

- 1
- 2
- 3
- 4
- 5
- 6
- Esc
- Alt
- Shift
- Ctrl
- Top
- Front
- Right
- Left
- Fit
- Config
- Dominant
- Speed +
- Speed -
- Rotation Lock

Depending on the selected action, a physical button can perform:

- a virtual SpaceMouse button
- a keyboard shortcut
- a built-in SpacePilotBridge function
- a profile action
- a disabled action

---

## Short Press / Long Press

Buttons can support different actions depending on how long they are held.

For example:

```text
Left short press
→ Left View

Right short press
→ Right View

Left or Right long press
→ Back View
```

This is useful because the original SpacePilot contains more physical controls than some modern virtual SpaceMouse models expose.

---

# LCD Support

The original SpacePilot contains a:

**240 × 64 monochrome LCD**

SpacePilotBridge can control it directly.

Supported LCD content includes:

- status information
- application/profile information
- widgets
- static images
- animations
- custom screensavers

---

## LCD Widgets

Available widget concepts include:

- Active profile
- Active application
- SpacePilot sensitivity
- Motion information
- Clock
- Latency
- LED mode
- Controller status
- Now Playing / media information

Example:

```text
PROFILE   FUSION 360
SPEED     1.00X
LATENCY   3.2 MS
```

---

## Spotify / Now Playing

SpacePilotBridge can display media information from Windows media sessions.

For supported media players this can show information such as:

```text
DAFT PUNK
VERIDIS QUO
```

The feature is intended to work with applications such as Spotify without requiring the SpacePilot LCD to become application-specific.

---

# Screensavers

Custom SpacePilot LCD screensavers can be stored in:

```text
%LOCALAPPDATA%\SpacePilotBridge\Screensavers
```

Supported sources include formats such as:

- GIF
- PNG
- BMP
- JPG
- `.spbsaver` presets

Images are converted for the SpacePilot's 1-bit monochrome display.

---

## Dithering

Different monochrome conversion methods are available for images:

- Threshold
- Bayer 2×2
- Bayer 4×4
- Floyd–Steinberg

This makes photographs and detailed artwork much more usable on the 240×64 display.

---

## Screensaver Playlist

Multiple screensavers can be selected for automatic playback.

Options include:

- playlist interval
- shuffle
- selected screensavers
- idle activation

---

## Idle Screensaver

The display can automatically start a screensaver after a period without SpacePilot input.

Moving the controller or pressing a button can return the display to its normal status view.

---

## LCD Performance

The SpacePilot LCD is old hardware and has a relatively slow physical refresh rate.

SpacePilotBridge therefore uses conservative frame pacing rather than attempting to display GIFs at their original frame rate.

The LCD writer attempts to:

1. complete the current LCD frame;
2. wait for the physical transfer to finish;
3. only then advance to the next animation frame.

Animation speed may therefore be lower than the original GIF frame rate.

This is intentional.

---

# LCD Recovery

The LCD system runs separately from the main controller logic.

This allows SpacePilotBridge to restart LCD handling without stopping 3D navigation.

A **Recover LCD** function can restart the LCD connection if the display becomes unresponsive.

LCD stability is still considered an area under active development due to the age and behavior of the original SpacePilot HID implementation.

---

# LED Control

The SpacePilot LED can be controlled by SpacePilotBridge.

Modes can include:

- Off
- On
- Activity
- Slow Blink
- Fast Blink
- Heartbeat
- Breathe-style animation
- Screensaver activity

The original hardware only exposes basic LED on/off control, so brightness-style effects are generated using timing.

---

# Diagnostics

A built-in Diagnostics page helps troubleshoot the bridge.

Information can include:

```text
Physical SpacePilot
Virtual SpaceMouse
Active Application
Active Profile

Tx / Ty / Tz
Rx / Ry / Rz

Buttons

Last latency
Average latency
Peak latency
Reports/sec

LCD status
LCD recovery count
Screensaver status
Memory usage
```

---

# Latency Meter

SpacePilotBridge measures the delay between receiving a physical SpacePilot HID report and forwarding the corresponding output.

This can help identify problems with:

- USB input
- virtual HID output
- system load
- application processing

---

# Startup Options

SpacePilotBridge can:

- start with Windows
- start minimized
- run silently in the system tray

The program is designed to remain running while the SpacePilot is connected.

---

# Dark Compact UI

The settings application uses a compact dark interface designed for quick configuration.

Main sections include:

- General
- Apps
- Buttons
- Controls
- LCD
- Screensavers
- LED
- Diagnostics

Settings are saved automatically.

There is no need to manually press a Save button after every change.

---

# Architecture

The basic data path is:

```text
Original SpacePilot
046D:C625
        │
        ▼
SpacePilotBridge
        │
        ├── Application profiles
        ├── Button mapping
        ├── LCD
        ├── LED
        ├── Editing controls
        └── Diagnostics
        │
        ▼
Virtual modern SpaceMouse
        │
        ▼
Current 3DxWare
        │
        ▼
Fusion / Maya / ZBrush / etc.
```

This allows modern applications to see a supported 3Dconnexion-style device while SpacePilotBridge communicates directly with the legacy hardware.

---

# Requirements

Recommended environment:

- Windows 10 or Windows 11
- Original 3Dconnexion SpacePilot
- USB VID:PID `046D:C625`
- Current 3Dconnexion / 3DxWare software
- .NET runtime / SDK required by the selected build
- Administrator access for initial virtual HID driver installation

---

# Installation

## Existing installation

If the virtual SpaceMouse driver is already installed and working, update only the application:

```text
Upgrade-App-Only.cmd
```

This is the recommended update method.

It preserves the known-working virtual HID driver.

---

## Important: Virtual Driver

Do **not** update or reinstall the virtual HID driver unless you specifically need to.

The SpaceMouse-only virtual driver is currently the recommended configuration for normal 3D use.

Experimental virtual driver variants may expose additional HID functionality, but can interfere with 3DxWare device detection.

If 3D navigation stops working after experimenting with the virtual driver, use:

```text
Rollback-VirtualDriver-SpaceMouseOnly.cmd
```

and restart Windows if necessary.

---

# Supported Hardware

Currently targeted hardware:

| Device | VID | PID | Status |
|---|---:|---:|---|
| 3Dconnexion SpacePilot | `046D` | `C625` | Primary target |

Other legacy 3Dconnexion devices may require additional protocol mappings.

---

# Current Status

SpacePilotBridge is an experimental community project.

Core functionality such as:

- SpacePilot HID input
- 6DOF motion
- virtual SpaceMouse output
- profiles
- button mapping
- LCD control
- screensavers
- LED control
- editing actions

is implemented, but some areas are still being refined.

In particular, the original SpacePilot LCD can behave unpredictably when continuously updated for long periods.

Testing, bug reports, and hardware observations are welcome.

---

# Known Limitations

### 3DxWare button names

The virtual SpaceMouse currently identifies as a modern SpaceMouse-class device.

3DxWare therefore only displays the button set associated with that virtual device.

The original SpacePilot has more physical buttons.

SpacePilotBridge works around this by handling additional physical buttons in its own button mapping system.

### LCD speed

The SpacePilot LCD is not a modern high-refresh display.

Complex animations should be designed for low frame rates.

Approximately **1–3 FPS** is generally much more appropriate than typical GIF frame rates.

### Application shortcuts

Some application controls are implemented through keyboard or wheel commands.

Applications with administrator privileges may reject input from a non-elevated SpacePilotBridge process.

### Adobe canvas rotation

Analog canvas rotation is difficult to implement without pointer movement because some Adobe rotate-view workflows are based on dragging.

SpacePilotBridge intentionally avoids moving the Windows cursor.

---

# Recommended Screensaver Format

For best results:

```text
Resolution: 240 × 64
Color:      monochrome / 1-bit
FPS:        1–3
Loop:       seamless
Contrast:   high
```

Avoid very fast motion or animation that depends on high temporal resolution.

The physical LCD refreshes much more slowly than a normal computer display.

---

# Development

The project combines several Windows technologies:

- raw HID access
- Windows virtual HID
- 3Dconnexion-compatible reports
- Win32 input handling
- Windows application detection
- LCD feature reports
- media session APIs
- application-specific profiles

The goal is to keep as much functionality as possible outside individual application plugins.

---

# Contributing

Bug reports and contributions are welcome.

Useful information to include in an issue:

```text
Windows version
SpacePilotBridge version
3DxWare version
Application + version
Physical SpacePilot detected: yes/no
Virtual SpaceMouse detected: yes/no
Relevant Diagnostics output
```

For LCD issues, also mention:

- screensaver/image used
- approximate runtime before failure
- configured FPS
- whether Recover LCD works

---

# Disclaimer

SpacePilotBridge is an independent project and is not affiliated with or endorsed by 3Dconnexion, Logitech, Autodesk, Adobe, Blackmagic Design, Maxon, Spotify, or other referenced software/hardware vendors.

All product names and trademarks belong to their respective owners.

Use experimental virtual HID drivers at your own risk.

---

# Project Goal

The SpacePilot is old.

It does not need to be obsolete.

SpacePilotBridge exists to keep great hardware useful on modern systems.

<a href='https://ko-fi.com/M2P7299RQT' target='_blank'><img height='36' style='border:0px;height:36px;' src='https://storage.ko-fi.com/cdn/kofi6.png?v=6' border='0' alt='Buy Me a Coffee at ko-fi.com' /></a>
