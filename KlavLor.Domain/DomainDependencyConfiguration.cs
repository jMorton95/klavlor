using Microsoft.Extensions.DependencyInjection;
using KlavLor.Domain.Factories;
using KlavLor.Domain.Services.Users;

namespace KlavLor.Domain;

public static class DomainDependencyConfiguration
{
    public static void AddDomain(this IServiceCollection services)
    {
        services.AddScoped<UserLoginService>();
        services.AddScoped<UserFactory>();
    }
}
