using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Yamoh.Infrastructure.Configuration.Validation;

public class ScheduleConfigurationValidation(IConfiguration config) : IValidateOptions<ScheduleConfiguration>
{
    private readonly ScheduleConfiguration? _config =
        config.GetSection(ScheduleConfiguration.Position).Get<ScheduleConfiguration>();

    public ValidateOptionsResult Validate(string? name, ScheduleConfiguration options)
    {
        if (this._config == null)
        {
            return ValidateOptionsResult.Fail("Schedule configuration is missing. \n");
        }

        var isValid = Cronos.CronExpression.TryParse(this._config.OverlayManagerCronSchedule, out _);

        return !isValid
            ? ValidateOptionsResult.Fail(
                $"Cron expression '{this._config.OverlayManagerCronSchedule}' is invalid or unparseable. \n")
            : ValidateOptionsResult.Success;
    }
}
