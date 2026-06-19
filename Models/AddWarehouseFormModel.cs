using System.ComponentModel.DataAnnotations;

namespace Biktal.WebMVC.Models;

public sealed class AddWarehouseFormModel
{
    [Required(ErrorMessage = "Warehouse name is required.")]
    [StringLength(128, MinimumLength = 1)]
    [Display(Name = "Location name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(32)]
    [Display(Name = "Code")]
    public string? Code { get; set; }

    [StringLength(512)]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    [Display(Name = "Sort order")]
    public int? SortOrder { get; set; }
}
