using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Serilog;
using Yamoh.Infrastructure.Extensions;

namespace Yamoh.Infrastructure.Configuration.Validation;

public class YamohConfigurationValidation(IConfiguration config) : IValidateOptions<YamohConfiguration>
{
    private readonly YamohConfiguration? _config = config.GetSection(YamohConfiguration.Position).Get<YamohConfiguration>();

    public ValidateOptionsResult Validate(string? name, YamohConfiguration options)
    {
        if (this._config == null)
        {
            return ValidateOptionsResult.Fail("Yamoh configuration is missing. \n");
        }

        var errors = new List<YamohConfigurationError>();

        try
        {
            ValidateUrl(this._config.PlexUrl, nameof(this._config.PlexUrl), errors);
            ValidateUrl(this._config.MaintainerrUrl, nameof(this._config.MaintainerrUrl), errors);

            ValidatePathFormat(this._config.AssetBasePath, nameof(this._config.AssetBasePath), errors);
            ValidateOrCreatePathExists(this._config.AssetBaseFullPath, nameof(this._config.AssetBasePath), errors);
            ValidatePathIsWriteable(this._config.AssetBaseFullPath, nameof(this._config.AssetBasePath), errors);

            ValidatePathFormat(this._config.BackupImagePath, nameof(this._config.BackupImagePath), errors);
            ValidateOrCreatePathExists(this._config.BackupImageFullPath, nameof(this._config.BackupImagePath), errors);
            ValidatePathIsWriteable(this._config.BackupImageFullPath, nameof(this._config.BackupImagePath), errors);

            ValidatePathFormat(this._config.TempImagePath, nameof(this._config.TempImagePath), errors);
            ValidateOrCreatePathExists(this._config.TempImageFullPath, nameof(this._config.TempImagePath), errors);
            ValidatePathIsWriteable(this._config.TempImageFullPath, nameof(this._config.TempImagePath), errors);

            ValidatePathFormat(this._config.FontPath, nameof(this._config.FontPath), errors);
            ValidatePathExists(this._config.FontFullPath, nameof(this._config.FontPath), errors);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception encountered when parsing {YamohConfiguration} configuration", nameof(YamohConfiguration));
            return ValidateOptionsResult.Fail($"Error encountered while validating {nameof(YamohConfiguration)}.");
        }

        if (errors.Count == 0)
        {
            return ValidateOptionsResult.Success;
        }

        var validationErrors = errors.Select(error => error.Issue).ToList();

        return ValidateOptionsResult.Fail(validationErrors);
    }

    private static void ValidateUrl(string url, string propertyName, List<YamohConfigurationError> errors)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            errors.Add(new YamohConfigurationError(propertyName, $"{propertyName} must be provided."));
            return;
        }

        if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
        {
            errors.Add(new YamohConfigurationError(propertyName, $"{propertyName} is not a properly formatted Url."));
            return;
        }

        if (!IsUrlReachable(url))
        {
            errors.Add(new YamohConfigurationError(propertyName, $"{propertyName} url: {url} is not reachable."));
        }
    }

    private static bool IsUrlReachable(string url)
    {
        try
        {
            var uri = new Uri(url);

            using var client = new TcpClient(uri.Host, uri.Port);

            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void ValidatePathExists(string path, string propertyName, List<YamohConfigurationError> errors)
    {
        if (errors.Any(x => x.Field == propertyName)) return;

        if (!Directory.Exists(path))
        {
            errors.Add(new YamohConfigurationError(propertyName, $"{propertyName} Path does not exist. Path: {path}"));
        }
    }

    private static void ValidateOrCreatePathExists(string path, string propertyName, List<YamohConfigurationError> errors)
    {
        if (errors.Any(x => x.Field == propertyName)) return;

        if (Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception)
        {
            errors.Add(new YamohConfigurationError(propertyName, $"Could not create {propertyName} Path. Path: {path}"));
        }
    }

    private static void ValidatePathIsWriteable(string path, string propertyName, List<YamohConfigurationError> errors)
    {
        if (errors.Any(x => x.Field == propertyName)) return;

        if (!new DirectoryInfo(path).HasWritePermissions())
        {
            errors.Add(new YamohConfigurationError(propertyName, $"{propertyName} is not writeable. Path: {path}"));
        }
    }

    private static void ValidatePathFormat(string path, string propertyName, List<YamohConfigurationError> errors)
    {
        if (errors.Any(x => x.Field == propertyName)) return;

        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            errors.Add(new YamohConfigurationError(propertyName, $"{propertyName} contains invalid path characters."));
    }
}
