# SwiftControl v0.1

A compact Windows 11 utility for the useful hardware controls on an Acer Swift:

- read and toggle Acer's 80% optimized battery charging mode;
- show charge and AC state;
- select Silent, Normal, or Performance firmware mode;
- follow mode changes made by AcerSense or the keyboard;
- show a lightweight OSD after a verified power-mode change;
- use a mode-specific notification-area icon;
- optionally launch quietly in the notification area at Windows sign-in.

SwiftControl talks only to Acer's installed localhost services and does not send
analytics. The launch-at-startup option uses the current user's standard Windows
`Run` entry and does not require administrator access.

## Controls

- Left-click the notification-area icon to cycle power modes.
- Right-click it to open the panel, choose a mode, change the charging limit, or
  exit SwiftControl.
- The panel closes when focus moves elsewhere.
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
