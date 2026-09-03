$ErrorActionPreference = 'Stop'

$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$source = Join-Path $PSScriptRoot 'src\SwiftControl'
$output = Join-Path $PSScriptRoot 'bin'
$gac = 'C:\Windows\Microsoft.NET\assembly'
$presentationCore = "$gac\GAC_64\PresentationCore\v4.0_4.0.0.0__31bf3856ad364e35\PresentationCore.dll"
$presentationFramework = "$gac\GAC_MSIL\PresentationFramework\v4.0_4.0.0.0__31bf3856ad364e35\PresentationFramework.dll"
$windowsBase = "$gac\GAC_MSIL\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll"
$systemXaml = "$gac\GAC_MSIL\System.Xaml\v4.0_4.0.0.0__b77a5c561934e089\System.Xaml.dll"

if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The built-in .NET Framework C# compiler was not found.'
}

New-Item -ItemType Directory -Path $output -Force | Out-Null

& $compiler `
    /nologo `
    /target:winexe `
    /optimize+ `
    /platform:anycpu `
    "/out:$output\SwiftControl.exe" `
    "/win32manifest:$source\app.manifest" `
    "/resource:$source\Assets\AcerSystemUsage.png,SwiftControl.AcerSystemUsage.png" `
    "/resource:$source\Assets\AcerSense.png,SwiftControl.AcerSense.png" `
    "/reference:$presentationCore" `
    "/reference:$presentationFramework" `
    "/reference:$windowsBase" `
    "/reference:$systemXaml" `
    "$source\AcerLighting.cs" `
    "$source\AcerProtocol.cs" `
    "$source\AssemblyInfo.cs" `
    "$source\BatteryHibernate.cs" `
    "$source\DashboardReader.cs" `
    "$source\LightingCoordinator.cs" `
    "$source\LightingSignals.cs" `
    "$source\LockAfterSuspend.cs" `
    "$source\MainWindow.cs" `
    "$source\ModeOsdWindow.cs" `
    "$source\ModernStandbyNetwork.cs" `
    "$source\PowerAutomation.cs" `
    "$source\PowerProfiles.cs" `
    "$source\TrayController.cs" `
    "$source\TrustedWifiWindow.cs" `
    "$source\TouchpadLightingSettings.cs" `
    "$source\WindowsPowerMode.cs" `
    "$source\Program.cs"

if ($LASTEXITCODE -ne 0) {
    throw "C# compiler exited with code $LASTEXITCODE."
}

& $compiler `
    /nologo `
    /target:exe `
    /optimize+ `
    /platform:anycpu `
    /main:SwiftControl.SelfTest `
    "/out:$output\SwiftControl.SelfTest.exe" `
    "$source\AcerLighting.cs" `
    "$source\AcerProtocol.cs" `
    "$source\BatteryHibernate.cs" `
    "$source\DashboardReader.cs" `
    "$source\LockAfterSuspend.cs" `
    "$source\ModernStandbyNetwork.cs" `
    "$source\PowerAutomation.cs" `
    "$source\PowerProfiles.cs" `
    "$source\TouchpadLightingSettings.cs" `
    "$source\WindowsPowerMode.cs" `
    "$PSScriptRoot\tools\SelfTest.cs"

if ($LASTEXITCODE -ne 0) {
    throw "Self-test compiler exited with code $LASTEXITCODE."
}

& $compiler `
    /nologo `
    /target:winexe `
    /optimize+ `
    /platform:anycpu `
    /main:SwiftControl.LightingNotify `
    "/out:$output\SwiftControl.Notify.exe" `
    "$source\LightingSignals.cs" `
    "$PSScriptRoot\tools\LightingNotify.cs"

if ($LASTEXITCODE -ne 0) {
    throw "Lighting notifier compiler exited with code $LASTEXITCODE."
}

Get-Item -LiteralPath "$output\SwiftControl.exe" |
    Select-Object FullName, Length, LastWriteTime
