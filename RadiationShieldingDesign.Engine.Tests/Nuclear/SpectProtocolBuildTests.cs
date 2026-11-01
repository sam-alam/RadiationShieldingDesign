using RadiationShieldingDesign.Engine.Nuclear;

namespace RadiationShieldingDesign.Engine.Tests.Nuclear;

public class SpectProtocolBuildTests
{
    [Fact]
    public void OneDay_Defaults_Give_The_Documented_Timeline()
    {
        var tl = SpectProtocols.Create(SpectProtocols.Tc1DayRestStress);
        Assert.Equal(2, tl.Injections.Count);
        Assert.Equal(new Injection("Tc-99m", 370, 0), tl.Injections[0]);
        Assert.Equal(new Injection("Tc-99m", 1110, 100), tl.Injections[1]);   // 90 + 15·2/3
        var expected = new[]
        {
            new LocationSegment(SpectRegions.Waiting, 0, 45),
            new LocationSegment(SpectRegions.Camera, 45, 65),
            new LocationSegment(SpectRegions.Waiting, 65, 90),
            new LocationSegment(SpectRegions.Stress, 90, 105),
            new LocationSegment(SpectRegions.Waiting, 105, 135),
            new LocationSegment(SpectRegions.Camera, 135, 155),
        };
        Assert.Equal(expected.Length, tl.Segments.Count);
        for (int i = 0; i < expected.Length; i++) Assert.Equal(expected[i], tl.Segments[i]);
    }

    [Fact]
    public void User_Activities_And_Times_Are_Used()
    {
        // Glenville-style: 6 mCi rest, 18 mCi stress, 30 min rest wait.
        var s = SpectProtocols.Defaults(SpectProtocols.Tc1DayRestStress);
        s.Activity1_MBq = 6 * 37;
        s.Activity2_MBq = 18 * 37;
        s.Wait_min = 30;
        var tl = SpectProtocols.Build(s);

        Approx.Equal(222, tl.Injections[0].Activity_MBq);
        Approx.Equal(666, tl.Injections[1].Activity_MBq);
        Assert.Equal(new LocationSegment(SpectRegions.Camera, 30, 50), tl.Segments[1]);
        Assert.Contains(tl.Notes, n => n.Contains("user-entered"));
    }

    [Fact]
    public void Dual_Isotope_Uses_Tl201_Then_Tc99m()
    {
        var tl = SpectProtocols.Create(SpectProtocols.DualIsotope);
        Assert.Equal("Tl-201", tl.Injections[0].NuclideId);
        Assert.Equal("Tc-99m", tl.Injections[1].NuclideId);
        Approx.Equal(55, tl.Injections[1].Time_min);
    }

    [Fact]
    public void Mibg_Has_Early_And_Late_Images_Only()
    {
        var s = SpectProtocols.Defaults(SpectProtocols.I123Mibg);
        s.Activity1_MBq = 370;
        var tl = SpectProtocols.Build(s);
        Assert.Equal(2, tl.Segments.Count);
        Assert.All(tl.Segments, seg => Assert.Equal(SpectRegions.Camera, seg.Region));
        Assert.Equal(240, tl.Segments[1].Start_min);
    }

    [Fact]
    public void Zero_Length_Periods_Are_Dropped()
    {
        var s = SpectProtocols.Defaults(SpectProtocols.Tc1DayRestStress);
        s.Gap_min = 0;
        var tl = SpectProtocols.Build(s);
        Assert.All(tl.Segments, seg => Assert.True(seg.End_min > seg.Start_min));
        Assert.Equal(5, tl.Segments.Count); // the waiting period between rest scan and stress disappears
    }

    [Fact]
    public void Glenville_Activities_Scale_Dose_Linearly()
    {
        // Same times, 6/18 mCi vs Kusano 10/30 mCi → exactly 0.6× the dose in every region.
        var lib = NuclideLibrary.Default;
        var kus = SpectPatientModel.Compute(SpectProtocols.Create(SpectProtocols.Tc1DayRestStress), lib);
        var s = SpectProtocols.Defaults(SpectProtocols.Tc1DayRestStress);
        s.Activity1_MBq = 222; s.Activity2_MBq = 666;
        var glen = SpectPatientModel.Compute(SpectProtocols.Build(s), lib);
        foreach (var k in kus)
        {
            var g = glen.Single(x => x.Region == k.Region && x.NuclideId == k.NuclideId);
            Approx.Equal(k.Dose_uSv_at1m * 0.6, g.Dose_uSv_at1m);
        }
    }
}
