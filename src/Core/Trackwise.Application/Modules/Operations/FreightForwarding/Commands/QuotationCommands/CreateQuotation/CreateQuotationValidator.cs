using FluentValidation;
using Trackwise.Domain.Primitives.Enums;
using Trackwise.Domain.Primitives.Enums.FreightForwarding;
using Trackwise.Shared.Models;

namespace Trackwise.Application.Modules.Operations.FreightForwarding.Commands.QuotationCommands.CreateQuotation;

public class CreateQuotationValidator : AbstractValidator<CreateQuotationCommand>
{
    private static bool IsLaneMode(FreightMode mode) =>
    mode is FreightMode.Reefer or FreightMode.Trucking;
    public CreateQuotationValidator()
    {
        RuleFor(x => x.CustomerId)
            .NotEmpty()
            .WithMessage("Customer is required.");

        RuleFor(x => x.BranchId)
            .NotEmpty()
            .WithMessage("Branch is required.");

        RuleFor(x => x.Currency)
            .NotEmpty()
            .Length(3)
            .WithMessage("Currency must be a valid 3-letter ISO code.");

        RuleFor(x => x.Mode).IsInEnum();
        RuleFor(x => x.FreightMode).IsInEnum();
        RuleFor(x => x.ShipmentType).IsInEnum();
        RuleFor(x => x.Incoterm).IsInEnum();

        When(x => IsLaneMode(x.FreightMode), () =>
        {
            RuleFor(x => x.Lane)
                .NotNull()
                .WithMessage("Lane routing information is required for Reefer and Trucking freight modes.")
                .SetValidator(new LaneTransitDtoValidator()!);

            // Ports are not applicable to lane modes
            RuleFor(x => x.PolId)
                .Null()
                .WithMessage("POL is not applicable to Reefer and Trucking freight modes.");
            RuleFor(x => x.PodId)
                .Null()
                .WithMessage("POD is not applicable to Reefer and Trucking freight modes.");
        });

        When(x => x.FreightMode == FreightMode.Reefer || x.FreightMode == FreightMode.Trucking, () =>
        {
            RuleFor(x => x.Lane)
                .NotNull()
                .WithMessage("Lane routing information is required for Reefer and Trucking freight modes.")
                .SetValidator(new LaneTransitDtoValidator());
        });

        When(x => !IsLaneMode(x.FreightMode), () =>
        {
            RuleFor(x => x.PolId)
                .NotEmpty()
                .WithMessage("POL (Port of Loading) is required for Domestic/Import/Export/Triangular freight modes.");

            RuleFor(x => x.PodId)
                .NotEmpty()
                .WithMessage("POD (Port of Discharge) is required for Domestic/Import/Export/Triangular freight modes.");

            RuleFor(x => x.PodId)
                .NotEqual(x => x.PolId)
                .When(x => x.PolId.HasValue && x.PodId.HasValue)
                .WithMessage("Port of Loading and Port of Discharge must be different.");

            RuleFor(x => x.Lane)
                .Null()
                .WithMessage("A route lane is not applicable to Domestic/Import/Export/Triangular freight modes.");
        });

        When(x => x.Cargo != null && !IsCargoEmpty(x.Cargo), () =>
        {
            RuleFor(x => x.Cargo!)
                .SetValidator(new CargoSpecificationDtoValidator());
        });

        When(x => x.Cargo?.RequiresActiveRefrigeration == true, () =>
        {
            RuleFor(x => x.TemperatureSpec)
                .NotNull()
                .WithMessage("Temperature specifications are required when active refrigeration is selected.");
        });

        // Temperature control is Reefer-only
        When(x => x.FreightMode == FreightMode.Reefer, () =>
        {
            RuleFor(x => x.TemperatureSpec)
                .NotNull()
                .WithMessage("Temperature specifications are required for Reefer freight mode.");
        });

        When(x => x.FreightMode != FreightMode.Reefer, () =>
        {
            RuleFor(x => x.TemperatureSpec)
                .Null()
                .WithMessage("Temperature specifications are only applicable to Reefer freight mode.");
        });
    }

    private static bool IsCargoEmpty(CargoSpecificationDto cargo)
    {
        return string.IsNullOrWhiteSpace(cargo.Commodity)
            && cargo.TotalWeightKg <= 0
            && cargo.PalletCount <= 0
            && cargo.DeclaredCargoValueAmount <= 0;
    }
}
