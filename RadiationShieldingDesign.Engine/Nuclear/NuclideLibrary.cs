using System.Text.Json;

namespace RadiationShieldingDesign.Engine.Nuclear;

/// <summary>Transmission model for one nuclide in one material. Thickness is always in mm.</summary>
public sealed class MaterialTransmission
{
    public string Material { get; init; } = "";
    public string Model { get; init; } = "Archer";      // "Archer" or "TVL"
    public double Alpha_per_mm { get; init; }
    public double Beta_per_mm { get; init; }
    public double Gamma { get; init; }
    public double Tvl_mm { get; init; }
    public string Source { get; init; } = "";
    public string? Note { get; init; }
    public bool LowConfidence { get; init; }

    /// <summary>Broad-beam transmission factor (0..1) through t_mm of this material.</summary>
    public double Transmission(double t_mm)
    {
        if (!(t_mm > 0)) return 1.0;

        if (Model == "TVL")
            return Math.Clamp(Math.Pow(10.0, -t_mm / Tvl_mm), 0.0, 1.0);

        double r = Beta_per_mm / Alpha_per_mm;
        double onePlusR = 1.0 + r;
        double z = Alpha_per_mm * Gamma * t_mm;

        // For large z, (1+r)e^z dominates: TF ≈ (1+r)^(-1/γ) · e^(-αx). Avoids overflow.
        if (z > 600.0)
            return Math.Clamp(Math.Pow(onePlusR, -1.0 / Gamma) * Math.Exp(-Alpha_per_mm * t_mm), 0.0, 1.0);

        double inside = onePlusR * Math.Exp(z) - r;
        if (!(inside > 0)) return 1.0;
        return Math.Clamp(Math.Pow(inside, -1.0 / Gamma), 0.0, 1.0);
    }
}

public sealed class Nuclide
{
    public string Id { get; init; } = "";
    public double HalfLife_h { get; init; }
    /// <summary>Point source in air, no patient self-attenuation. µSv·m²/(GBq·h).</summary>
    public double DoseRateConst_uSv_m2_per_GBq_h { get; init; }
    public string DoseRateConstSource { get; init; } = "";
    public IReadOnlyDictionary<string, MaterialTransmission> Materials { get; init; } =
        new Dictionary<string, MaterialTransmission>(StringComparer.OrdinalIgnoreCase);

    public double Lambda_per_h => Math.Log(2.0) / HalfLife_h;

    /// <summary>µSv·m²/(MBq·h)</summary>
    public double DoseRateConst_uSv_m2_per_MBq_h => DoseRateConst_uSv_m2_per_GBq_h / 1000.0;

    public bool TryGetMaterial(string material, out MaterialTransmission mt) =>
        Materials.TryGetValue(material, out mt!);
}

/// <summary>Loads nuclear-medicine nuclide data embedded in the Engine assembly (Nuclear/Data/nm_isotopes.json).</summary>
public sealed class NuclideLibrary
{
    public IReadOnlyDictionary<string, Nuclide> Nuclides { get; }
    public IReadOnlyDictionary<string, string> Sources { get; }

    private NuclideLibrary(Dictionary<string, Nuclide> nuclides, Dictionary<string, string> sources)
    {
        Nuclides = nuclides;
        Sources = sources;
    }

    private static readonly Lazy<NuclideLibrary> _default = new(LoadEmbedded);
    public static NuclideLibrary Default => _default.Value;

    public Nuclide Get(string id) =>
        Nuclides.TryGetValue(id, out var n) ? n : throw new KeyNotFoundException($"Nuclide '{id}' not in library.");

    public static NuclideLibrary LoadEmbedded()
    {
        var asm = typeof(NuclideLibrary).Assembly;
        string? name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("nm_isotopes.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Embedded resource nm_isotopes.json not found in Engine assembly.");
        using var s = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(s);
        return Parse(reader.ReadToEnd());
    }

    public static NuclideLibrary Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        var root = doc.RootElement;

        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("sources", out var so))
            foreach (var p in so.EnumerateObject()) sources[p.Name] = p.Value.GetString() ?? "";

        static string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        static double Num(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) ? v.GetDouble() : throw new FormatException($"Missing '{name}'.");

        var nuclides = new Dictionary<string, Nuclide>(StringComparer.OrdinalIgnoreCase);
        foreach (var jn in root.GetProperty("nuclides").EnumerateArray())
        {
            string id = Str(jn, "id") ?? throw new FormatException("Nuclide without id.");
            var mats = new Dictionary<string, MaterialTransmission>(StringComparer.OrdinalIgnoreCase);
            if (jn.TryGetProperty("materials", out var jm))
            {
                foreach (var p in jm.EnumerateObject())
                {
                    var m = p.Value;
                    string unit = Str(m, "unit") ?? "mm";
                    double toMm = unit switch
                    {
                        "mm" => 1.0,
                        "cm" => 10.0,
                        _ => throw new FormatException($"Unknown unit '{unit}' for {id}/{p.Name}")
                    };
                    string model = Str(m, "model") ?? "Archer";
                    bool archer = model == "Archer";
                    mats[p.Name] = new MaterialTransmission
                    {
                        Material = p.Name,
                        Model = model,
                        // α, β are per unit length: per cm → per mm is divide by 10. γ is dimensionless.
                        Alpha_per_mm = archer ? Num(m, "alpha") / toMm : 0,
                        Beta_per_mm = archer ? Num(m, "beta") / toMm : 0,
                        Gamma = archer ? Num(m, "gamma") : 0,
                        Tvl_mm = Num(m, "tvl") * toMm,
                        Source = Str(m, "source") ?? "",
                        Note = Str(m, "note"),
                        LowConfidence = m.TryGetProperty("lowConfidence", out var lc) && lc.ValueKind == JsonValueKind.True
                    };
                }
            }

            nuclides[id] = new Nuclide
            {
                Id = id,
                HalfLife_h = Num(jn, "halfLife_h"),
                DoseRateConst_uSv_m2_per_GBq_h = Num(jn, "doseRateConst_uSv_m2_per_GBq_h"),
                DoseRateConstSource = Str(jn, "doseRateConstSource") ?? "",
                Materials = mats
            };
        }
        return new NuclideLibrary(nuclides, sources);
    }
}
