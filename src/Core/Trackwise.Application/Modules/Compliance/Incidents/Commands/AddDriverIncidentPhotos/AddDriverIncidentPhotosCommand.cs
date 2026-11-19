using Trackwise.Application.Abstractions;
using Trackwise.Shared.Models;

namespace Trackwise.Application.Modules.Compliance.Incidents.Commands;

/// <summary>Photos the crew takes of a case they reported from the Driver App - the damage, the scene, the other vehicle.</summary>
public class AddDriverIncidentPhotosCommand : ICommand<Result>
{
    public Guid IncidentId { get; set; }
    public List<FileUpload> Files { get; set; } = [];
}
