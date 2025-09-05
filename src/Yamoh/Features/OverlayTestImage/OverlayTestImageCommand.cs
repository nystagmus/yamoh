using ImageMagick;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yamoh.Infrastructure;
using Yamoh.Infrastructure.Configuration;
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
            var tempPath = Path.Combine(this._yamohConfiguration.TempImageFullPath,
                $"{dateString}_overlay_test_{i:00}.jpg");

            // create a test image with a gradient background
            var startColor = GenerateRandomColor();
            var endColor = GenerateRandomColor();

            using var image = new MagickImage($"gradient:{startColor}-{endColor}", width, height);
            image.Format = MagickFormat.Jpg;
            await image.WriteAsync(tempPath, cancellationToken);

            logger.LogInformation("Test image created at {TempPath}", tempPath);

            // Apply overlay
            var overlaySettings = AddOverlaySettings.FromConfig(
                _overlayConfiguration,
                _yamohConfiguration.FontFullPath);

            var days = this._rand.Next(1, 60);

            var overlayText = _overlayConfiguration.GetOverlayText(DateTimeOffset.UtcNow.AddDays(days));

            var result = overlayHelper.AddOverlay(
                0,
                tempPath,
                overlayText,
                overlaySettings);

            var finalPath = Path.Combine(this._yamohConfiguration.TempImageFullPath,
                $"{dateString}_overlay_test_{i:00}_final.jpg");
            File.Copy(result.FullName, finalPath, overwrite: true);
            File.Delete(result.FullName);
            File.Delete(tempPath);

            logger.LogInformation("Overlay applied and saved at {FinalPath}", finalPath);
        }
    }

    private MagickColor GenerateRandomColor()
    {
        var r = (ushort)_rand.Next(ushort.MaxValue);
        var g = (ushort)_rand.Next(ushort.MaxValue);
        var b = (ushort)_rand.Next(ushort.MaxValue);
        return new MagickColor(r, g, b, ushort.MaxValue);
    }
}
