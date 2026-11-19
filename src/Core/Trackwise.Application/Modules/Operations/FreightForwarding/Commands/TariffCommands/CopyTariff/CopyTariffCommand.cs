using Trackwise.Application.Abstractions;
using Trackwise.Shared.Models;

namespace Trackwise.Application.Modules.Operations.FreightForwarding.Commands.TariffCommands.CopyTariff;

public class CopyTariffCommand : ICommand<Result<Guid>>
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
}
