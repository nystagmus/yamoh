using Cronos;

namespace YAMOH.Infrastructure.Scheduling;

public sealed record CronRegistryEntry(Type Type, CronExpression CrontabSchedule);