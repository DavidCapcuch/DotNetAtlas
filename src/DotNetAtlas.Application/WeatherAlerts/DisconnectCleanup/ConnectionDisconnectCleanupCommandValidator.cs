using FluentValidation;

namespace DotNetAtlas.Application.WeatherAlerts.DisconnectCleanup;

public class ConnectionDisconnectCleanupCommandValidator : AbstractValidator<ConnectionDisconnectCleanupCommand>
{
    public ConnectionDisconnectCleanupCommandValidator()
    {
        RuleFor(sfcac => sfcac.ConnectionId)
            .NotEmpty();
    }
}
