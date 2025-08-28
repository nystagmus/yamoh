using Cronos;

namespace Yamoh.Infrastructure.Extensions;

public static class CronExpressionExtensions
{
    public static string ToDescriptor(this CronExpression cronExpression)
        => CronExpressionDescriptor.ExpressionDescriptor.GetDescription(cronExpression.ToString());
}
