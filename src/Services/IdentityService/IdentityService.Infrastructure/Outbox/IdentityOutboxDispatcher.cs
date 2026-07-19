using IdentityService.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Infrastructure.Outbox;

namespace IdentityService.Infrastructure.Outbox;

public sealed class IdentityOutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    ILogger<IdentityOutboxDispatcher> logger,
    IOptions<OutboxPublishPolicy> policy)
    : OutboxProcessorBase<IdentityDbContext>(scopeFactory, logger, policy);
