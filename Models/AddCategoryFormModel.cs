using System.ComponentModel.DataAnnotations;

namespace Biktal.WebMVC.Models;

public sealed class AddCategoryFormModel
{
    [Required(ErrorMessage = "Category name is required.")]
    [StringLength(128, MinimumLength = 1)]
    [Display(Name = "Category name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(512)]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    [Display(Name = "Sort order")]
    public int? SortOrder { get; set; }
}
