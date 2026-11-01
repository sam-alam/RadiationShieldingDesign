using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadiationShieldingDesign.Engine.Nuclear;

namespace RadiationShieldingDesign
{
    /// <summary>Observable item that raises <see cref="Changed"/> on any property change.</summary>
    public abstract class SpectItem : ObservableObject
    {
        public event Action? Changed;

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (IsInput(e.PropertyName)) Changed?.Invoke();
        }

        protected virtual bool IsInput(string? propertyName) => true;

        protected void RaiseChanged() => Changed?.Invoke();

        /// <summary>Hooks a collection so item edits and add/remove raise <paramref name="changed"/>.</summary>
        public static void Watch<T>(ObservableCollection<T> items, Action changed) where T : SpectItem
        {
            foreach (var i in items) i.Changed += changed;
            items.CollectionChanged += (_, e) =>
            {
                if (e.OldItems != null) foreach (SpectItem i in e.OldItems) i.Changed -= changed;
                if (e.NewItems != null) foreach (SpectItem i in e.NewItems) i.Changed += changed;
                changed();
            };
        }
    }

    public partial class InjectionItem : SpectItem
    {
        [ObservableProperty] private string nuclideId = "Tc-99m";
        [ObservableProperty] private double activityMBq;
        [ObservableProperty] private double timeMin;
    }

    public partial class SegmentItem : SpectItem
    {
        [ObservableProperty] private string region = SpectRegions.Waiting;
        [ObservableProperty] private double startMin;
        [ObservableProperty] private double endMin;
    }

    public partial class LayerItem : SpectItem
    {
        [ObservableProperty] private string material = "Gypsum Wallboard";
        [ObservableProperty] private double thicknessMm;
    }

    public partial class RegionSeenItem : SpectItem
    {
        public RegionSeenItem(string region) { Region = region; }
        public string Region { get; }
        [ObservableProperty] private bool sees;
        [ObservableProperty] private double distanceM = 2.0;
        [ObservableProperty] private double maxSimultaneousPatients = 1;
    }

    public partial class ProtocolItem : SpectItem
    {
        private readonly PatientTimeline _source;

        public ProtocolItem(PatientTimeline source, double patientsPerWeek)
        {
            _source = source;
            name = source.Name;
            this.patientsPerWeek = patientsPerWeek;
            Injections = new ObservableCollection<InjectionItem>(source.Injections.Select(i =>
                new InjectionItem { NuclideId = i.NuclideId, ActivityMBq = i.Activity_MBq, TimeMin = i.Time_min }));
            Segments = new ObservableCollection<SegmentItem>(source.Segments.Select(s =>
                new SegmentItem { Region = s.Region, StartMin = s.Start_min, EndMin = s.End_min }));
            Watch(Injections, RaiseChanged);
            Watch(Segments, RaiseChanged);
        }

        [ObservableProperty] private string name;
        [ObservableProperty] private double patientsPerWeek;
        public ObservableCollection<InjectionItem> Injections { get; }
        public ObservableCollection<SegmentItem> Segments { get; }
        public string NotesText => string.Join(Environment.NewLine, _source.Notes);

        public PatientTimeline ToTimeline() => new()
        {
            Name = Name,
            Injections = Injections.Select(i => new Injection(i.NuclideId, i.ActivityMBq, i.TimeMin)).ToList(),
            Segments = Segments.Select(s => new LocationSegment(s.Region, s.StartMin, s.EndMin)).ToList(),
            Voids = _source.Voids.ToList(),
            PatientAttenuationFactor = _source.PatientAttenuationFactor,
            Notes = _source.Notes.ToList()
        };
    }

    public class NuclideBreakdownRow
    {
        public string Nuclide { get; init; } = "";
        public string UnshieldedWeek { get; init; } = "";
        public string UnshieldedHour { get; init; } = "";
        public string PreTransmission { get; init; } = "";
        public string ShieldedWeek { get; init; } = "";
        public string ShieldedHour { get; init; } = "";
    }

    public partial class BarrierItem : SpectItem
    {
        public BarrierItem(string name)
        {
            this.name = name;
            Regions = new ObservableCollection<RegionSeenItem>(
                new[] { SpectRegions.Waiting, SpectRegions.Camera, SpectRegions.Stress, SpectRegions.Toilet }
                    .Select(r => new RegionSeenItem(r)));
            PreExisting = new ObservableCollection<LayerItem>();
            Watch(Regions, RaiseChanged);
            Watch(PreExisting, RaiseChanged);
        }

        [ObservableProperty] private string name;
        [ObservableProperty] private string material = "Lead";
        [ObservableProperty] private double occupancy = 1.0;
        [ObservableProperty] private bool isControlled;
        [ObservableProperty] private double pWeekMSv = 0.02;
        [ObservableProperty] private bool hourlyCheck = true;

        public ObservableCollection<RegionSeenItem> Regions { get; }
        public ObservableCollection<LayerItem> PreExisting { get; }

        // Results
        [ObservableProperty] private string summary = "";
        [ObservableProperty] private string requiredText = "";
        [ObservableProperty] private string governing = "";
        [ObservableProperty] private string weeklyHourlyText = "";
        [ObservableProperty] private string error = "";
        public ObservableCollection<NuclideBreakdownRow> Breakdown { get; } = new();
        public ObservableCollection<string> Warnings { get; } = new();
        public ObservableCollection<KeyValuePair<string, string>> Trace { get; } = new();

        protected override bool IsInput(string? propertyName) =>
            propertyName is not (nameof(Summary) or nameof(RequiredText) or nameof(Governing)
                or nameof(WeeklyHourlyText) or nameof(Error));

        partial void OnIsControlledChanged(bool value)
        {
            PWeekMSv = value ? 0.1 : 0.02;
            HourlyCheck = !value;
        }

        public void SetResult(NuclearBarrierResult r)
        {
            Error = "";
            RequiredText = double.IsInfinity(r.RequiredThickness_mm)
                ? "Not achievable"
                : $"{r.RequiredThickness_mm.ToString("0.###", CultureInfo.InvariantCulture)} mm {Material}";
            Governing = r.Governing;
            WeeklyHourlyText = $"Weekly: {Fmt(r.ThicknessForWeekly_mm)} mm   Hourly: {Fmt(r.ThicknessForHourly_mm)} mm";
            Summary = $"{RequiredText} ({r.Governing})";

            Breakdown.Clear();
            foreach (var c in r.Contributions)
                Breakdown.Add(new NuclideBreakdownRow
                {
                    Nuclide = c.NuclideId,
                    UnshieldedWeek = c.Unshielded_week_mSv.ToString("G4", CultureInfo.InvariantCulture),
                    UnshieldedHour = c.Unshielded_hour_mSv.ToString("G4", CultureInfo.InvariantCulture),
                    PreTransmission = c.PreExistingTransmission.ToString("G4", CultureInfo.InvariantCulture),
                    ShieldedWeek = c.Shielded_week_mSv.ToString("G4", CultureInfo.InvariantCulture),
                    ShieldedHour = c.Shielded_hour_mSv.ToString("G4", CultureInfo.InvariantCulture)
                });
            Warnings.Clear();
            foreach (var w in r.Warnings) Warnings.Add(w);
            Trace.Clear();
            foreach (var t in r.Trace) Trace.Add(t);
        }

        public void SetError(string message)
        {
            Error = message;
            Summary = "Error";
            RequiredText = "";
            Governing = "";
            WeeklyHourlyText = "";
            Breakdown.Clear();
            Warnings.Clear();
            Trace.Clear();
        }

        private static string Fmt(double v) =>
            double.IsInfinity(v) ? "n/a" : v.ToString("0.###", CultureInfo.InvariantCulture);
    }

    public partial class SpectSuiteViewModel : ObservableObject
    {
        private readonly NuclideLibrary _lib = NuclideLibrary.Default;
        private bool _suspend;

        public static IReadOnlyList<string> RegionNames { get; } =
            new[] { SpectRegions.Waiting, SpectRegions.Camera, SpectRegions.Stress, SpectRegions.Toilet };

        public static IReadOnlyList<string> NuclideNames { get; } =
            NuclideLibrary.Default.Nuclides.Keys.OrderBy(k => k).ToList();

        public static IReadOnlyList<string> MaterialNames { get; } =
            NuclideLibrary.Default.Nuclides.Values
                .SelectMany(n => n.Materials.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k).ToList();

        public IReadOnlyList<string> PresetProtocolNames => SpectProtocols.All;

        public ObservableCollection<ProtocolItem> Protocols { get; } = new();
        public ObservableCollection<BarrierItem> Barriers { get; } = new();

        [ObservableProperty] private string? presetToAdd = SpectProtocols.All.FirstOrDefault();
        [ObservableProperty] private ProtocolItem? selectedProtocol;
        [ObservableProperty] private BarrierItem? selectedBarrier;

        public SpectSuiteViewModel()
        {
            SpectItem.Watch(Protocols, Recalculate);
            SpectItem.Watch(Barriers, Recalculate);

            _suspend = true;
            AddProtocol();
            var b = new BarrierItem("Barrier 1");
            Barriers.Add(b);
            b.Regions[0].Sees = true;
            _suspend = false;

            SelectedProtocol = Protocols.FirstOrDefault();
            SelectedBarrier = Barriers.FirstOrDefault();
            Recalculate();
        }

        [RelayCommand]
        private void AddProtocol()
        {
            if (string.IsNullOrEmpty(PresetToAdd)) return;
            var item = new ProtocolItem(SpectProtocols.Create(PresetToAdd), 10);
            Protocols.Add(item);
            SelectedProtocol = item;
        }

        [RelayCommand]
        private void RemoveProtocol()
        {
            if (SelectedProtocol == null) return;
            int idx = Protocols.IndexOf(SelectedProtocol);
            Protocols.Remove(SelectedProtocol);
            SelectedProtocol = Protocols.Count == 0 ? null : Protocols[Math.Min(idx, Protocols.Count - 1)];
        }

        [RelayCommand]
        private void AddInjection() => SelectedProtocol?.Injections.Add(new InjectionItem());

        [RelayCommand]
        private void AddSegment() => SelectedProtocol?.Segments.Add(new SegmentItem());

        [RelayCommand]
        private void AddBarrier()
        {
            var b = new BarrierItem($"Barrier {Barriers.Count + 1}");
            Barriers.Add(b);
            SelectedBarrier = b;
        }

        [RelayCommand]
        private void RemoveBarrier()
        {
            if (SelectedBarrier == null) return;
            int idx = Barriers.IndexOf(SelectedBarrier);
            Barriers.Remove(SelectedBarrier);
            SelectedBarrier = Barriers.Count == 0 ? null : Barriers[Math.Min(idx, Barriers.Count - 1)];
        }

        [RelayCommand]
        private void AddLayer() => SelectedBarrier?.PreExisting.Add(new LayerItem());

        [RelayCommand]
        private void RemoveLayer(LayerItem? layer)
        {
            if (layer != null) SelectedBarrier?.PreExisting.Remove(layer);
        }

        [RelayCommand]
        private void RemoveInjection(InjectionItem? item)
        {
            if (item != null) SelectedProtocol?.Injections.Remove(item);
        }

        [RelayCommand]
        private void RemoveSegment(SegmentItem? item)
        {
            if (item != null) SelectedProtocol?.Segments.Remove(item);
        }

        private void Recalculate()
        {
            if (_suspend) return;
            var workloads = Protocols.Select(p => new ProtocolWorkload(p.ToTimeline(), p.PatientsPerWeek)).ToList();

            foreach (var b in Barriers)
            {
                try
                {
                    var req = new NuclearBarrierRequest
                    {
                        BarrierId = b.Name,
                        Occupancy = b.Occupancy,
                        P_week_mSv = b.PWeekMSv,
                        P_hour_mSv = b.HourlyCheck ? 0.02 : 0,
                        Material = b.Material
                    };
                    req.Workloads.AddRange(workloads);
                    req.Regions.AddRange(b.Regions.Where(r => r.Sees)
                        .Select(r => new RegionExposure(r.Region, r.DistanceM, r.MaxSimultaneousPatients)));
                    req.PreExisting.AddRange(b.PreExisting.Where(l => l.ThicknessMm > 0)
                        .Select(l => new ShieldLayer(l.Material, l.ThicknessMm)));

                    b.SetResult(NuclearBarrierSolver.Solve(req, _lib));
                }
                catch (Exception ex)
                {
                    b.SetError(ex.Message);
                }
            }
        }
    }
}
