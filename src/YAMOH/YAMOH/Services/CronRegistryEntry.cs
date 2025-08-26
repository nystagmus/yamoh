using Cronos;

namespace YAMOH.Services;

public sealed record CronRegistryEntry(Type Type, CronExpression CrontabSchedule);