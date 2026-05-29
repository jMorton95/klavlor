namespace KlavLor.Infrastructure.ExternalServices.OsrsWiki;

public enum ImageProfile
{
    /// 128 px max dimension. Item inventory icons, drop log entries.
    ItemIcon,

    /// 256 px max dimension. Boss/source icons in headers and tooltips.
    SourceIcon,

    /// 256 px max dimension. Template node icons and other canvas assets.
    TemplateAsset
}

internal static class ImageProfileExtensions
{
    public static int MaxDimension(this ImageProfile profile) => profile switch
    {
        ImageProfile.ItemIcon => 128,
        ImageProfile.SourceIcon => 256,
        ImageProfile.TemplateAsset => 256,
        _ => 256
    };
}
