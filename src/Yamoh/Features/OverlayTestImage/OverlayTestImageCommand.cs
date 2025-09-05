using ImageMagick;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yamoh.Infrastructure;
using Yamoh.Infrastructure.Configuration;
using Yamoh.Infrastructure.FileProcessing;
using Yamoh.Infrastructure.ImageProcessing;

namespace Yamoh.Features.OverlayTestImage;

public class OverlayTestImageCommand(
    ILogger<OverlayTestImageCommand> logger,
    IOptions<OverlayConfiguration> overlayConfigurationOptions,
    IOptions<YamohConfiguration> yamohConfigurationOptions,
    OverlayHelper overlayHelper) : IYamohCommand
{
    public string CommandName => "test-overlay-image";
    public string CommandDescription => "Generate a test image and apply overlay for testing purposes.";

    private readonly OverlayConfiguration _overlayConfiguration = overlayConfigurationOptions.Value;
    private readonly YamohConfiguration _yamohConfiguration = yamohConfigurationOptions.Value;

    private readonly Random _rand = new();

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        const int width = 1000;
        const int height = 1500;

        var dateString = DateTime.Now.ToString("yyyyMMddHHmmss");

        for (var i = 0; i < 10; i++)
        {
            try
            {
                // create a test image with a gradient background
                var startColor = GenerateRandomColor();

                var endColor = GenerateRandomColor();

                using var image = new MagickImage($"gradient:{startColor}-{endColor}", width, height);

                image.Format = MagickFormat.Jpg;

                var tempPathInfo = new FileInfo(Path.Combine(this._yamohConfiguration.TempImageFullPath,
                    $"{dateString}_overlay_test_{i:00}.jpg"));
                var tempPath = new AssetPathInfo(tempPathInfo);

                await image.WriteAsync(tempPath.File.FullName, cancellationToken);

                logger.LogDebug("Test image created at {TempPath}", tempPath.File.FullName);

                // Apply overlay
                var overlaySettings = AddOverlaySettings.FromConfig(
                    _overlayConfiguration,
                    _yamohConfiguration.FontFullPath);

                var days = i * 5 + 1;

                var expirationDate = DateTimeOffset.UtcNow.AddDays(days);

                var overlayText = _overlayConfiguration.GetOverlayText(expirationDate);

                var result = overlayHelper.AddOverlay(
                    0,
                    tempPath,
                    overlayText,
                    overlaySettings);

                var finalPath = Path.Combine(this._yamohConfiguration.TempImageFullPath,
                    $"{dateString}_overlay_test_{i:00}_expiration_{expirationDate:yy-MM-dd}_final.jpg");
                result.File.CopyTo(finalPath, overwrite: true);
                result.File.Delete();
                tempPathInfo.Delete();

                logger.LogInformation("Overlay applied and saved at {FinalPath}", finalPath);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error while generating overlay image");
            }
        }
        logger.LogInformation("Test images created");
    }

    private MagickColor GenerateRandomColor()
    {
        var r = (ushort)_rand.Next(ushort.MaxValue);
        var g = (ushort)_rand.Next(ushort.MaxValue);
        var b = (ushort)_rand.Next(ushort.MaxValue);
        return new MagickColor(r, g, b, ushort.MaxValue);
    }
}
