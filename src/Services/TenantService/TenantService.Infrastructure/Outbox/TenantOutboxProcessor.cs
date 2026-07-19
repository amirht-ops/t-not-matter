using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Infrastructure.Outbox;
using TenantService.Infrastructure.Persistence;

namespace TenantService.Infrastructure.Outbox;

public sealed class TenantOutboxProcessor(
    IServiceScopeFactory scopeFactory,
    ILogger<TenantOutboxProcessor> logger,
    IOptions<OutboxPublishPolicy> policy)
    : OutboxProcessorBase<TenantDbContext>(scopeFactory, logger, policy);
