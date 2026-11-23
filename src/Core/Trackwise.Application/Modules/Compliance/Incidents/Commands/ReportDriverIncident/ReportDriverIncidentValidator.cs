using FluentValidation;

namespace Trackwise.Application.Modules.Compliance.Incidents.Commands;

internal sealed class ReportDriverIncidentValidator : AbstractValidator<ReportDriverIncidentCommand>
{
    public ReportDriverIncidentValidator()
    {
        RuleFor(i => i.Kind).IsInEnum();
        RuleFor(i => i.Note).MaximumLength(2000);
        RuleFor(i => i.Latitude).InclusiveBetween(-90, 90).When(i => i.Latitude.HasValue);
        RuleFor(i => i.Longitude).InclusiveBetween(-180, 180).When(i => i.Longitude.HasValue);
    }
}
