using Trackwise.Application.Modules.Compliance.Incidents;
using Trackwise.Application.Modules.Compliance.Incidents.Commands;
using Trackwise.Domain.Entities;
using Trackwise.Domain.Primitives.Enums;
using Trackwise.Domain.Primitives.Enums.Safety;
using Trackwise.Domain.Primitives.ValueObjects;
using Xunit;

namespace Trackwise.Application.Tests.Compliance;

/// <summary>A crew's "Problem" report from the Driver App, turned into a case on their trip.</summary>
public class DriverIncidentReportTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private static readonly Truck OwnTruck = new() { Number = "NGT1267", Name = "NGT1267", Type = TruckType.FreightTruck };
    private static readonly Truck RescueTruck = new() { Number = "NIO4715", Name = "NIO4715", Type = TruckType.FreightTruck };
    private static readonly Employee TripDriver = NewDriver("Salvador");
    private static readonly Employee RescueDriver = NewDriver("Wilson");
    private static readonly Employee Stranger = NewDriver("Judie");
    private static readonly Helper TripHelper = new() { Code = "", Name = "Rayan Baylosis" };
    private static readonly Helper RescueHelper = new() { Code = "", Name = "Aldrin Cruz" };

    private static Employee NewDriver(string name) =>
        new() { Code = name, Email = $"{name}@test.com", FirstName = name, LastName = "Driver" };

    private static Load NewLoad(string name, int drop) => new()
    {
        Name = name,
        Type = LoadType.GeneralFreight,
        Customer = null!,
        OriginAddress = Address("Warehouse"),
        OriginLocation = new GeoPoint(0, 0),
        DestinationAddress = Address(name),
        DestinationLocation = new GeoPoint(drop, drop),
        DeliveryCost = Money.Zero("PHP")
    };

    private static Address Address(string street) => new()
    {
        StreetAddress = street,
        CityOrMunicipality = "Calamba",
        Province = "Laguna",
        Region = "",
        Zipcode = "",
        Country = "PH"
    };

    /// <summary>Loaded and on the road to its two drops, an hour after leaving the warehouse.</summary>
    private static Trip TripUnderWay()
    {
        var trip = Trip.Create("HMI Calamba", OwnTruck, [NewLoad("Drop 1", 1), NewLoad("Drop 2", 2)]);
        trip.AssignCrew(TripDriver, TripHelper, null);
        trip.Dispatch();
        trip.RecordGarageTimes(Now.AddHours(-4), null);
        foreach (var pickup in trip.Stops.Where(s => s.Type == TripStopType.PickUp))
        {
            trip.MarkStopArrived(pickup.Id, arrivedAt: Now.AddHours(-3.5));
            trip.MarkStopDeparted(pickup.Id, new StopDeparture(null, DepartedAt: Now.AddHours(-3)));
        }

        return trip;
    }

    private static void Rescue(Trip trip) =>
        trip.RecordRescue(
            new RescueSpec(
                RescueTruck,
                RescueDriver,
                RescueHelper,
                null,
                trip.Stops.Where(s => s.Type == TripStopType.DropOff).Select(s => s.Id).ToList(),
                Now.AddHours(-1),
                "Clutch gave out",
                "Pansol, Calamba"),
            "office");

    private static ReportDriverIncidentCommand Report(
        IncidentKind kind = IncidentKind.Accident, string? note = null, bool injuries = false, bool drivable = true) =>
        new() { Kind = kind, Note = note, AnyInjuries = injuries, VehicleDrivable = drivable, Latitude = 14.2, Longitude = 121.1 };

    [Fact]
    public void Build_TripDriver_NamesTheTripsTruckAndCrew()
    {
        var trip = TripUnderWay();

        var result = DriverIncidentReport.Build(trip, TripDriver.Id, "Salvador Driver", Report(drivable: false), Now);

        Assert.True(result.IsSuccess);
        var (details, parties) = result.Value;
        Assert.Equal(trip.Id, parties.TripId);
        Assert.Equal(OwnTruck.Id, parties.TruckId);
        Assert.Equal(TripDriver.Id, parties.DriverId);
        Assert.Equal(TripHelper.Id, parties.HelperId);
        Assert.Equal(IncidentKind.Accident, details.Kind);
        Assert.False(details.VehicleDrivable);
        Assert.True(details.VehicleDamaged);
        Assert.Equal(14.2, details.Latitude);
        Assert.Equal(Now, details.OccurredAt);
    }

    [Fact]
    public void Build_RescueDriver_NamesTheRescueTruckAndCrew()
    {
        var trip = TripUnderWay();
        Rescue(trip);

        var result = DriverIncidentReport.Build(trip, RescueDriver.Id, "Wilson Driver", Report(IncidentKind.Breakdown), Now);

        Assert.True(result.IsSuccess);
        var (details, parties) = result.Value;
        Assert.Equal(RescueTruck.Id, parties.TruckId);
        Assert.Equal(RescueDriver.Id, parties.DriverId);
        Assert.Equal(RescueHelper.Id, parties.HelperId);
        Assert.False(details.VehicleDamaged);
    }

    [Fact]
    public void Build_AnotherDriver_Refused()
    {
        var trip = TripUnderWay();

        var result = DriverIncidentReport.Build(trip, Stranger.Id, "Judie Driver", Report(), Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(DriverIncidentReport.NotYourTrip, result.Error);
    }

    [Fact]
    public void Build_TripNotDispatched_Refused()
    {
        var trip = Trip.Create("HMI Calamba", OwnTruck, [NewLoad("Drop 1", 1)]);
        trip.AssignCrew(TripDriver, null, null);

        Assert.False(DriverIncidentReport.Build(trip, TripDriver.Id, "Salvador Driver", Report(), Now).IsSuccess);
    }

    [Fact]
    public void Build_Injuries_IsModerateAndCountsOneUntilTheOfficeKnowsMore()
    {
        var result = DriverIncidentReport.Build(TripUnderWay(), TripDriver.Id, "Salvador Driver", Report(injuries: true), Now);

        var details = result.Value.Details;
        Assert.Equal(IncidentSeverity.Moderate, details.Severity);
        Assert.True(details.AnyInjuries);
        Assert.Equal(1, details.NumberOfInjuries);
    }

    [Fact]
    public void Build_NoInjuries_IsMinor()
    {
        var result = DriverIncidentReport.Build(TripUnderWay(), TripDriver.Id, "Salvador Driver", Report(), Now);

        Assert.Equal(IncidentSeverity.Minor, result.Value.Details.Severity);
        Assert.Null(result.Value.Details.NumberOfInjuries);
    }

    [Fact]
    public void Build_KeepsTheCrewsWordsAndSaysWhoReported()
    {
        var withNote = DriverIncidentReport.Build(
            TripUnderWay(), TripDriver.Id, "Salvador Driver", Report(note: "  Sideswiped by a jeep  "), Now);
        var withoutNote = DriverIncidentReport.Build(TripUnderWay(), TripDriver.Id, "Salvador Driver", Report(), Now);

        Assert.StartsWith("Sideswiped by a jeep\n\n", withNote.Value.Details.Summary);
        Assert.EndsWith("Reported from the Driver App by Salvador Driver.", withNote.Value.Details.Summary);
        Assert.Equal("Reported from the Driver App by Salvador Driver.", withoutNote.Value.Details.Summary);
    }

    [Fact]
    public void Build_ResultRecordsAsAnOpenIncident()
    {
        var (details, parties) = DriverIncidentReport.Build(
            TripUnderWay(), TripDriver.Id, "Salvador Driver", Report(injuries: true), Now).Value;

        var incident = Domain.Entities.Safety.Incident.Record("CCL-CASENO.090", details, parties, Now);

        Assert.Equal(IncidentStatus.Open, incident.Status);
        Assert.Equal(OwnTruck.Id, incident.TruckId);
    }
}
