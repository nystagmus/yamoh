using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Yamoh.Infrastructure.Configuration.Validation;

public class OverlayConfigurationValidation(IConfiguration config) : IValidateOptions<OverlayConfiguration>
{
    private readonly OverlayConfiguration?
        _config = config.GetSection(OverlayConfiguration.Position).Get<OverlayConfiguration>();

    public ValidateOptionsResult Validate(string? name, OverlayConfiguration options)
    {
        if (this._config == null)
        {
            return ValidateOptionsResult.Fail("Overlay configuration is missing. \n");
        }

        string? validationMessage = null;

        if (!IsValidDateFormat(options.DateFormat))
        {
            validationMessage =
                $"{nameof(options.DateFormat)} string '{options.DateFormat}' is not a valid date-format string. \n";
        }

        if (!IsValidCultureString(options.Language))
        {
            validationMessage += $"{nameof(options.Language)} string '{options.Language}' is not a valid language-code. \n";
        }

        return validationMessage != null
            ? ValidateOptionsResult.Fail(validationMessage)
            : ValidateOptionsResult.Success;
    }

    private bool IsValidCultureString(string cultureName)
    {
        try
        {
            CultureInfo.GetCultureInfo(cultureName);
            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    private static bool IsValidDateFormat(string format)
    {
        var dateString = DateTimeOffset.UtcNow;

        try
        {
            dateString.ToString(format);
        }
        catch (FormatException)
        {
            return false;
        }
        return true;
    }
}
