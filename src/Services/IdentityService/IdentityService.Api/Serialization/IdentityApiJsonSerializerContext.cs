using System.Text.Json.Serialization;
using IdentityService.Api.Contracts.Responses;
using IdentityService.Application.Features.Register;
using SharedKernel.Responses;
using SharedKernel.Results;

namespace IdentityService.Api.Serialization;

[JsonSerializable(typeof(ApiResponse<Unit>))]
[JsonSerializable(typeof(ApiResponse<RegisterResponse>))]
[JsonSerializable(typeof(ApiResponse<TokenResponse>))]
[JsonSerializable(typeof(ApiResponse<CommandStatusResponse>))]
[JsonSerializable(typeof(ApiError))]
[JsonSerializable(typeof(CommandStatusResponse))]
[JsonSerializable(typeof(RegisterResponse))]
[JsonSerializable(typeof(TokenResponse))]
public sealed partial class IdentityApiJsonSerializerContext : JsonSerializerContext;
