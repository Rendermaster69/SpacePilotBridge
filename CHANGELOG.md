# Changelog

## v0.9.2
- Hard safety: SpacePilotBridge no longer sends mouse-pointer movement or mouse-drag input in any application.
- Photoshop/Illustrator pointerless presets use HWheel/Wheel for pan and wheel/shortcut actions for zoom.
- Global profile editor output is hard-disabled.
- Fusion defaults to Horizon Lock: HID Ry (roll) is suppressed while Rx/Rz remain available.
- Per-profile Horizon Lock checkbox added to Controls.
- LCD safe pacing: 3 ms delay after every feature report.
- Screensaver frames are full-frame writes by default and are never advanced before completion.
- LCD animation minimum frame interval raised to 900 ms.
- LCD host recovery kills the sacrificial helper process before disposing IPC handles.
