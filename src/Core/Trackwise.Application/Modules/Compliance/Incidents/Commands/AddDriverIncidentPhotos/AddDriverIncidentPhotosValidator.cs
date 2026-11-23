using FluentValidation;

namespace Trackwise.Application.Modules.Compliance.Incidents.Commands;

internal sealed class AddDriverIncidentPhotosValidator : AbstractValidator<AddDriverIncidentPhotosCommand>
{
    public AddDriverIncidentPhotosValidator()
    {
        RuleFor(i => i.Files).NotEmpty().WithMessage("Take at least one photo.");
        RuleForEach(i => i.Files)
            .Must(f => f.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Only photos can be sent from the app.");
    }
}
