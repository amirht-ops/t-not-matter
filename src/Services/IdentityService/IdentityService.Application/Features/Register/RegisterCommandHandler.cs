using IdentityService.Application.Common.Abstractions;
using IdentityService.Domain;
using IdentityService.Domain.Aggregates.User;
using IdentityService.Domain.Repositories;
using IdentityService.Domain.Services;
using IdentityService.Domain.ValueObjects;
using MediatR;
using SharedKernel.Errors;
using SharedKernel.Results;

namespace IdentityService.Application.Features.Register;

public sealed class RegisterCommandHandler(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    Platform.Abstractions.Tenant.IRequestContextAccessor requestContextAccessor,
    IAuditSink auditSink) : IRequestHandler<RegisterCommand, Result<RegisterResponse>>
{
    public async Task<Result<RegisterResponse>> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        var context = requestContextAccessor.Context;

        var usernameResult = Username.Create(request.Username);
        if (usernameResult.IsFailure) return Result<RegisterResponse>.Failure(usernameResult.Error);

        var phoneNumberResult = PhoneNumber.Create(request.PhoneNumber);
        if (phoneNumberResult.IsFailure) return Result<RegisterResponse>.Failure(phoneNumberResult.Error);

        Email? email = null;
        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var emailResult = Email.Create(request.Email);
            if (emailResult.IsFailure) return Result<RegisterResponse>.Failure(emailResult.Error);
            email = emailResult.Value!;
        }

        var username = usernameResult.Value!;
        var phoneNumber = phoneNumberResult.Value!;

        // Public self-registration is tenant-less: the user is created in the global
        // tenant in a Pending state. The real TenantId is bound later during admin
        // onboarding (first role grant). Scope the request context to the global tenant
        // so RLS and uniqueness checks are consistent for this row.
        var tenantId = TenantConstants.GlobalTenantId;
        requestContextAccessor.Context = context with { TenantId = tenantId };

        if (await users.ExistsByUsernameAsync(username, tenantId, cancellationToken))
            return Result<RegisterResponse>.Failure(GeneralErrors.Conflict);

        if (await users.ExistsByPhoneNumberAsync(phoneNumber, tenantId, cancellationToken))
            return Result<RegisterResponse>.Failure(GeneralErrors.Conflict);

        var passwordHashResult = PasswordHash.FromHash(passwordHasher.Hash(request.Password));
        if (passwordHashResult.IsFailure)
        {
            return Result<RegisterResponse>.Failure(passwordHashResult.Error);
        }

        var user = User.Register(tenantId, username, phoneNumber,
            passwordHashResult.Value!, email,
            context.CorrelationId);
        await users.AddAsync(user, cancellationToken);
        await auditSink.RecordAsync("IdentityService.user_registered", tenantId, context.CorrelationId, user.Id, true, null, cancellationToken);
        return Result<RegisterResponse>.Success(new RegisterResponse(user.Id, username.Value!,
            user.Status.ToString()));
    }
}
