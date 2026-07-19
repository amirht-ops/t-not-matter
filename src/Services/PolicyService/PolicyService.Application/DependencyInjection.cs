using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using PolicyService.Application.Features.Consumption;
using PolicyService.Application.Features.Policies;
using PolicyService.Application.Features.QuotaPolicies;
using PolicyService.Application.Features.Subscriptions;
using PolicyService.Domain.Services;
using PolicyService.Domain.Specifications;

namespace PolicyService.Application;

/// <summary>
/// Registers the PolicyService Application layer: MediatR pipeline (handlers + validators from
/// this assembly) and the pure domain services the command handlers depend on. Transaction,
/// authorization, validation, retry and outbox behavior are owned by the platform pipeline
/// (ADR-015); this method only wires application composition.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<CreatePolicyCommandValidator>();
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<CreatePolicyCommandHandler>();
        });

        // Pure domain services (defined in PolicyService.Domain) used by the command handlers.
        services.AddScoped<ISubscriptionResolver, SubscriptionResolver>();
        services.AddScoped<IQuotaResolver, QuotaResolver>();
        services.AddScoped<IAllowanceEngine, AllowanceEngine>();
        services.AddScoped<IRecoveryProcessor, RecoveryProcessor>();
        services.AddScoped<IAllowanceAdministrationService, AllowanceAdministrationService>();
        services.AddScoped<IRegoGenerationService, RegoGenerationService>();
        services.AddScoped<IPolicyCompiler, PolicyCompiler>();
        services.AddScoped<IPolicyEvaluator, PolicyEvaluator>();

        // Domain specifications.
        services.AddScoped<IQuotaResolutionSpecification, QuotaResolutionSpecification>();
        services.AddScoped<IDebtDominanceSpecification, DebtDominanceSpecification>();
        services.AddScoped<IAllowanceSufficiencySpecification, AllowanceSufficiencySpecification>();

        return services;
    }
}
