using System.ComponentModel.DataAnnotations;
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
            return ValidateOptionsResult.Fail("Overlay configuration is missing." + Environment.NewLine);
        }

        List<string> validationFailures = [];

        if (!IsValidDateFormat(options.DateFormat))
        {
            validationFailures.Add(
                $"{nameof(options.DateFormat)} string '{options.DateFormat}' is not a valid date-format string.");
        }

        if (!IsValidCultureString(options.Language))
        {
            validationFailures.Add(
                $"{nameof(options.Language)} string '{options.Language}' is not a valid language-code.");
        }

        if (!IsValidImageMagickColorString(options.FontColor))
        {
            validationFailures.Add(
                $"{nameof(options.FontColor)} string '{options.FontColor}' is not a valid ImageMagick color string.");
        }

        if (!IsValidImageMagickColorString(options.BackColor))
        {
            validationFailures.Add(
                $"{nameof(options.BackColor)} string '{options.BackColor}' is not a valid ImageMagick color string.");
        }

        return validationFailures.Count > 0
            ? ValidateOptionsResult.Fail(validationFailures)
            : ValidateOptionsResult.Success;
    }

    private bool IsValidImageMagickColorString(string colorString)
    {
        try
        {
            var test = new ImageMagick.MagickColor(colorString);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
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
