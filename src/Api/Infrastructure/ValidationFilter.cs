using FluentValidation;

public class ValidationFilter<T>(IValidator<T> validator) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var arg = ctx.Arguments.OfType<T>().First();
        var result = await validator.ValidateAsync(arg);
        if (!result.IsValid) return Results.ValidationProblem(result.ToDictionary());
        return await next(ctx);
    }
}