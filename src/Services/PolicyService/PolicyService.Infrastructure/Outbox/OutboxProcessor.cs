using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Infrastructure.Outbox;
using PolicyService.Infrastructure.Persistence;

namespace PolicyService.Infrastructure.Outbox;

public sealed class OutboxProcessor(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxProcessor> logger,
    IOptions<OutboxPublishPolicy> policy)
    : OutboxProcessorBase<PolicyDbContext>(scopeFactory, logger, policy);
