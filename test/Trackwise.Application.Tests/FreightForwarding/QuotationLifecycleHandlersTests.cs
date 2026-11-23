using MockQueryable;
using Microsoft.Extensions.Options;
using Trackwise.Application.Tests.TestKit;
using NSubstitute;
using Trackwise.Application.Modules.Operations.FreightForwarding.Commands.QuotationCommands.AcceptQuotation;
using Trackwise.Application.Modules.Operations.FreightForwarding.Commands.QuotationCommands.AddQuotationLines;
using Trackwise.Application.Modules.Operations.FreightForwarding.Commands.QuotationCommands.ApproveQuotation;
using Trackwise.Application.Modules.Operations.FreightForwarding.Commands.QuotationCommands.ExpireQuotation;
using Trackwise.Application.Modules.Operations.FreightForwarding.Commands.QuotationCommands.IssueQuotation;
using Trackwise.Application.Modules.Operations.FreightForwarding.Commands.QuotationCommands.RejectQuotation;
using Trackwise.Application.Modules.Operations.FreightForwarding.Commands.QuotationCommands.SubmitQuotation;
using Trackwise.Domain.Entities;
using Trackwise.Domain.Options;
using Trackwise.Domain.Persistence;
using Trackwise.Domain.Primitives.Enums;
using Trackwise.Domain.Primitives.Enums.FreightForwarding;
using Trackwise.Domain.Primitives.ValueObjects;
using Xunit;
using Trackwise.Application.Abstractions.CurrentUser;
using Trackwise.Application.Abstractions.Email;
using Trackwise.Application.Abstractions.Email.Models;

namespace Trackwise.Application.Tests.FreightForwarding;

public class QuotationLifecycleHandlersTests
{
    private readonly ITenantUnitOfWork _tenantUow = Substitute.For<ITenantUnitOfWork>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly IEmailTemplateService _emailTemplateService = Substitute.For<IEmailTemplateService>();
    private readonly ICurrentUserService currentUserService = Substitute.For<ICurrentUserService>();
    private readonly IOptions<CustomerPortalOptions> _portalOptions = Options.Create(new CustomerPortalOptions());
    private readonly ITenantRepository<Quotation, Guid> _quotationRepo = Substitute.For<ITenantRepository<Quotation, Guid>>();
    private readonly ITenantRepository<QuotationLine, Guid> _quotationLineRepo = Substitute.For<ITenantRepository<QuotationLine, Guid>>();
    private readonly ITenantRepository<TariffRate, Guid> _tariffRateRepo = Substitute.For<ITenantRepository<TariffRate, Guid>>();
    private readonly ITenantRepository<Customer, Guid> _customerRepo = Substitute.For<ITenantRepository<Customer, Guid>>();
    private readonly ITenantRepository<QuotationLink, Guid> _quotationLinkRepo = Substitute.For<ITenantRepository<QuotationLink, Guid>>();

    public QuotationLifecycleHandlersTests()
    {
        _tenantUow.Repository<Quotation>().Returns(_quotationRepo);
        _tenantUow.Repository<QuotationLine>().Returns(_quotationLineRepo);
        _tenantUow.Repository<TariffRate>().Returns(_tariffRateRepo);
        _tenantUow.Repository<Customer>().Returns(_customerRepo);
        _tenantUow.Repository<QuotationLink>().Returns(_quotationLinkRepo);
    }

    #region AddQuotationLines

    [Fact]
    public async Task AddQuotationLines_QuotationNotFound_Fails()
    {
        var id = Guid.NewGuid();
        _quotationRepo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((Quotation?)null);

        var result = await new AddQuotationLinesHandler(_tenantUow).Handle(
            BatchCommand(id, LineFor(Guid.NewGuid())), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("not found", result.Error);
        await _tenantUow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddQuotationLines_NonDraftQuotation_Fails()
    {
        var quotation = CreateTestQuotation();
        TestQuotations.AddBaseLine(quotation);
        quotation.Approve("manager@test.com");
        _quotationRepo.GetByIdAsync(quotation.Id, Arg.Any<CancellationToken>()).Returns(quotation);

        var result = await new AddQuotationLinesHandler(_tenantUow).Handle(
            BatchCommand(quotation.Id, LineFor(Guid.NewGuid())), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("Draft", result.Error);
        await _tenantUow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddQuotationLines_UnknownTariffRate_FailsWithLineNumber()
    {
        var quotation = CreateTestQuotation();
        _quotationRepo.GetByIdAsync(quotation.Id, Arg.Any<CancellationToken>()).Returns(quotation);
        SetupRates(); // none exist

        var result = await new AddQuotationLinesHandler(_tenantUow).Handle(
            BatchCommand(quotation.Id, LineFor(Guid.NewGuid())), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("Line 1", result.Error);
        Assert.Contains("not found", result.Error);
    }

    [Fact]
    public async Task AddQuotationLines_InactiveTariff_Fails()
    {
        var quotation = CreateTestQuotation();
        _quotationRepo.GetByIdAsync(quotation.Id, Arg.Any<CancellationToken>()).Returns(quotation);

        var tariff = CreateTariff("Expired Road Tariff", isActive: false);
        var rate = CreateRate(tariff, "FRT", ChargeCategory.Base);
        SetupRates(rate);

        var result = await new AddQuotationLinesHandler(_tenantUow).Handle(
            BatchCommand(quotation.Id, LineFor(rate.Id)), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("not active", result.Error);
        Assert.Contains("Expired Road Tariff", result.Error);
    }

    [Fact]
    public async Task AddQuotationLines_TariffModeDiffersFromQuotation_Fails()
    {
        var quotation = CreateTestQuotation(); // Trucking
        _quotationRepo.GetByIdAsync(quotation.Id, Arg.Any<CancellationToken>()).Returns(quotation);

        var tariff = CreateTariff("Import Tariff", mode: FreightMode.Import);
        var rate = CreateRate(tariff, "FRT", ChargeCategory.Base);
        SetupRates(rate);

        var result = await new AddQuotationLinesHandler(_tenantUow).Handle(
            BatchCommand(quotation.Id, LineFor(rate.Id)), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("Import Tariff", result.Error);
    }

    [Fact]
    public async Task AddQuotationLines_PercentageRateWithCustomBaseAndNoBaseAmount_FailsWithLineNumber()
    {
        var quotation = CreateTestQuotation();
        _quotationRepo.GetByIdAsync(quotation.Id, Arg.Any<CancellationToken>()).Returns(quotation);

        var tariff = CreateTariff("Road Tariff");
        var rate = CreateRate(tariff, "FSC", ChargeCategory.Base, PricingModel.Percentage, percentageBase: "LINEHAUL");
        SetupRates(rate);

        var result = await new AddQuotationLinesHandler(_tenantUow).Handle(
            BatchCommand(quotation.Id, LineFor(rate.Id)), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("Line 1", result.Error);
        Assert.Contains("Provide a BaseAmount", result.Error);
    }

    [Fact]
    public async Task AddQuotationLines_RatesFromMultipleTariffs_AddsAllLinesAndSavesOnce()
    {
        var quotation = CreateTestQuotation();
        _quotationRepo.GetByIdAsync(quotation.Id, Arg.Any<CancellationToken>()).Returns(quotation);

        var roadTariff = CreateTariff("Road Tariff");
        var accessorialTariff = CreateTariff("Accessorials 2026");
        var linehaul = CreateRate(roadTariff, "FRT", ChargeCategory.Base);
        var fuel = CreateRate(roadTariff, "FSC", ChargeCategory.Base);
        var liftgate = CreateRate(accessorialTariff, "LFT", ChargeCategory.Base);
        SetupRates(linehaul, fuel, liftgate);

        var result = await new AddQuotationLinesHandler(_tenantUow).Handle(
            BatchCommand(quotation.Id, LineFor(linehaul.Id), LineFor(fuel.Id), LineFor(liftgate.Id)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Count);
        Assert.Equal(3, quotation.Lines.Count);
        Assert.Equal(quotation.Lines.Select(l => l.Id), result.Value);

        await _quotationLineRepo.Received(3).AddAsync(Arg.Any<QuotationLine>(), Arg.Any<CancellationToken>());
        await _tenantUow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddQuotationLines_SameRateTwice_AddsBothLines()
    {
        // The form lets users duplicate a charge (e.g. the same fee under two service types).
        var quotation = CreateTestQuotation();
        _quotationRepo.GetByIdAsync(quotation.Id, Arg.Any<CancellationToken>()).Returns(quotation);

        var tariff = CreateTariff("Road Tariff");
        var rate = CreateRate(tariff, "FRT", ChargeCategory.Base);
        SetupRates(rate);

        var result = await new AddQuotationLinesHandler(_tenantUow).Handle(
            BatchCommand(
                quotation.Id,
                LineFor(rate.Id, OperationalServiceType.LineHaul),
                LineFor(rate.Id, OperationalServiceType.Standard)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, quotation.Lines.Count);
    }

    [Fact]
    public async Task AddQuotationLines_LaterLineInvalid_PersistsNothing()
    {
        // All-or-nothing: line 1 is fine, line 2 fails validation, so line 1 must not be saved either.
        var quotation = CreateTestQuotation();
        _quotationRepo.GetByIdAsync(quotation.Id, Arg.Any<CancellationToken>()).Returns(quotation);

        var tariff = CreateTariff("Road Tariff");
        var good = CreateRate(tariff, "FRT", ChargeCategory.Base);
        var bad = CreateRate(tariff, "FSC", ChargeCategory.Base, PricingModel.Percentage, percentageBase: "LINEHAUL");
        SetupRates(good, bad);

        var result = await new AddQuotationLinesHandler(_tenantUow).Handle(
            BatchCommand(quotation.Id, LineFor(good.Id), LineFor(bad.Id)), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("Line 2", result.Error);
        Assert.Empty(quotation.Lines);
        await _quotationLineRepo.DidNotReceive().AddAsync(Arg.Any<QuotationLine>(), Arg.Any<CancellationToken>());
        await _tenantUow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AddQuotationLinesValidator_EmptyLines_IsInvalid()
    {
        var result = new AddQuotationLinesValidator().Validate(BatchCommand(Guid.NewGuid()));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void AddQuotationLinesValidator_NonPositiveQuantity_IsInvalid()
    {
        var line = LineFor(Guid.NewGuid());
        line.Quantity = 0;

        var result = new AddQuotationLinesValidator().Validate(BatchCommand(Guid.NewGuid(), line));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void AddQuotationLinesValidator_ValidBatch_IsValid()
    {
        var result = new AddQuotationLinesValidator().Validate(
            BatchCommand(Guid.NewGuid(), LineFor(Guid.NewGuid()), LineFor(Guid.NewGuid())));

        Assert.True(result.IsValid);
    }

    #endregion

    #region Submit


    [Fact]
    public async Task Submit_QuotationWithoutLines_Fails()
    {
        var quotation = CreateTestQuotation(); // 0 lines
        _quotationRepo.GetByIdAsync(quotation.Id, Arg.Any<CancellationToken>()).Returns(quotation);

        var sut = new SubmitQuotationHandler(_tenantUow, currentUserService);
        var result = await sut.Handle(new SubmitQuotationCommand { Id = quotation.Id }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("without priced line items", result.Error);
    }

    #endregion

    #region Approve

    [Fact]
    public async Task Approve_PendingApprovalQuotation_SetsApprovedByAndTimestamp()
    {
        var quotation = CreateTestQuotation(declaredValue: 31_000_000m);
        AddPricedLine(quotation);
        quotation.Submit("System"); // Moves to PendingApproval
        _quotationRepo.GetByIdAsync(quotation.Id, Arg.Any<CancellationToken>()).Returns(quotation);

        currentUserService.GetUserName().Returns("director@test.com");

        var sut = new ApproveQuotationHandler(_tenantUow, currentUserService);
        var result = await sut.Handle(
            new ApproveQuotationCommand { Id = quotation.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(QuotationStatus.Approved, quotation.Status);
        Assert.Equal("director@test.com", quotation.ApprovedBy);
        Assert.NotNull(quotation.ApprovedAt);
    }

    #endregion

    #region Issue

    [Fact]
    public async Task Issue_ApprovedQuotation_SnapshotsPricesAndSetsExpiration()
    {
        var quotation = CreateTestQuotation();
        AddPricedLine(quotation);
        quotation.Approve("manager@test.com");

        _quotationRepo.GetByIdAsync(quotation.Id, Arg.Any<CancellationToken>()).Returns(quotation);
        _quotationRepo.Query().Returns(new[] { quotation }.BuildMock());

        var sut = new IssueQuotationHandler(_tenantUow, _emailSender, _emailTemplateService, currentUserService, _portalOptions);
        var result = await sut.Handle(
            new IssueQuotationCommand { Id = quotation.Id, ValidityDays = 10 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(QuotationStatus.Issued, quotation.Status);
        Assert.NotNull(quotation.ValidUntil);
        Assert.True(quotation.ValidUntil.Value > DateTimeOffset.UtcNow.AddDays(9));

        // Verifies financial snapshotting took place
        var line = quotation.Lines.First();
        Assert.NotNull(line.SnapshotBuyingAmount);
        Assert.NotNull(line.SnapshotSellingAmount);
    }

    [Fact]
    public async Task Issue_DraftQuotation_Fails()
    {
        var quotation = CreateTestQuotation();
        _quotationRepo.GetByIdAsync(quotation.Id, Arg.Any<CancellationToken>()).Returns(quotation);
        _quotationRepo.Query().Returns(new[] { quotation }.BuildMock());

        var sut = new IssueQuotationHandler(_tenantUow, _emailSender, _emailTemplateService, currentUserService, _portalOptions);
        var result = await sut.Handle(new IssueQuotationCommand { Id = quotation.Id }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("Only Approved quotations can be issued", result.Error);
    }

    [Fact]
    public async Task Issue_WithoutCustomerEmail_DoesNotSendQuotationEmail()
    {
        var quotation = CreateTestQuotation();
        AddPricedLine(quotation);
        quotation.Approve("manager@test.com");

        _quotationRepo.GetByIdAsync(quotation.Id, Arg.Any<CancellationToken>()).Returns(quotation);
        _quotationRepo.Query().Returns(new[] { quotation }.BuildMock());

        var sut = new IssueQuotationHandler(_tenantUow, _emailSender, _emailTemplateService, currentUserService, _portalOptions);
        var result = await sut.Handle(new IssueQuotationCommand { Id = quotation.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        await _emailSender.DidNotReceive().SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
        await _quotationLinkRepo.DidNotReceive().AddAsync(Arg.Any<QuotationLink>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Issue_WithCustomerEmail_GeneratesTokenizedQuotationLinkAndSendsEmail()
    {
        var quotation = CreateTestQuotation();
        AddPricedLine(quotation);
        quotation.Approve("manager@test.com");

        var customer = new Customer { Id = quotation.CustomerId, Name = "Acme Shipping" };
        _quotationRepo.GetByIdAsync(quotation.Id, Arg.Any<CancellationToken>()).Returns(quotation);
        _quotationRepo.Query().Returns(new[] { quotation }.BuildMock());
        _customerRepo.GetByIdAsync(quotation.CustomerId, Arg.Any<CancellationToken>()).Returns(customer);
        _tenantUow.GetCurrentTenant().Returns(new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Acme Logistics",
            CompanyName = "Acme Logistics",
            CompanyAddress = new Address
            {
                StreetAddress = "123 Test Street",
                CityOrMunicipality = "Makati",
                Province = "Metro Manila",
                Region = "NCR",
                Zipcode = "1200",
                Country = "PH"
            },
            ConnectionString = "Host=localhost;Database=test",
            BillingEmail = "billing@acme.com"
        });
        QuotationLink? addedLink = null;
        _quotationLinkRepo.AddAsync(Arg.Do<QuotationLink>(l => addedLink = l), Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(Task.CompletedTask);

        _emailTemplateService
            .RenderAsync("QuotationIssued", Arg.Any<QuotationIssuedEmailModel>())
            .Returns("Dear Customer, <a href=\"https://app/track\">View Quotation</a>");
        _emailSender.SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(true);

        var sut = new IssueQuotationHandler(_tenantUow, _emailSender, _emailTemplateService, currentUserService, _portalOptions);
        var result = await sut.Handle(
            new IssueQuotationCommand { Id = quotation.Id, ValidityDays = 10, CustomerEmail = "shipper@acme.com" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        await _emailSender.Received(1).SendEmailAsync("shipper@acme.com", Arg.Any<string>(), Arg.Any<string>());
        await _quotationLinkRepo.Received(1).AddAsync(Arg.Any<QuotationLink>(), Arg.Any<CancellationToken>());

        await _quotationLinkRepo.Received(1).AddAsync(Arg.Any<QuotationLink>(), Arg.Any<CancellationToken>());
        Assert.NotNull(addedLink);
        Assert.False(string.IsNullOrWhiteSpace(addedLink.Token));
        Assert.Equal(quotation.Id, addedLink.QuotationId);
        Assert.Equal(quotation.ValidUntil!.Value.UtcDateTime, addedLink.ExpiresAt);
    }

    #endregion

    #region Accept

    [Fact]
    public async Task Accept_IssuedActiveQuotation_TransitionsToAccepted()
    {
        var quotation = CreateTestQuotation();
        AddPricedLine(quotation);
        quotation.Approve("manager@test.com");
        quotation.Issue(TimeSpan.FromDays(7), "System"); // Valid for 7 days
        _quotationRepo.GetByIdAsync(quotation.Id, Arg.Any<CancellationToken>()).Returns(quotation);

        var sut = new AcceptQuotationHandler(_tenantUow, currentUserService);
        var result = await sut.Handle(new AcceptQuotationCommand { Id = quotation.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(QuotationStatus.Accepted, quotation.Status);
        Assert.NotNull(quotation.AcceptedAt);
    }

    [Fact]
    public async Task Accept_UnissuedQuotation_Fails()
    {
        var quotation = CreateTestQuotation();
        _quotationRepo.GetByIdAsync(quotation.Id, Arg.Any<CancellationToken>()).Returns(quotation);

        var sut = new AcceptQuotationHandler(_tenantUow, currentUserService);
        var result = await sut.Handle(new AcceptQuotationCommand { Id = quotation.Id }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("Only Issued quotations can be accepted", result.Error);
    }

    #endregion

    #region Expire

    [Fact]
    public async Task Expire_PastExpirationDate_TransitionsToExpired()
    {
        var quotation = CreateTestQuotation();
        AddPricedLine(quotation);
        quotation.Approve("manager@test.com");
        quotation.Issue(TimeSpan.FromDays(-1), "System"); // Already in the past
        _quotationRepo.GetByIdAsync(quotation.Id, Arg.Any<CancellationToken>()).Returns(quotation);

        var sut = new ExpireQuotationHandler(_tenantUow, currentUserService);
        var result = await sut.Handle(new ExpireQuotationCommand { Id = quotation.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(QuotationStatus.Expired, quotation.Status);
    }

    [Fact]
    public async Task Expire_BeforeExpirationDate_Fails()
    {
        var quotation = CreateTestQuotation();
        AddPricedLine(quotation);
        quotation.Approve("manager@test.com");
        quotation.Issue(TimeSpan.FromDays(14), "System"); // Future expiration
        _quotationRepo.GetByIdAsync(quotation.Id, Arg.Any<CancellationToken>()).Returns(quotation);

        var sut = new ExpireQuotationHandler(_tenantUow, currentUserService);
        var result = await sut.Handle(new ExpireQuotationCommand { Id = quotation.Id }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("has not yet reached its expiration date", result.Error);
    }

    #endregion

    #region Reject

    //[Fact]
    //public async Task Reject_IssuedQuotation_TransitionsToRejectedWithReason()
    //{
    //    var quotation = CreateTestQuotation();
    //    AddPricedLine(quotation);
    //    quotation.Approve("manager@test.com");
    //    quotation.Issue(TimeSpan.FromDays(7), "System");
    //    _quotationRepo.GetByIdAsync(quotation.Id, Arg.Any<CancellationToken>()).Returns(quotation);

    //    var sut = new RejectQuotationHandler(_tenantUow, currentUserService);
    //    var result = await sut.Handle(
    //        new RejectQuotationCommand { Id = quotation.Id, RejectedBy = "shipper@client.com", Reason = "Competitor offered lower rate." },
    //        CancellationToken.None);

    //    Assert.True(result.IsSuccess);
    //    Assert.Equal(QuotationStatus.Rejected, quotation.Status);
    //    //Assert.Equal("shipper@client.com", quotation.RejectedBy);
    //    Assert.Equal("Competitor offered lower rate.", quotation.RejectionReason);
    //}

    #endregion

    #region Test Helpers

    private static Quotation CreateTestQuotation(decimal declaredValue = 25000m)
    {
        var lane = LaneTransit.PointToPoint(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddDays(1),
            DateTimeOffset.UtcNow.AddDays(2),
            120m);

        var cargo = CargoSpecification.Create(
            commodity: "Standard Palletized Freight",
            totalWeightKg: 4000m,
            palletCount: 8,
            declaredCargoValue: new Money { Amount = declaredValue, Currency = "USD" },
            requiresActiveRefrigeration: false);

        return Quotation.Create(
            customerId: Guid.NewGuid(),
            branchId: Guid.NewGuid(),
            referenceNumber: "QT-TEST-2026",
            currency: "USD",
            mode: TransportMode.Road,
            freightMode: FreightMode.Trucking,
            shipmentType: ShipmentType.FTL,
            incoterm: Incoterm.DAP,
            lane: lane,
            cargoSpec: cargo,
            declaredCargoValuePhp: declaredValue);
    }

    private static void AddPricedLine(Quotation quotation)
    {
        var chargeCode = ChargeCode.Create("FRT", "Freight", ChargeCategory.Base, [FreightMode.Trucking], "km");
        chargeCode.Currency = "USD";

        var tariffRate = new TariffRate
        {
            ChargeCode = chargeCode,
            PricingModel = PricingModel.PerUnit,
            BaseAmount = 10m,
            Unit = "km"
        };

        var line = quotation.AddLine(
            tariffRateId: tariffRate.Id,
            category: ChargeCategory.Base,
            serviceType: OperationalServiceType.LineHaul,
            quantity: 100m,
            markupType: MarkupType.Percentage,
            markupValue: 10m,
            fxRate: null,
            notes: null);

        // Inject navigation property to support line.Price()
        typeof(QuotationLine).GetProperty(nameof(QuotationLine.TariffRate))?.SetValue(line, tariffRate);
    }

    // --- Batch AddQuotationLines helpers ---------------------------------

    /// <summary>
    /// Active, currently effective Domestic tariff unless overridden. Built through the domain factory
    /// because Tariff's setters are private.
    /// </summary>
    private static Tariff CreateTariff(
        string name,
        FreightMode mode = FreightMode.Trucking,
        bool isActive = true)
    {
        var tariff = Tariff.Create(
            mode: mode,
            serviceLevel: default,
            name: name,
            currency: "USD",
            originLane: null,
            destinationLane: null,
            truckType: null,
            tonnageClass: null,
            containerTypeId: null,
            carrierId: null,
            effectiveFrom: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            contractRef: null);

        if (!isActive)
            tariff.Deactivate();

        return tariff;
    }

    /// <summary>
    /// Adds a rate through Tariff.AddRate, then sets the ChargeCode and Tariff navigations by reflection,
    /// which is what the handler's Include(...) calls would give it. Charge currency matches the
    /// quotation (USD) so no FX is needed.
    /// </summary>
    private static TariffRate CreateRate(
        Tariff tariff,
        string code,
        ChargeCategory category,
        PricingModel pricingModel = PricingModel.PerUnit,
        string? percentageBase = null)
    {
        var chargeCode = ChargeCode.Create(code, $"{code} charge", category, [FreightMode.Domestic], "km");
        chargeCode.Currency = "USD";

        var isPercentage = pricingModel == PricingModel.Percentage;

        var rate = tariff.AddRate(
            chargeCodeId: chargeCode.Id,
            pricingModel: pricingModel,
            baseAmount: isPercentage ? 0m : 10m,
            unit: "km",
            minimum: null,
            maximum: null,
            freeDays: null,
            rateAfterFreeDays: null,
            percentage: isPercentage ? 5m : null,
            percentageBase: isPercentage ? percentageBase : null,
            breakpointJson: null);

        typeof(TariffRate).GetProperty(nameof(TariffRate.ChargeCode))!.SetValue(rate, chargeCode);
        typeof(TariffRate).GetProperty(nameof(TariffRate.Tariff))!.SetValue(rate, tariff);

        return rate;
    }

    private void SetupRates(params TariffRate[] rates) =>
        _tariffRateRepo.Query().Returns(rates.BuildMock());

    private static AddQuotationLineItem LineFor(
        Guid tariffRateId,
        OperationalServiceType serviceType = OperationalServiceType.Standard) =>
        new()
        {
            TariffRateId = tariffRateId,
            ServiceType = serviceType,
            Quantity = 10m,
            MarkupType = MarkupType.Percentage,
            MarkupValue = 15m
        };

    private static AddQuotationLinesCommand BatchCommand(Guid quotationId, params AddQuotationLineItem[] lines) =>
        new() { QuotationId = quotationId, Lines = [.. lines] };

    #endregion
}
