# SpacePilotBridge v0.9.2

## v0.9.2 hotfix

- LCD animations now use backpressure: the next frame is not advanced until the previous 240x64 frame has fully flushed.
- LCD host timeouts are longer and animation pacing adapts to measured physical LCD flush time. Imported GIFs default to 2 fps before adaptive pacing.
- Per-app mouse/editor controls are now explicitly opt-in. Global/Fusion/Maya/ZBrush profiles cannot accidentally pan the Windows mouse.
- Switching profiles immediately releases any held MMB/Space/LMB rotate gesture.
- Controls tab includes an **Enable mouse/keyboard/editor controls for this profile** switch for custom apps.

SpacePilotBridge keeps the original Logitech/3Dconnexion SpacePilot (USB `046D:C625`) useful on current Windows systems. The physical SpacePilot is read directly; its 6DOF data can continue to feed the existing virtual SpaceMouse/3DxWare path for CAD and 3D applications, while v0.9 adds a configurable per-application control layer for editing and 2D software.

v0.9 is an **app-only upgrade** for an already working v0.8 installation. The existing virtual HID driver is unchanged.

## Upgrade

Run:

`Upgrade-App-Only.cmd`

The current virtual SpaceMouse/game-controller driver is left untouched.

## New in v0.9

### Configurable programs and profiles

The **Apps** tab is no longer limited to built-in application names.

- Foreground applications are learned automatically and sorted by most recently used.
- **Add executable...** can add any `.exe` manually.
- **Create profile** creates a completely custom profile and can immediately assign it to the selected app.
- Any discovered executable can be assigned to any profile.
- Buttons and six SpacePilot axes can then be customized independently for that profile.

This means Blender, Premiere Pro, After Effects, a proprietary in-house application, a game or any other Windows application can have its own SpacePilot behavior without a new SpacePilotBridge build.

### Six-axis control editor

The old one-mode editor is replaced by **Controls**. Every profile can independently configure:

- `Tx`
- `Ty`
- `Tz`
- `Rx`
- `Ry`
- `Rz`

Each axis has:

- positive action
- negative action
- neutral/release action
- deadzone
- rate
- repeat on/off
- custom positive/negative shortcut values

Available actions include pan, zoom, canvas rotation, Resolve shuttle, frame stepping, Go To In/Out, horizontal/vertical scrolling and arbitrary keyboard shortcuts.

### DaVinci Resolve default profile

The included **Resolve preset** is designed around physical SpacePilot gestures:

- twist `Rz` right -> play/shuttle forward (`L`)
- twist `Rz` left -> play/shuttle backward (`J`)
- return twist to centre -> stop (`K`)
- push left/right (`Tx`) -> previous/next frame
- push up/down (`Ty`) -> Go To Out / Go To In
- tap physical **Top** -> Space (play/pause)

All of these are ordinary profile mappings. They can be changed in **Controls** and **Buttons**; the preset is not hard-coded into Resolve-specific logic.

### Photoshop / Illustrator pan + zoom + rotate

Built-in 2D presets now map multiple axes rather than only pan:

**Adobe Photoshop**

- `Tx` / `Ty` -> canvas pan using Space + left drag
- `Tz` -> zoom
- `Rz` -> Rotate View gesture

**Adobe Illustrator**

- `Tx` / `Ty` -> canvas pan using middle-button drag
- `Tz` -> zoom
- `Rz` -> Rotate View gesture

The same actions are available to custom profiles.

### Spotify / Now Playing LCD widget

A new **Now Playing** LCD widget reads Spotify title/artist from the Windows system media-session API. It does not require a Spotify Web API token or a Spotify login inside SpacePilotBridge.

Choose `Now Playing` in any of the four LCD widget slots. If no Spotify media session is available the widget shows `SPOTIFY -`.

### LCD isolation retained

v0.8's separate LCD helper process remains in place. A wedged synchronous LCD HID call can be recovered by killing/restarting only the helper rather than freezing the main tray app.

### Existing C631 button limitation

The virtual 3Dconnexion device still identifies as a SpaceMouse Pro Wireless (`256F:C631`) because that is the compatibility identity already proven on this installation. 3DxWare therefore only displays the button slots that model exposes. All 21 physical SpacePilot buttons remain available in SpacePilotBridge's own **Buttons** tab for shortcuts, bridge actions and carrier-slot mappings.

## Notes

- CAD/3D motion for Fusion, Maya and ZBrush can remain on the native virtual-SpaceMouse path.
- Editor/2D actions are only emitted by profiles that explicitly configure them.
- If an application has customized keyboard shortcuts, change the corresponding SpacePilotBridge shortcut/action to match.
- Spotify metadata depends on Spotify publishing a Windows media session. The bridge fails closed to `SPOTIFY -` if that session is unavailable.


## v0.9.2 safety changes
The bridge never changes the Windows cursor position. Pointer-drag based pan/rotate modes were removed. Photoshop/Illustrator defaults are pointerless scroll/zoom mappings. Fusion Horizon Lock suppresses HID Ry, the SpaceMouse roll axis. LCD animations use conservative report pacing and full-frame writes for the original slow 240x64 display.
