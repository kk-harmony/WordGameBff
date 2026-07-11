using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using WordGameBff.Application.Configuration;

namespace WordGameBff.Api.Extensions;

public static class AuthenticationExtensions
{
    public static IServiceCollection AddWordGameBffAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var sessionOptions = configuration.GetSection(WordGameBff.Application.Configuration.SessionOptions.SectionName)
                                 .Get<WordGameBff.Application.Configuration.SessionOptions>()
                             ?? throw new InvalidOperationException("Session configuration is required.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = sessionOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = sessionOptions.Issuer,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(sessionOptions.SigningKey)),
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = JwtRegisteredClaimNames.Sub
                };

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    },
                    OnTokenValidated = async context =>
                    {
                        var sessionId = context.Principal?.FindFirst("sid")?.Value;
                        if (string.IsNullOrEmpty(sessionId))
                        {
                            context.Fail("Missing session id.");
                            return;
                        }

                        var revocationStore = context.HttpContext.RequestServices
                            .GetRequiredService<WordGameBff.Application.Auth.ISessionRevocationStore>();
                        if (await revocationStore.IsRevokedAsync(sessionId, context.HttpContext.RequestAborted))
                        {
                            context.Fail("Session revoked.");
                        }
                    }
                };
            });

        services.AddAuthorization();
        return services;
    }
}
