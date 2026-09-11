namespace G.PcHealthCheck;

internal static class BrandAssets
{
    private const string ShieldResourceName = "G.PcHealthCheck.g-shield.png";

    public static Image? LoadShield()
    {
        var assembly = typeof(BrandAssets).Assembly;
        using var stream = assembly.GetManifestResourceStream(ShieldResourceName);
        if (stream is null) return null;

        using var source = Image.FromStream(stream, useEmbeddedColorManagement: true, validateImageData: true);
        return new Bitmap(source);
    }
}
