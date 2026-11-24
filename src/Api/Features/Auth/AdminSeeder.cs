using Microsoft.AspNetCore.Identity;

public static class AdminSeeder
{
    public static async Task SeedAsync(IServiceProvider sp, IConfiguration cfg)
    {
        var um = sp.GetRequiredService<UserManager<IdentityUser>>();
        var rm = sp.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await rm.RoleExistsAsync("Admin")) await rm.CreateAsync(new IdentityRole("Admin"));

        var email = cfg["Admin:Email"];
        var password = cfg["Admin:Password"];
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password)) return;
        if (await um.FindByEmailAsync(email) is not null) return;

        var u = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
        var created = await um.CreateAsync(u, password);
        if (!created.Succeeded)
            throw new InvalidOperationException("Không tạo được admin: "
                + string.Join("; ", created.Errors.Select(e => e.Description)));
        await um.AddToRoleAsync(u, "Admin");
    }
}