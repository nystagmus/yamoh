using System.Reflection;
using Cronos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using YAMOH.Services;

namespace YAMOH.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAllTypesOf<T>(
        this IServiceCollection services,
        Assembly assembly,
        ServiceLifetime lifetime = ServiceLifetime.Transient) =>
        services.AddAllTypesOf(typeof(T), assembly, lifetime);

    public static IServiceCollection AddAllTypesOf(
        this IServiceCollection services,
        Type targetType,
        Assembly assembly,
        ServiceLifetime lifetime = ServiceLifetime.Transient)
    {
        var isOpenGeneric = targetType.IsGenericTypeDefinition;

        var types = assembly.GetTypes()
            .Where(t => t is { IsInterface: false, IsAbstract: false })
            .Where(t =>
                isOpenGeneric
                    ? t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == targetType)
                    : targetType.IsAssignableFrom(t));

        foreach (var implementation in types)
        {
            services.Add(new ServiceDescriptor(implementation, implementation, lifetime));

            if (isOpenGeneric)
            {
                foreach (var closedInterface in implementation.GetInterfaces()
                             .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == targetType))
                {
                    services.Add(new ServiceDescriptor(closedInterface, implementation, lifetime));
                }
            }
            else
            {
                services.Add(new ServiceDescriptor(targetType, implementation, lifetime));
            }
        }

        return services;
    }

    public static IServiceCollection AddCronJob<T>(this IServiceCollection services, string cronExpression)
        where T : class, ICronJob
    {
        if (!CronExpression.TryParse(cronExpression, out var cron))
        {
            throw new ArgumentException("Invalid cron expression", nameof(cronExpression));
        }

        var entry = new CronRegistryEntry(typeof(T), cron);

        services.AddHostedService<CronScheduler>();
        services.TryAddSingleton<T>();
        services.AddSingleton(entry);

        return services;
    }
}
