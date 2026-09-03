# Touchpad lighting protocol

The Swift SF16-51T activity indicator is exposed by Acer Lighting Component
(`ALSSvc`). SwiftControl uses Acer's signed service instead of issuing raw HID
or embedded-controller commands.

## Connection and authorization

The service listens on one of these loopback port ranges:

- Ultron service: `55995` through `55999`
- alternate/PixArt service: `56955` through `56959`

After a normal WebSocket upgrade, every JSON request must be prefixed with the
four ASCII characters `ACER`. A JSON-only request is rejected with result 101.

Example capability request:

```text
ACER{"Function":"GET_ULTRON_LIGHTING_CAPABILITY"}
```

This laptop reports capability `1` and device `5`. AcerSense maps device `5`
to touchpad lighting and device `6` to Copilot-key lighting.

## Supported operations

SwiftControl currently implements:

- `GET_ULTRON_LIGHTING_CAPABILITY`
- `GET_ULTRON_LIGHTING_STATUS`
- `SET_ULTRON_LIGHTING_STATUS`
- `SET_ULTRON_LIGHTING_EFFECT`
- `TERMINATE_ULTRON_LIGHTING_EFFECT`

The effect request schema is:

```json
{
  "Function": "SET_ULTRON_LIGHTING_EFFECT",
  "Parameter": { "effect": "Blink" }
}
```

The signed Acer driver installs four effect definitions:

- `Blink`: 1.2 seconds, three flashes, non-looping
- `Breath`: 2.2 seconds, looping
- `Circle_R`: 44 frames at 14 fps
- `Twinkle_R`: 41 frames at 12 fps

Always send `TERMINATE_ULTRON_LIGHTING_EFFECT` before replacing a running effect
and after a temporary or looping effect. The Acer animation engine does not
reliably replace an active effect in place. Terminating an effect does not
change the user's global lighting switch.

## Trigger integration

SwiftControl owns one local auto-reset event:

- `Local\SwiftControl.Lighting.CodexComplete`

`SwiftControl.Notify.exe` signals this event. The Codex `notify` hook sends
`codex-complete`. Its effect and enabled state are configured on SwiftControl's
Touchpad light tab, and temporary Codex effects are terminated automatically.
Selecting an effect in the panel terminates any prior animation and previews the
new selection immediately.

The same tab exposes Acer's global activity-indicator switch and live localhost
service status. Preferences are stored for the current user under
`HKCU\Software\SwiftControl`; changing an individual trigger never changes the
global Acer switch.

## Diagnostics

The JavaScript probe is read-only:

```powershell
node tools\acer-lighting-probe.mjs
```

The compiled client can perform the same probe or a short, automatically
terminated Blink test:

```powershell
bin\SwiftControl.SelfTest.exe --lighting-probe
bin\SwiftControl.SelfTest.exe --lighting-blink
```
