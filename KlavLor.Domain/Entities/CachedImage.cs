using System.ComponentModel.DataAnnotations;

namespace KlavLor.Domain.Entities;

public sealed class CachedImage
{
    [Required, Key]
    public int Id { get; set; }

    [Required, StringLength(1000)]
    public string SourceUrl { get; set; } = "";

    [Required]
    public byte[] ImageData { get; set; } = [];

    [Required, StringLength(100)]
    public string ContentType { get; set; } = "image/png";

    [Required]
    public DateTimeOffset CachedAt { get; set; }
}
