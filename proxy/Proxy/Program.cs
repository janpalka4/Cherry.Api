using System.Security.Claims;
using Auth0.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Proxy;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var (redis, key) = RedisOptions.GetConnectionMultiplexer(builder.Configuration);
builder.Services.AddDataProtection()
    .SetApplicationName("Cherry.Proxy")
    .PersistKeysToStackExchangeRedis(redis, key);

builder.Services.AddAuth0WebAppAuthentication(Auth0Options.Configure(builder.Configuration))
    .WithAccessToken(Auth0Options.ConfigureAccessToken(builder.Configuration));

var app = builder.Build();

app.UseHsts();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapReverseProxy();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        var accessToken = await context.GetTokenAsync("access_token");
        if (accessToken is not null)
        {
            context.Request.Headers.Authorization = $"Bearer {accessToken}";
        }
    }

    await next();
});

app.MapGet("Account/Profile", (HttpContext context) =>
{
    if (context.User.Identity?.IsAuthenticated is not true)
    {
        return Results.Unauthorized();
    }

    return Results.Json(new
    {
        Id = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
        context.User.Identity.Name,
        context.User.Identity?.IsAuthenticated
    });
});

app.MapGet("Account/Login", (HttpContext context, [FromQuery] string returnUrl = "/") =>
{
    var authenticationProperties = new LoginAuthenticationPropertiesBuilder()
        .WithRedirectUri(returnUrl)
        .Build();

    context.Request.IsHttps = true;
    return context.ChallengeAsync(Auth0Constants.AuthenticationScheme, authenticationProperties);
});

app.MapGet("Account/Logout", async context =>
{
    var authenticationProperties = new LogoutAuthenticationPropertiesBuilder()
        .WithRedirectUri("/")
        .Build();

    context.Request.IsHttps = true;
    await context.SignOutAsync(Auth0Constants.AuthenticationScheme, authenticationProperties);
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
});

app.Run();