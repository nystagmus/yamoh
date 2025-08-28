using Cronos;

namespace Yamoh.Infrastructure.Scheduling;

public sealed record CronRegistryEntry(Type Type, CronExpression CrontabSchedule);