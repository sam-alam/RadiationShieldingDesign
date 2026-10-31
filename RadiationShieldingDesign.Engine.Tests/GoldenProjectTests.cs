using System.IO;
using System.Globalization;

namespace RadiationShieldingDesign.Engine.Tests;

public class GoldenProjectTests
{
    private static string GoldenDir => Path.Combine(AppContext.BaseDirectory, "golden");

    internal static List<string> Run(string file)
    {
        var results = new List<string>();
        Exception? error = null;
        var th = new Thread(() =>
        {
            try
            {
                var vm = new MainViewModel();
                ProjectIO.LoadInto(vm, file);
                foreach (var b in vm.Barriers)
                {
                    vm.RecomputeBarrier(b);
                    results.Add(b.Id + "|" + b.RequiredThickness_mm.ToString("R", CultureInfo.InvariantCulture));
                }
            }
            catch (Exception ex) { error = ex; }
        });
        th.SetApartmentState(ApartmentState.STA);
        th.Start();
        th.Join();
        if (error != null) throw new Exception("Load failed: " + file, error);
        return results;
    }

    private static readonly Dictionary<string, double> Expected = new()
    {
        ["CT_SafeG.rsd.json" + "|" + "f6892221-7ec6-4824-ba72-cdddc462e017"] = 0,
        ["CT_SafeG.rsd.json" + "|" + "7bef46ee-4177-45e0-9314-ee83459669d9"] = 0,
        ["CT_SafeG.rsd.json" + "|" + "e4e061f8-a84c-462f-9f95-5a990d1ac5bc"] = 0,
        ["CT_SafeG.rsd.json" + "|" + "e5335bbf-c919-4fea-a5e2-c5ed56bb5f2b"] = 0,
        ["FL_SafeG.rsd.json" + "|" + "599e6390-9657-4b50-a127-c3937e4cbf93"] = 1.735793684896392,
        ["FL_SafeG.rsd.json" + "|" + "9ca0c3f8-30db-4645-bf12-c266e810a7dd"] = 0.8019397044763261,
        ["FL_SafeG.rsd.json" + "|" + "f9b045ad-180f-407a-9e95-2c8073f6003c"] = 1.4055345985646253,
        ["FL_SafeG.rsd.json" + "|" + "f0e1a871-881d-4a90-b04b-1c1765acd310"] = 1.451383113961664,
        ["PET_SafeG.rsd.json" + "|" + "27ba59df-855c-448b-94d1-5a742d73f175"] = 7.059455592494766,
        ["PET_SafeG.rsd.json" + "|" + "576f6bc9-487f-42cc-a43a-5f698aeb152f"] = 23.477400358878704,
        ["PET_SafeG.rsd.json" + "|" + "6041058b-45ea-4e79-a86d-0b51e446e50e"] = 23.477400358878704,
        ["PET_SafeG.rsd.json" + "|" + "77645c75-1563-4129-8bac-4717f5cd17ed"] = 22.9350667970191,
        ["RF_Rad_SafeG.rsd.json" + "|" + "aba93566-1642-453c-acfd-590924f8d14e"] = 1.4993731968350374,
        ["RF_Rad_SafeG.rsd.json" + "|" + "ae9d8e2a-c3e3-499b-baf8-e8821cb2d34f"] = 0.5569538520403663,
        ["RF_Rad_SafeG.rsd.json" + "|" + "91b791f3-17fa-412a-8da6-c48589c23b6b"] = 1.426956502175608,
        ["RF_Rad_SafeG.rsd.json" + "|" + "fae9b74d-da70-4319-a555-a482f7cf070e"] = 0.6816923394525695,
        ["Rad_SafeG.rsd.json" + "|" + "a0292248-cb36-4c9f-8eee-e807aabcaaeb"] = 1.576577616978272,
        ["Rad_SafeG.rsd.json" + "|" + "82ad81d6-e259-41fd-83cf-9a448a9c9a52"] = 0.31955998041143835,
        ["Rad_SafeG.rsd.json" + "|" + "f07d5dd5-cceb-475d-8e4c-f245f72dc8ce"] = 0.9711781481201682,
        ["Rad_SafeG.rsd.json" + "|" + "c4032d52-a817-4157-9408-b6052749a2f5"] = 0.6260075772366485,
    };

    public static IEnumerable<object[]> GoldenFiles() =>
        Expected.Keys.Select(k => k.Split('|')[0]).Distinct().Select(f => new object[] { f });

    [Theory]
    [MemberData(nameof(GoldenFiles))]
    public void Golden_Barrier_Thickness_Pinned(string fileName)
    {
        var results = Run(Path.Combine(GoldenDir, fileName));
        var expectedForFile = Expected.Where(kv => kv.Key.StartsWith(fileName + "|")).ToList();
        Assert.Equal(expectedForFile.Count, results.Count);
        foreach (var r in results)
        {
            var parts = r.Split('|');
            double actual = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            Approx.Equal(Expected[fileName + "|" + parts[0]], actual);
        }
    }
}
