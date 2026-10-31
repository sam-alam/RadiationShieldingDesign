# Nuclear medicine (SPECT) shielding engine

UI-independent. Everything here is plain .NET 8 with no NuGet dependencies, and is covered by tests in
`RadiationShieldingDesign.Engine.Tests/Nuclear`.

## Pieces

| File | What it does |
|---|---|
| `Data/nm_isotopes.json` | Half-lives, dose-rate constants, Archer/TVL transmission data. Embedded resource. Every value cites its source. |
| `NuclideLibrary.cs` | Loads the JSON (`NuclideLibrary.Default`). `MaterialTransmission.Transmission(t_mm)` gives broad-beam TF. |
| `SpectPatientModel.cs` | Patient timeline (injections + where the patient is + voiding) → per-patient dose at 1 m for each region and nuclide, plus the worst 1-hour window. Exact decay integrals. |
| `SpectProtocols.cs` | Cardiac presets: Tc-99m 1-day, 2-day, dual isotope (Tl-201 + Tc-99m), PYP, MUGA, I-123 MIBG. Times are assumptions; PYP/MUGA/MIBG activities must be entered. |
| `NuclearBarrierSolver.cs` | Sizes one barrier: per-nuclide transmission, pre-existing layers, weekly design goal AND hourly public limit; returns thickness, governing limit, per-nuclide breakdown, warnings, trace. |

## How a barrier is calculated

1. For each protocol (timeline × patients/week), compute the per-patient dose at 1 m in each region, per nuclide.
2. For the barrier's calculation point, sum over the regions it sees: `D_week = Σ D_pt · N/wk / d² · T`,
   and `D_hour = Σ peakHour_pt · N_simultaneous / d²` (no occupancy factor).
3. Each nuclide is attenuated by its own transmission data: `Σ_i D_i · TF_pre,i · TF_i(x)`.
   The thickness is found numerically (bisection), separately for the weekly goal and the hourly limit; the larger governs.
   Summing K across nuclides and inverting once is wrong for dual-isotope work and is deliberately not done.

## Data decisions

- Dose-rate constants are point-source-in-air (no patient self-attenuation): conservative. A per-protocol
  `PatientAttenuationFactor` exists, default 1.0.
- Lead and concrete for all nuclides: Kusano & Caldwell 2014. Tc-99m gypsum, LW concrete, steel, glass: Oumano et al. 2025.
  Kusano's Tc-99m lead TVL (1.08 mm) is larger than Oumano's (0.870 mm), so Kusano is the conservative default.
- Tl-201 in concrete: the published Archer fit is degenerate as printed (β = −α → TF = 1 everywhere), so the
  single TVL from Kusano Table 3 (10.50 cm) is used.
- I-123 in concrete: fit flagged low-confidence (poor fit in source).
- No data for a nuclide/material pair: a pre-existing layer gives no credit for that nuclide (warning);
  a new barrier in that material gives no attenuation for it (warning).

## Minimal use

```csharp
var lib = NuclideLibrary.Default;
var tc1day = SpectProtocols.Create(SpectProtocols.Tc1DayRestStress);

var result = NuclearBarrierSolver.Solve(new NuclearBarrierRequest
{
    BarrierId = "Office wall",
    Workloads = { new ProtocolWorkload(tc1day, PatientsPerWeek: 60) },
    Regions   = { new RegionExposure(SpectRegions.Waiting, Distance_m: 2.0, MaxSimultaneousPatients: 3) },
    Occupancy = 1.0,
    P_week_mSv = 0.02,   // uncontrolled
    P_hour_mSv = 0.02,   // public, any one hour
    Material = "Lead",
    PreExisting = { new ShieldLayer("Gypsum Wallboard", 31.75) }
}, lib);
// result.RequiredThickness_mm, result.Governing, result.Contributions, result.Warnings, result.Trace
```
