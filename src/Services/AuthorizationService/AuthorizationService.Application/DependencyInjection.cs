using AuthorizationService.Application.Features.EvaluateAuthorizationDecision;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace AuthorizationService.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<EvaluateAuthorizationDecisionCommandValidator>();
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<EvaluateAuthorizationDecisionCommandHandler>();
        });
        return services;
    }
}
