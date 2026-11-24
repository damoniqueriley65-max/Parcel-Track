using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ParcelTrack.Data;
using ParcelTrack.Middleware;
using ParcelTrack.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// EF Core: any controller that asks for an AppDbContext gets one,
// connected to the database named in appsettings.json.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("ParcelTrackDb")));

// Identity: user accounts and roles, stored through AppDbContext.
// RequireConfirmedAccount = false: this app can't send email.
builder.Services.AddDefaultIdentity<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>();

// A policy: "CanManageParcels" = Admin or Staff.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CanManageParcels", policy => policy.RequireRole("Admin", "Staff"));
});

// Part A: JWT bearer tokens for the API.
// The browser pages keep the Identity cookie; the API uses tokens instead, because a
// program with no cookie cannot follow a 302 redirect to the login page.
// JwtBearer checks all four: issuer, audience, expiry and signing key.
builder.Services
    .AddAuthentication(options =>
    {
        // Identity's cookies stay the default for the browser pages.
        options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
        options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
    })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        IConfigurationSection jwt = builder.Configuration.GetSection("Jwt");
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwt["Audience"],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!)),
            ClockSkew = TimeSpan.Zero
        };
    });

// Part D: the delivery fee rule in one place. Scoped, because it shares the request's
// AppDbContext (it looks the branch's parish up in the database).
builder.Services.AddScoped<IDeliveryFeeCalculator, DeliveryFeeCalculator>();

var app = builder.Build();

// Start-up seed: the roles and the two demo accounts. A scope, because there is no request yet.
using (IServiceScope scope = app.Services.CreateScope())
{
    await RoleSeed.Run(scope.ServiceProvider, app.Configuration);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// Part B: log every request with a short id. After UseStaticFiles (so /css/site.css is not
// logged) and before UseRouting and UseAuthentication, so a 401 or 403 is still logged.
app.UseMiddleware<RequestLoggingMiddleware>();

app.UseRouting();

// Who are you? first, then: are you allowed? The order matters.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// The Register, Log in and Log out pages come with the Identity.UI package.
app.MapRazorPages();

app.Run();

// Exposed as a public partial class so integration tests can target it with
// WebApplicationFactory<Program> (the standard pattern in the ASP.NET Core docs).
public partial class Program { }
