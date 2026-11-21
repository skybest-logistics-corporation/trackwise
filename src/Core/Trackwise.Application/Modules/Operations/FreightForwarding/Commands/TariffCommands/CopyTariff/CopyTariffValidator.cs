using FluentValidation;

namespace Trackwise.Application.Modules.Operations.FreightForwarding.Commands.TariffCommands.CopyTariff;

internal sealed class CopyTariffValidator : AbstractValidator<CopyTariffCommand>
{
    public CopyTariffValidator()
    {
        RuleFor(i => i.Name).NotEmpty().MaximumLength(200);
    }
}
