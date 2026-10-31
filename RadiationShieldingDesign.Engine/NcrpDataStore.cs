using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RadiationShieldingDesign.Engine;
public sealed class NcrpDataStore
{
    public IReadOnlyDictionary<string, MaterialSpec> Materials { get; private set; } =
    new Dictionary<string, MaterialSpec>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, PreShieldSpec> PrimaryPreShield { get; private set; } =
    new Dictionary<string, PreShieldSpec>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, UseFactorSpec> UseFactors { get; private set; } =
    new Dictionary<string, UseFactorSpec>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<OccupancySpec> Occupancy { get; private set; } = [];
    public IReadOnlyList<CoeffSpec> PrimaryBarrierCoeffs { get; private set; } = new List<CoeffSpec>();
    public IReadOnlyList<CoeffSpec> SecondaryBarrierCoeffs { get; private set; } = new List<CoeffSpec>();
    public IReadOnlyList<PrimaryAirKermaSpec> PrimaryAirKerma { get; private set; } = new List<PrimaryAirKermaSpec>();
    public IReadOnlyList<SecondaryAirKermaSpec> SecondaryAirKerma { get; private set; } = new List<SecondaryAirKermaSpec>();

    // === CT sections (populated by LoadFromBundle) ===
    public class CtDlpCoeff
    {
        public string Part { get; set; } = "";
        public double K1_coeff { get; set; }
        public double K_per_cm { get; set; }
    }

    public IReadOnlyList<CTCoeffSpec> CTCoeffs { get; private set; } = Array.Empty<CTCoeffSpec>();

    // public List<JObject>? CTCoeffs { get; private set; }
    public CtDefaults? CTDefaults { get; private set; }
    public IReadOnlyDictionary<string, (double m_a, double m_b, double c_a, double c_b)>? CTSutton { get; private set; }
    public (CtDlpCoeff Head, CtDlpCoeff Body) CTDlpCoeffs { get; private set; }

    public class CtDefaults
    {
        public double patients_per_week_avg { get; set; }
        public double patients_per_week_busy { get; set; }
        public double dlp_per_patient_body_week { get; set; }
        public double dlp_per_patient_head_week { get; set; }

        public double dlp_per_patient_body_mGy_cm { get; set; }
        public double dlp_per_patient_head_mGy_cm { get; set; }
        public double effective_mAs_per_patient { get; set; }
        public double iso_center_height_ft { get; set; }
    }

    private static List<CTCoeffSpec> ParseCtCoefficientTable(JArray table, string firstColKey)
    {
        var list = new List<CTCoeffSpec>();

        foreach (var row in table.OfType<JObject>())
        {
            string label = row.Value<string>(firstColKey) ?? "";
            if (string.IsNullOrWhiteSpace(label) || label == "NaN" || label == "ALPHA")
                continue;

            // label is like "120 kVp" or "140 kVp"
            int kvp;
            {
                var s = label.Trim();
                var firstToken = s.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                if (!int.TryParse(firstToken, out kvp)) continue;
            }

            void AddTriplet(string material, int startIdx)
            {
                double a = row.Value<double?>($"Unnamed: {startIdx}") ?? 0.0;
                double b = row.Value<double?>($"Unnamed: {startIdx + 1}") ?? 0.0;
                double g = row.Value<double?>($"Unnamed: {startIdx + 2}") ?? 0.0;

                if (a > 0 && g > 0)
                {
                    list.Add(new CTCoeffSpec
                    {
                        kVp = kvp,
                        MaterialName = material,
                        Alpha = a,
                        Beta = b,
                        Gamma = g
                    });
                }
            }

            AddTriplet("Lead", 1);
            AddTriplet("Concrete", 4);
            AddTriplet("Steel", 7);
            AddTriplet("Gypsum Wallboard", 10);
        }

        return list;
    }

    public static NcrpDataStore LoadFromBundle(string bundlePath)
    {
        using var sr = File.OpenText(bundlePath);
        var root = (JObject)JToken.ReadFrom(new JsonTextReader(sr));
        var sheets = (JObject?)root["data"] ?? throw new InvalidDataException("JSON bundle missing 'data' root.");
        var store = new NcrpDataStore();
        var mats = new Dictionary<string, MaterialSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["Lead"] = new MaterialSpec { Name = "Lead", Density_g_cm3 = 11.34, Alpha = double.NaN, Beta = double.NaN, Gamma = double.NaN },
            ["Concrete"] = new MaterialSpec { Name = "Concrete", Density_g_cm3 = 2.35, Alpha = double.NaN, Beta = double.NaN, Gamma = double.NaN },
            ["Lite Concrete"] = new MaterialSpec { Name = "Lite Concrete", Density_g_cm3 = 1.76, Alpha = double.NaN, Beta = double.NaN, Gamma = double.NaN },
            ["Steel"] = new MaterialSpec { Name = "Steel", Density_g_cm3 = 7.85, Alpha = double.NaN, Beta = double.NaN, Gamma = double.NaN },
            ["Wood"] = new MaterialSpec { Name = "Wood", Density_g_cm3 = 0.60, Alpha = double.NaN, Beta = double.NaN, Gamma = double.NaN },
            ["Gypsum Wallboard"] = new MaterialSpec { Name = "Gypsum Wallboard", Density_g_cm3 = 0.80, Alpha = double.NaN, Beta = double.NaN, Gamma = double.NaN },
          //  ["Iron"] = new MaterialSpec { Name = "Iron", Density_g_cm3 = 7.87, Alpha = double.NaN, Beta = double.NaN, Gamma = double.NaN }
        };
        // Finally assign the read-only wrapper
        store.Materials = mats;
        //  }
        // Primary pre-shielding
        if (sheets.TryGetValue("primary_preshielding", out var psTok) && psTok is JArray psArr)
        {
            var pre = new Dictionary<string, PreShieldSpec>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in psArr.OfType<JObject>())
            {
                string type = row.Value<string>("Type") ?? "";
                if (string.IsNullOrWhiteSpace(type)) continue;
                double lead_mm = row.Value<double?>("Lead_mm") ?? 0.0;
                double conc_mm = row.Value<double?>("Concrete_mm") ?? 0.0;
                double steel_mm = row.Value<double?>("Steel_mm") ?? 0.0;
                pre[type] = new PreShieldSpec { Type = type, Lead_mm = lead_mm, Concrete_mm = conc_mm, Steel_mm = steel_mm };
            }
            store.PrimaryPreShield = pre;
        }
        // Use factors
        if (sheets.TryGetValue("use_factors", out var ufTok) && ufTok is JArray ufArr)
        {
            var uf = new Dictionary<string, UseFactorSpec>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in ufArr.OfType<JObject>())
            {
                string designation = row.Value<string>("Designation") ?? "";
                if (string.IsNullOrWhiteSpace(designation)) continue;
                uf[designation] = new UseFactorSpec
                {
                    Designation = designation,
                    U = row.Value<double?>("Use Factor") ?? 0.0
                };
            }
            store.UseFactors = uf;
        }
        // Occupancy factors
        if (sheets.TryGetValue("occupancy_factors", out var occTok) && occTok is JArray occArr)
        {
            var occ = new List<OccupancySpec>();
            foreach (var row in occArr.OfType<JObject>())
            {
                string location = row.Value<string>("NCRP147 - Table 4.1 occupancy factors") ?? "";
                if (string.IsNullOrWhiteSpace(location) || location == "Location") continue;
                occ.Add(new OccupancySpec
                {
                    Location = location,
                    T = row.Value<double?>("Unnamed: 1") ?? 0.0
                });
            }
            store.Occupancy = occ;
        }
        // Primary barrier coefficients
        if (sheets.TryGetValue("primary_barriers_coeffs", out var pbcTok) && pbcTok is JArray pbcArr)
        {
            store.PrimaryBarrierCoeffs = ParseCoefficientTable(pbcArr, "NCRP 147 - Table B.1. Primary barrier coefficients");
        }
        // Secondary barrier coefficients
        if (sheets.TryGetValue("sec_barriers_coeffs", out var sbcTok) && sbcTok is JArray sbcArr)
        {
            store.SecondaryBarrierCoeffs = ParseCoefficientTable(sbcArr, "NCRP 147 - Table C.1. Secondary barrier coefficients");
        }
        // Primary air kerma
        if (sheets.TryGetValue("primary_airkerma", out var pakTok) && pakTok is JArray pakArr)
        {
            var pak = new List<PrimaryAirKermaSpec>();
            foreach (var row in pakArr.OfType<JObject>())
            {
                string scenario = row.Value<string>("Workload Distribution") ?? "";
                if (string.IsNullOrWhiteSpace(scenario)) continue;
                pak.Add(new PrimaryAirKermaSpec
                {
                    Scenario = scenario,
                    Wnorm_mAmin_per_patient = row.Value<double?>("W_norm (mA min patient^-1)") ?? 0.0,
                    Kp1_mGy_per_patient = row.Value<double?>("K_p^1 (mGy/patient)") ?? 0.0,
                    TypicalPatientsAverage = row.Value<double?>("Typical Number of Patients (N) - Average") ?? 0.0, // NEW
                    TypicalPatientsBusy = row.Value<double?>("Typical Number of Patients (N) - Busy") ?? 0.0          // NEW
                });
            }
            store.PrimaryAirKerma = pak;
        }
        // Secondary air kerma (scatter)
        if (sheets.TryGetValue("sec_airkerma_scatter", out var sakTok) && sakTok is JArray sakArr)
        {
            var sak = new List<SecondaryAirKermaSpec>();
            foreach (var row in sakArr.OfType<JObject>())
            {
                string scenario = row.Value<string>("Workload Distribution") ?? "";
                if (string.IsNullOrWhiteSpace(scenario)) continue;
                sak.Add(new SecondaryAirKermaSpec
                {
                    Scenario = scenario,
                    Wnorm_mAmin_per_patient = row.Value<double?>("W_norm (mA min patient^-1)") ?? 0.0,
                    Ksec1_mGy_per_patient = row.Value<double?>("Leakage and Forward/Backscattered (K_sec^1, mGy/patient)") ?? 0.0
                });
            }
            store.SecondaryAirKerma = sak;
        }

        // --- CT-specific sections under "data" ---
        if (sheets.TryGetValue("CT_coeffs", out var ctCoeffsTok) && ctCoeffsTok is JArray ctCoeffsArr)
        {
            store.CTCoeffs = ParseCtCoefficientTable(ctCoeffsArr, "NCRP 147 - Fig A.2");
        }


        if (sheets.TryGetValue("ct_defaults", out var ctDefaultsTok) && ctDefaultsTok is JArray ctDefaultsArr)
        {
            var first = ctDefaultsArr.OfType<JObject>().FirstOrDefault();
            if (first != null)
            {
                store.CTDefaults = new CtDefaults
                {
                    patients_per_week_avg = first.Value<double?>("patients_per_week_avg") ?? 70,
                    patients_per_week_busy = first.Value<double?>("patients_per_week_busy") ?? 120,
                    dlp_per_patient_body_week = first.Value<double?>("dlp_per_patient_body_week") ?? 40,
                    dlp_per_patient_head_week = first.Value<double?>("dlp_per_patient_head_week") ?? 50,
                    dlp_per_patient_body_mGy_cm = first.Value<double?>("dlp_per_patient_body_mGy_cm") ?? 800,
                    dlp_per_patient_head_mGy_cm = first.Value<double?>("dlp_per_patient_head_mGy_cm") ?? 700,
                    effective_mAs_per_patient = first.Value<double?>("effective_mAs_per_patient") ?? 10000,
                    iso_center_height_ft = first.Value<double?>("iso_center_height_ft") ?? 3.35
                };
            }
        }

        if (sheets.TryGetValue("ct_sutton", out var ctSuttonTok) && ctSuttonTok is JArray ctSuttonArr)
        {
            var suttonDict = new Dictionary<string, (double m_a, double m_b, double c_a, double c_b)>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in ctSuttonArr.OfType<JObject>())
            {
                string voltage = row.Value<string>("Voltage") ?? "";
                if (string.IsNullOrWhiteSpace(voltage)) continue;
                double m_a = row.Value<double?>("m_a") ?? 0.0;
                double m_b = row.Value<double?>("m_b") ?? 0.0;
                double c_a = row.Value<double?>("c_a") ?? 0.0;
                double c_b = row.Value<double?>("c_b") ?? 0.0;
                var key = voltage.Replace("kV", "").Trim();
                suttonDict[key] = (m_a, m_b, c_a, c_b);
            }
            store.CTSutton = suttonDict;
        }

        if (sheets.TryGetValue("ct_dlp_coeffs_ncrp147", out var ctDlpTok) && ctDlpTok is JArray ctDlpArr)
        {
            var head = ctDlpArr.OfType<JObject>().FirstOrDefault(o =>
                string.Equals(o.Value<string>("Part"), "head", StringComparison.OrdinalIgnoreCase));
            var body = ctDlpArr.OfType<JObject>().FirstOrDefault(o =>
                string.Equals(o.Value<string>("Part"), "body", StringComparison.OrdinalIgnoreCase));

            store.CTDlpCoeffs = (
                Head: new CtDlpCoeff
                {
                    Part = "head",
                    K1_coeff = head?.Value<double?>("K1_coeff") ?? 0.0,
                    K_per_cm = head?.Value<double?>("K_per_cm") ?? 0.0
                },
                Body: new CtDlpCoeff
                {
                    Part = "body",
                    K1_coeff = body?.Value<double?>("K1_coeff") ?? 0.0,
                    K_per_cm = body?.Value<double?>("K_per_cm") ?? 0.0
                });
        }

        return store;
    }
    public bool TryGetPrimaryPatients(string scenario, out double nAvg, out double nBusy)
    {
        var hit = PrimaryAirKerma.FirstOrDefault(x =>
            x.Scenario.Equals(scenario, StringComparison.OrdinalIgnoreCase));

        if (hit is not null)
        {
            nAvg = hit.TypicalPatientsAverage;
            nBusy = hit.TypicalPatientsBusy;
            return true;
        }
        nAvg = nBusy = 0;
        return false;
    }

    // Case-insensitive compare helper
    private static readonly StringComparer OrdIgnore = StringComparer.OrdinalIgnoreCase;

    private static string NormalizeMaterial(string material)
    {
        if (string.IsNullOrWhiteSpace(material)) return "Lead";
        var m = material.Trim();
        if (OrdIgnore.Equals(m, "Lead-Eq")) return "Lead";
        if (OrdIgnore.Equals(m, "Std Concrete")) return "Concrete";
        if (OrdIgnore.Equals(m, "Standard Concrete")) return "Concrete";
        if (OrdIgnore.Equals(m, "Lite Concrete")) return "Concrete";
        if (OrdIgnore.Equals(m, "Gypsum")) return "Gypsum Wallboard";
        return m;
    }

    public bool TryGetPrimaryCoeff(string scenario, string material, out (double a, double b, double g) c)
    {
        string raw = string.IsNullOrWhiteSpace(material) ? "Lead" : material.Trim();
        string map = NormalizeMaterial(raw);

        // 1) exact scenario + (mapped|raw) material
        var hit = PrimaryBarrierCoeffs.FirstOrDefault(x =>
            OrdIgnore.Equals(x.Scenario, scenario) &&
            (OrdIgnore.Equals(x.MaterialName, map) || OrdIgnore.Equals(x.MaterialName, raw)));

        if (hit is not null) { c = (hit.Alpha, hit.Beta, hit.Gamma); return true; }

        // 2) scenario-agnostic fallback: any scenario with this material
        hit = PrimaryBarrierCoeffs.FirstOrDefault(x =>
            OrdIgnore.Equals(x.MaterialName, map) || OrdIgnore.Equals(x.MaterialName, raw));

        if (hit is not null) { c = (hit.Alpha, hit.Beta, hit.Gamma); return true; }

        c = default; return false;
    }

    public bool TryGetSecondaryCoeff(string scenario, string material, out (double a, double b, double g) c)
    {
        string raw = string.IsNullOrWhiteSpace(material) ? "Lead" : material.Trim();
        string map = NormalizeMaterial(raw);

        // 1) exact scenario + (mapped|raw) material
        var hit = SecondaryBarrierCoeffs.FirstOrDefault(x =>
            OrdIgnore.Equals(x.Scenario, scenario) &&
            (OrdIgnore.Equals(x.MaterialName, map) || OrdIgnore.Equals(x.MaterialName, raw)));

        if (hit is not null) { c = (hit.Alpha, hit.Beta, hit.Gamma); return true; }

        // 2) scenario-agnostic fallback
        hit = SecondaryBarrierCoeffs.FirstOrDefault(x =>
            OrdIgnore.Equals(x.MaterialName, map) || OrdIgnore.Equals(x.MaterialName, raw));

        if (hit is not null) { c = (hit.Alpha, hit.Beta, hit.Gamma); return true; }

        c = default; return false;
    }

    public bool TryGetCtCoeff(string kvp, string material, out (double a, double b, double g) coeff)
    {
        coeff = default;

        if (CTCoeffs is null || CTCoeffs.Count == 0) return false;

        if (!int.TryParse(kvp.Trim(), out int kv)) return false;

        string raw = string.IsNullOrWhiteSpace(material) ? "Lead" : material.Trim();
        string map = NormalizeMaterial(raw);

        // 1) exact kVp + (mapped|raw) material
        var hit = CTCoeffs.FirstOrDefault(x =>
            x.kVp == kv &&
            (OrdIgnore.Equals(x.MaterialName, map) || OrdIgnore.Equals(x.MaterialName, raw)));

        if (hit is not null && hit.Alpha > 0 && hit.Gamma > 0)
        {
            coeff = (hit.Alpha, hit.Beta, hit.Gamma);
            return true;
        }

        // 2) fallback: any kVp match (should rarely be needed, but keeps behavior consistent)
        hit = CTCoeffs.FirstOrDefault(x =>
            (OrdIgnore.Equals(x.MaterialName, map) || OrdIgnore.Equals(x.MaterialName, raw)));

        if (hit is not null && hit.Alpha > 0 && hit.Gamma > 0)
        {
            coeff = (hit.Alpha, hit.Beta, hit.Gamma);
            return true;
        }

        return false;
    }



    public bool TryGetPrimaryK1(string scenario, out double kp1_mGyPerPatient)
    {
        var hit = PrimaryAirKerma.FirstOrDefault(x =>
        x.Scenario.Equals(scenario, StringComparison.OrdinalIgnoreCase));
        if (hit is not null) { kp1_mGyPerPatient = hit.Kp1_mGy_per_patient; return true; }
        kp1_mGyPerPatient = 0.0; return false;
    }

    public bool TryGetPrimaryWnorm(string scenario, out double wnorm)
    {
        var hit = PrimaryAirKerma.FirstOrDefault(x =>
            x.Scenario.Equals(scenario, StringComparison.OrdinalIgnoreCase));
        if (hit is not null) { wnorm = hit.Wnorm_mAmin_per_patient; return true; }
        wnorm = 0; return false;
    }

    public bool TryGetSecondaryWnorm(string scenario, out double wnorm)
    {
        var hit = SecondaryAirKerma.FirstOrDefault(x =>
            x.Scenario.Equals(scenario, StringComparison.OrdinalIgnoreCase));
        if (hit is not null) { wnorm = hit.Wnorm_mAmin_per_patient; return true; }
        wnorm = 0; return false;
    }

    public bool TryGetSecondaryK1(string scenario, out double ksec1_mGyPerPatient)
    {
        var hit = SecondaryAirKerma.FirstOrDefault(x =>
        x.Scenario.Equals(scenario, StringComparison.OrdinalIgnoreCase));
        if (hit is not null) { ksec1_mGyPerPatient = hit.Ksec1_mGy_per_patient; return true; }
        ksec1_mGyPerPatient = 0.0; return false;
    }

    // --- Internal helpers ---
    private static List<CoeffSpec> ParseCoefficientTable(JArray table, string firstColKey)
    {
        var list = new List<CoeffSpec>();
        foreach (var row in table.OfType<JObject>())
        {
            string scenario = row.Value<string>(firstColKey) ?? "";
            if (string.IsNullOrWhiteSpace(scenario) || scenario == "NaN" || scenario == "ALPHA") continue;
            void AddTriplet(string material, int startIdx)
            {
                double a = row.Value<double?>($"Unnamed: {startIdx}") ?? 0.0;
                double b = row.Value<double?>($"Unnamed: {startIdx + 1}") ?? 0.0;
                double g = row.Value<double?>($"Unnamed: {startIdx + 2}") ?? 0.0;
                if (a > 0 && g > 0) // Skip invalid, beta can be 0
                {
                    list.Add(new CoeffSpec
                    {
                        Scenario = scenario.Trim(),
                        MaterialName = material,
                        Alpha = a,
                        Beta = b,
                        Gamma = g
                    });
                }
            }
            AddTriplet("Lead", 1);
            AddTriplet("Concrete", 4);
            AddTriplet("Steel", 7);
            AddTriplet("Wood", 10);
            AddTriplet("Gypsum Wallboard", 13);
        }
        return list;
    }
}