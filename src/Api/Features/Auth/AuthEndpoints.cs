using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

public record LoginRequest(string Email, string Password);

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth");

        auth.MapPost("/login", async (LoginRequest r, SignInManager<IdentityUser> sm) =>
        {
            var res = await sm.PasswordSignInAsync(r.Email, r.Password,
                isPersistent: false, lockoutOnFailure: true);
            return res.Succeeded ? Results.Ok() : Results.Unauthorized();
        });

        auth.MapPost("/logout", async (SignInManager<IdentityUser> sm) =>
        {
            await sm.SignOutAsync();
            return Results.Ok();
        }).RequireAuthorization("Admin");

        auth.MapGet("/me", (ClaimsPrincipal u) => Results.Ok(new { email = u.Identity!.Name }))
            .RequireAuthorization("Admin");
    }
}