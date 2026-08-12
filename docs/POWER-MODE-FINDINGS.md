# Acer and Windows Power-Mode Findings

## Brief

SwiftControl controls two independent but interacting policy layers on the Acer
Swift SF16-51T:

1. **Acer system profile** (`Silent`, `Normal`, or `Performance`) selects an
   Acer firmware/platform profile. On this machine it changes the fan-table
   status and the dynamic sustained CPU package power limit (PL1).
2. **Windows performance policy** (`Best power efficiency`, `Balanced`, or
   `Best performance`) controls Windows processor scheduling and performance
   preferences. It does not rewrite the Acer profile.

The most important measured result is:

| Acer profile | Dynamic PL1 target | Dynamic PL2 | Persisted `FanStatus` |
|---|---:|---:|---:|
| Silent | 15 W | 37 W | 2 |
| Normal | 20 W | 37 W | 0 |
| Performance | 30 W | 37 W | 3 |

Static HWiNFO limits remained at PL1 30 W and PL2 37 W. The profile-specific
PL1 is applied dynamically by the Acer/Intel platform stack after a delay; it
is not a normal Windows power-plan setting.

The practical model is therefore:

> Windows decides how aggressively to request performance. Acer firmware and
> Intel Dynamic Tuning decide the acoustic/thermal envelope and how much
> sustained package power is available. The most restrictive active condition
> wins when a workload reaches it.

For UI terminology, `ACER SYSTEM PROFILE` and `WINDOWS PERFORMANCE POLICY` are
more accurate than presenting both controls as interchangeable battery modes.

## Test system and scope

These findings are device-specific and should not be assumed to apply to every
Acer model.

- Model: Acer Swift SF16-51T
- Processor: Intel Core Ultra 7 256V, 8 cores / 8 threads
- BIOS: V1.31
- Operating system: Windows 11 Home, 25H2, build 26200.8973
- Primary test date: 2026-08-11
- Active Windows scheme during inspection:
  `1f8afba4-fb0c-4be2-a2ae-aece14470670 (Acer)`
- Instrumentation: HWiNFO64 8.34-5870, two-second CSV polling
- Most performance tests were run on battery. The final power-limit matrix was
  run on battery with Windows `Best performance` held constant.

AC power-limit values have not yet been measured. Acer or Intel Dynamic Tuning
may select different limits when plugged in.

## What is proven on this machine

### The Acer control reaches a proprietary platform interface

Acer Quick Access exposes these relevant localhost WebSocket commands on port
5141:

- `SystemUsageModes`
- `SystemUsageControl`
- `SystemUsageModeCapability`

`SystemUsageControl` accepts and retains these values:

- `0`: Silent
- `1`: Normal
- `2`: Performance

SwiftControl verifies a change using fresh delayed reads rather than trusting
the service's immediate `Set` response.

Readable symbols and diagnostic strings in Acer's installed
`AcerQAAgent.exe` show that its system-usage implementation includes:

- `SetFanStatus`
- `GetSwitchFanTableCurrentStatus`
- `GetSwitchFanTableCapability`
- `SetTargetSystemUsageMode`
- `GetCurrentSystemUsageMode`
- Acer HID 2025, WMI, SMBIOS, and thermal-event code paths

This is a proprietary Acer firmware/HID/WMI path, not merely a wrapper around
`powercfg`.

### The profiles select different fan-table statuses

`C:\ProgramData\Acer\QA\settings.json` was read after each verified profile
change on this SF16-51T:

| Selected profile | Reported profile value | Saved `FanStatus` |
|---|---:|---:|
| Silent | 0 | 2 |
| Normal | 1 | 0 |
| Performance | 2 | 3 |

This proves a profile-to-fan-table/status mapping. It does not reveal actual
RPM targets, temperature breakpoints, or hysteresis.

### The profiles select different dynamic sustained power limits

HWiNFO recorded the following platform limits while switching profiles on
battery:

| Profile | Static PL1 | Dynamic PL1 target | Static PL2 | Dynamic PL2 |
|---|---:|---:|---:|---:|
| Silent | 30 W | 15 W | 37 W | 37 W |
| Normal | 30 W | 20 W | 37 W | 37 W |
| Performance | 30 W | 30 W | 37 W | 37 W |

Observed target-application timing in this run was approximately:

- Silent: 15 W appeared about 26 seconds after selecting the profile.
- Normal: 20 W appeared about 17 seconds after selecting the profile, with a
  transient return to 30 W first.
- Performance: 30 W appeared about 5 seconds after selecting the profile.

These times are observations, not guaranteed protocol delays. Intel Dynamic
Tuning can respond to workload, temperature, battery state, and other platform
conditions, so future tests should wait at least 30 seconds after selecting an
Acer profile before measuring it.

### Acer profile changes do not rewrite the Windows power scheme

A complete `powercfg /qh SCHEME_CURRENT` snapshot was captured after selecting
each Acer profile and waiting for the service/firmware change. Each snapshot
contained 1,721 lines and produced the same SHA-256 hash:

```text
5842D427518544991C41180A3FF8673FCCDD268B7BC0B025F3AB08FDAB1BD294
```

`Compare-Object` reported zero differing lines for Silent versus Normal,
Normal versus Performance, and Silent versus Performance. The active Windows
scheme GUID also remained unchanged.

An earlier parser pass incorrectly reported six changed processor settings.
Exact-value capture and the later whole-scheme byte comparison disproved that
result. Treat it as a diagnostic parsing/timing artifact, not evidence.

## HWiNFO workload result

The final instrumented run used the same 30-second, eight-thread integer load
for each Acer profile. The machine was on battery and Windows `Best
performance` remained selected.

| Acer profile | Throughput, billion iterations/s | Package power, steady average | Average effective clock | Maximum package temperature |
|---|---:|---:|---:|---:|
| Silent | 4.522 | 17.293 W | 3,923 MHz | 68 C |
| Normal | 4.544 | 17.456 W | 3,925 MHz | 67 C |
| Performance | 4.477 | 17.534 W | 3,924 MHz | 69 C |

Additional observations:

- CPU utilization was 100% in the steady samples.
- No package thermal-throttling events were recorded.
- No package power-limit-exceeded events were recorded.
- HWiNFO's IA PL1 and PL2/PL3 limit-reason fields remained `No`.
- Whole-laptop battery discharge averaged roughly 28 W during the steady
  portions, but battery rate telemetry is slower and noisier than CPU package
  telemetry.
- Throughput varied by less than 1.5%, which is test noise for this short run.

This workload consumed only about 17.5 W of package power. It therefore did not
need Normal's full 20 W or Performance's 30 W budget. Silent's 15 W target was
also applied partway through its 30-second load, and PL1 is an averaged limit,
so this run is useful for identifying the configured limits but is not a
long-enough enforcement test for Silent.

Do not conclude that the three Acer profiles are universally equal. A longer
or heavier CPU workload, a combined CPU/GPU workload, or a hotter chassis can
reach the 15/20/30 W boundaries and make the profiles diverge.

## Earlier Acer-by-Windows CPU matrix

Before HWiNFO was available, all nine Acer/Windows combinations were measured
on battery using a short CPU-only integer benchmark. Values below are the
benchmark's throughput in billions of integer operations per second and are
useful primarily as relative comparisons within this matrix.

| Acer profile | Windows efficiency | Windows balanced | Windows performance |
|---|---:|---:|---:|
| Silent | 2.065 | 2.779 | 3.283 |
| Normal | 1.619 | 2.772 | 3.282 |
| Performance | 2.220 | 2.786 | 3.279 |

Longer follow-up samples included:

| Combination | Throughput |
|---|---:|
| Normal + Windows efficiency | 0.783 |
| Normal + Windows performance | 3.248 |
| Normal + Windows performance, repeat | 3.269 |
| Silent + Windows performance | 3.261 |
| Performance + Windows efficiency | 1.087 |
| Performance + Windows performance | 3.257 |

The defensible conclusions from these results are:

- Windows mode dominated this particular CPU-on-battery workload.
- Under Windows performance, all three Acer profiles were within about 0.6%.
- Under Windows balanced, all three were within about 0.5%.
- The unusually low Normal + Windows efficiency result was not reproduced
  sufficiently to establish that Normal is inherently worse. Scheduling,
  background activity, transition timing, and short test duration can dominate
  low-power measurements.
- These tests do not cover sustained CPU power above 20 W, integrated-GPU load,
  combined CPU/GPU power sharing, skin-temperature limits, or fan acoustics.

The later HWiNFO result explains why Windows performance could make the Acer
profiles appear equal: the measured CPU workload stayed below Normal and
Performance's dynamic PL1 budgets, and Silent's lower budget takes time to
become effective.

## How the controls stack

The controls should not be treated as two copies of the same three-position
slider.

### Windows performance policy

Windows controls processor power management and scheduling preferences,
including the energy/performance preference used by hardware-controlled
performance states. In broad terms:

- Best power efficiency requests lower energy use and less aggressive
  performance.
- Balanced adapts between responsiveness and energy use.
- Best performance requests aggressive responsiveness and performance.

SwiftControl uses the supported `powrprof.dll` AC/DC APIs, not `powercfg`
aliases, to read and set the current source-specific Windows mode:

| Mode | GUID |
|---|---|
| Best power efficiency | `961cc777-2547-4f9d-8174-7d86181b8a7a` |
| Balanced | all-zero GUID |
| Best performance | `ded574b5-45a0-4f42-8737-46345c09c238` |

### Acer system profile

On this system the Acer profile selects at least:

- a fan-table/status value; and
- a dynamic PL1 target of 15, 20, or 30 W.

The common 37 W PL2 allows the same short-burst ceiling in all three profiles.
The PL1 difference is more likely to appear during sustained work.

### Combined behavior

Windows can request less performance than the Acer envelope permits. It cannot
force the platform to sustain power above a firmware, electrical, battery,
thermal, or acoustic limit. Conversely, selecting Acer Performance does not
force applications to consume 30 W when Windows or the workload does not
request it.

This makes Acer Normal + Windows Best performance a reasonable combination:
it preserves aggressive Windows responsiveness while keeping the nominal
sustained CPU budget at 20 W. Acer Performance + Windows Best performance is
most useful for workloads that can actually use more than 20 W or for platform
conditions where the Performance fan table provides extra thermal headroom.

## Suggested user-facing pairings

Simple aligned presets remain understandable defaults:

| Preset | Acer system profile | Windows performance policy |
|---|---|---|
| Quiet / Eco | Silent | Best power efficiency |
| Balanced | Normal | Balanced |
| Maximum performance | Performance | Best performance |

They should be presented as convenient presets, not as the only meaningful
combinations. Advanced users may intentionally choose combinations such as
Normal + Best performance.

## Telemetry and testing pitfalls

### Battery telemetry lag

Windows and Acer battery capacity/rate values update in chunks and can lag the
workload that produced them. Early attempts to assign an instantaneous battery
wattage to every short benchmark row were unreliable. In particular, an early
rough value around 6.7 W was stale or idle-adjacent and must not be used as a
measured efficiency-mode result.

Use CPU package telemetry for CPU power and a long, steady observation window
for whole-system battery comparisons. Battery energy over several minutes is
more meaningful than one instantaneous charge-rate sample.

### Profile transition delay

The Acer service can report the requested profile as retained before Intel
Dynamic Tuning has finished applying its dynamic PL1 target. SwiftControl's
read-back verification proves that Acer accepted the profile; it does not mean
every downstream thermal/power policy has already settled.

For future automated benchmarking:

1. Set and verify the Acer profile.
2. Keep the Windows mode and power source fixed.
3. Wait at least 30 seconds.
4. Confirm HWiNFO's dynamic PL1 has reached the expected target.
5. Start a workload long enough to exceed PL1's averaging window, preferably
   60 to 120 seconds.
6. Record package power, IA power, effective clock, temperature, PL1/PL2,
   limit-reason flags, and benchmark throughput.
7. Cool the machine between runs and reverse or randomize test order.
8. Restore the user's original Acer and Windows modes in a `finally` path.

### Workload coverage

A CPU-only integer loop does not represent every laptop workload. A complete
follow-up matrix should include:

- a heavier vectorized CPU load capable of exceeding 20 W;
- an integrated-GPU-only load;
- a combined CPU/GPU load to reveal platform power sharing;
- AC and DC runs;
- fan RPM or acoustic measurements if a reliable sensor becomes available;
- sufficiently long runs to reach stable chassis and skin temperatures.

## Remaining unknowns

The investigation has not yet established:

- whether AC power uses the same 15/20/30 W dynamic PL1 targets;
- the PL1 averaging window or Tau selected by Acer/Intel Dynamic Tuning;
- any profile-specific GPU, SoC, memory, current, or platform-power limits;
- the numerical fan curves, RPM targets, temperature thresholds, or
  hysteresis;
- skin-temperature targets and passive cooling policies;
- sustained behavior after full thermal saturation;
- whether BIOS or driver updates change any of these values.

The installed Intel Dynamic Tuning/IPF configuration contains policies for
adaptive performance, intelligent thermal management, energy/performance
optimization, PowerBoss, and power sharing, but the OEM policy packages are
binary. Their per-profile numeric contents were not readable through normal
Windows power-plan queries or the exposed Acer WebSocket API.

## Useful external references

- [Acer Swift SF16-51T user manual](https://global-download.acer.com/GDFiles/Document/User%20Manual/User%20Manual_Acer_3.0_A_A.pdf?BC=ACER&LC=en&OS=ALL&SC=AAP_10&Step3=SWIFT+SF16-51T&acerid=638669086441945850)
- [Intel Dynamic Tuning Technology overview](https://www.intel.com/content/www/us/en/support/articles/000102775/processors.html)
- [Microsoft processor power-management overview](https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/configure-processor-power-management-options)
- [Microsoft processor energy-performance preference](https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/options-for-perf-state-engine-perfenergypreference)

## Bottom line

The Acer selector is not merely a cosmetic fan control. On this SF16-51T it
selects a fan-table status and a measurable dynamic sustained CPU limit:
15 W, 20 W, or 30 W. Windows mode remains a separate performance-request
policy. Their interaction depends on workload: if Windows or the application
does not ask for enough power to reach Acer's limit, changing Acer profiles can
produce little or no benchmark difference; under heavier sustained work, the
Acer PL1 boundary should become significant.
