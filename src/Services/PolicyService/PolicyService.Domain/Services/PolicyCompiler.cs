using PolicyService.Domain.ValueObjects;
using SharedKernel.Results;

namespace PolicyService.Domain.Services;

/// <summary>Compiles a policy expression into a Rego module.</summary>
public sealed class PolicyCompiler : IPolicyCompiler
{
    public Result<RegoModule> Compile(PolicyExpression expression)
    {
        if (expression is null)
            return Result<RegoModule>.Failure(PolicyErrors.PolicyExpressionRequired);

        return RegoModule.Create(expression.Value);
    }
}
