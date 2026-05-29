using SkiaSharp;

namespace KlavLor.Infrastructure.ExternalServices.OsrsWiki;

internal static class WebpEncoder
{
    public const int Quality = 85;
    public const string ContentType = "image/webp";

    /// Decodes <paramref name="original"/>, resizes to the profile's max dimension if larger,
    /// and re-encodes as WebP. Returns null if Skia can't decode/encode the bytes — callers
    /// should fall back to storing the original.
    public static byte[]? TryEncode(byte[] original, ImageProfile profile)
    {
        using var bitmap = SKBitmap.Decode(original);
        if (bitmap is null)
            return null;

        var maxDim = profile.MaxDimension();
        using var resized = ResizeIfNeeded(bitmap, maxDim);
        using var image = SKImage.FromBitmap(resized);
        using var encoded = image.Encode(SKEncodedImageFormat.Webp, Quality);
        return encoded?.ToArray();
    }

    private static SKBitmap ResizeIfNeeded(SKBitmap source, int maxDim)
    {
        if (source.Width <= maxDim && source.Height <= maxDim)
            return source.Copy();

        var scale = Math.Min((float)maxDim / source.Width, (float)maxDim / source.Height);
        var w = Math.Max(1, (int)Math.Round(source.Width * scale));
        var h = Math.Max(1, (int)Math.Round(source.Height * scale));

        var info = new SKImageInfo(w, h, source.ColorType, source.AlphaType);
        var resized = new SKBitmap(info);
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        source.ScalePixels(resized, sampling);
        return resized;
    }
}
