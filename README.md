# SwiftControl v0.1

A compact Windows 11 utility for the useful hardware controls on an Acer Swift:

- read and toggle Acer's 80% optimized battery charging mode;
- show charge and AC state;
- apply four evidence-based profiles that pair the Acer system envelope with
  the Windows performance policy;
- independently select Acer Silent, Normal, or Performance and Windows Best
  power efficiency, Balanced, or Best performance from Advanced controls;
- read and set Windows 11 Best power efficiency, Balanced, or Best performance
  mode for the active power source;
- follow mode changes made by AcerSense or the keyboard;
- automatically choose a paired profile when plugged in, unplugged, or below a
  chosen battery percentage;
- show a lightweight OSD after a verified power-mode change;
- use a mode-specific notification-area icon;
- optionally launch quietly in the notification area at Windows sign-in.

SwiftControl talks only to Acer's installed localhost services and does not send
analytics. The launch-at-startup option uses the current user's standard Windows
`Run` entry and does not require administrator access.

## Controls

- Choose Battery Saver, Everyday, Responsive, or Maximum in the panel to apply
  both control layers together. Advanced controls expose each layer separately
  and show unmatched combinations as Custom.
- Left-click the notification-area icon to cycle Battery Saver, Everyday,
  Responsive, and Maximum.
- Right-click it to open the panel, choose any of those paired profiles, change
  the charging limit, or exit SwiftControl. The checked item follows the live
  Acer and Windows combination.
- The panel closes when focus moves elsewhere.
- Automatic power profiles are integrated into the profile row. The Auto
  button both enables switching and expands its controls; turning it off hides
  the irrelevant condition controls, status, and assignment badges. When open,
  select a Plugged, Battery, or Below condition chip and click its destination
  profile, or drag the chip onto that profile. A manual change remains active
  until the power condition changes or Resume auto is clicked. Low battery
  clears five percentage points above its entry threshold.
- Charge-limit changes show a confirmation notification; failed operations show
  an error notification.

The power-mode OSD embeds the system-usage graphic extracted from this laptop's
locally installed Acer Quick Access package. It is retained for personal local
use; remove the locally copied images in `Assets` before redistributing the
project.

## Requirements

- Acer Care Center and Acer Quick Access software-component services;
- Windows 11 with .NET Framework 4.8 (included on this machine).

This release was developed and tested on an Acer Swift SF16-51T. Acer service
commands and supported modes may differ on other models.

## Technical notes

- [Acer and Windows power-mode findings](docs/POWER-MODE-FINDINGS.md) records
  the measured firmware limits, benchmark matrices, control-layer behavior,
  testing pitfalls, and remaining unknowns for the SF16-51T.

## Build

Run:

```powershell
.\build.ps1
```

The result is `bin\SwiftControl.exe`. No SDK or package restore is required.
Generated binaries under `bin` are intentionally excluded from version control.

## Protocol notes

The app interoperates with the same loopback-only services used by AcerSense:

- Care Center: `wss://localhost:4343`
- Quick Access: `wss://localhost:5141`

`BatteryHealthy` value `0` is optimized charging (about 80%); value `1` is
full charging. Every change is read back and verified before the UI reports
success.
