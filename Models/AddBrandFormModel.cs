using System.ComponentModel.DataAnnotations;

namespace Biktal.WebMVC.Models;

public sealed class AddBrandFormModel
{
    [Required(ErrorMessage = "Brand name is required.")]
    [StringLength(128, MinimumLength = 1)]
    [Display(Name = "Brand name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(512)]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    [Display(Name = "Sort order")]
    public int? SortOrder { get; set; }
}
