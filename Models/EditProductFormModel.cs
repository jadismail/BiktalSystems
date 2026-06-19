using System.ComponentModel.DataAnnotations;

namespace Biktal.WebMVC.Models;

public sealed class EditProductFormModel : IValidatableObject
{
    private const decimal MaxMoney = 999_999.99m;

    public Guid ProductId { get; set; }

    [Required(ErrorMessage = "SKU is required.")]
    [StringLength(96, MinimumLength = 1)]
    [Display(Name = "SKU")]
    public string Sku { get; set; } = string.Empty;

    [Required(ErrorMessage = "Product name is required.")]
    [StringLength(256, MinimumLength = 1)]
    [Display(Name = "Product name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(128)]
    [Display(Name = "Category")]
    public string Category { get; set; } = string.Empty;

    [Display(Name = "Brand")]
    public Guid? BrandId { get; set; }

    [StringLength(128)]
    [Display(Name = "Barcode")]
    public string? Barcode { get; set; }

    [Display(Name = "Cost (USD)")]
    public decimal Cost { get; set; }

    [Display(Name = "Price (USD)")]
    public decimal Price { get; set; }

    [Range(0, 99_999_999, ErrorMessage = "Stock must be between 0 and 99,999,999.")]
    [Display(Name = "Stock on hand")]
    public int StockQuantity { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ProductId == Guid.Empty)
            yield return new ValidationResult("Product is required.", new[] { nameof(ProductId) });

        if (Price < 0m || Price > MaxMoney)
            yield return new ValidationResult($"Price must be between 0 and {MaxMoney:N2}.", new[] { nameof(Price) });

        if (Cost < 0m || Cost > MaxMoney)
            yield return new ValidationResult($"Cost must be between 0 and {MaxMoney:N2}.", new[] { nameof(Cost) });
    }
}
