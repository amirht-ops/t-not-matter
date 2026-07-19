using AuthorizationService.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Infrastructure.Outbox;

namespace AuthorizationService.Infrastructure.Outbox;

public sealed class OutboxProcessor(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxProcessor> logger,
    IOptions<OutboxPublishPolicy> policy)
    : OutboxProcessorBase<AuthorizationDbContext>(scopeFactory, logger, policy);
