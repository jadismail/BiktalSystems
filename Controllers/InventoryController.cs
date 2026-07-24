using Biktal.Domain.Inventory;
using Biktal.Infrastructure.Inventory;
using Biktal.Infrastructure.Persistence;
using Biktal.WebMVC.Models;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace Biktal.WebMVC.Controllers;

[Authorize]
public sealed class InventoryController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly InventoryMovementService _movements;
    private readonly CatalogProductExcelImportService _productImport;

    public InventoryController(
        ApplicationDbContext db,
        InventoryMovementService movements,
        CatalogProductExcelImportService productImport)
    {
        _db = db;
        _movements = movements;
        _productImport = productImport;
    }

    public IActionResult Index() => RedirectToAction(nameof(Products));

    public async Task<IActionResult> Products(Guid? editId, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Products";
        ViewData["Module"] = "Inventory";

        EditProductFormModel? editProduct = null;
        var openEditModal = false;

        if (editId is { } id)
        {
            var product = await _db.CatalogProducts.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (product is not null)
            {
                editProduct = new EditProductFormModel
                {
                    ProductId = product.Id,
                    Sku = product.Sku,
                    Name = product.Name,
                    Category = product.Category,
                    BrandId = product.BrandId,
                    Barcode = product.Barcode,
                    Cost = product.Cost,
                    Price = product.Price,
                    StockQuantity = product.StockQuantity
                };
                openEditModal = true;
            }
            else
            {
                TempData["ProductError"] = "Product not found.";
            }
        }

        return View(await BuildProductsPageViewModelAsync(null, false, editProduct, openEditModal, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddProduct(
        [Bind(Prefix = nameof(ProductsPageViewModel.NewProduct))] AddProductFormModel model,
        CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Products";
        ViewData["Module"] = "Inventory";

        var skuNormalized = model.Sku.Trim().ToUpperInvariant();
        if (skuNormalized.Length > 0 &&
            await _db.CatalogProducts.AnyAsync(p => p.Sku.ToUpper() == skuNormalized, cancellationToken))
        {
            ModelState.AddModelError(
                $"{nameof(ProductsPageViewModel.NewProduct)}.{nameof(AddProductFormModel.Sku)}",
                "That SKU is already in use. Choose another SKU.");
        }

        var barcodeNorm = string.IsNullOrWhiteSpace(model.Barcode) ? null : model.Barcode.Trim();
        if (barcodeNorm is not null &&
            await _db.CatalogProducts.AnyAsync(
                p => p.Barcode != null && p.Barcode.ToUpper() == barcodeNorm.ToUpperInvariant(),
                cancellationToken))
        {
            ModelState.AddModelError(
                $"{nameof(ProductsPageViewModel.NewProduct)}.{nameof(AddProductFormModel.Barcode)}",
                "That barcode is already assigned to another product.");
        }

        var categoryOptions = await LoadOrderedCategoriesAsync(cancellationToken);
        if (categoryOptions.Count == 0)
        {
            ModelState.AddModelError(
                string.Empty,
                "Add at least one category under Inventory → Categories before you can add products.");
        }

        string? categoryToStore = null;
        if (categoryOptions.Count > 0 && string.IsNullOrWhiteSpace(model.Category))
        {
            ModelState.AddModelError(
                $"{nameof(ProductsPageViewModel.NewProduct)}.{nameof(AddProductFormModel.Category)}",
                "Please select a category.");
        }
        else if (categoryOptions.Count > 0)
        {
            var submitted = model.Category.Trim();
            var match = categoryOptions.FirstOrDefault(c =>
                string.Equals(c.Name, submitted, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                ModelState.AddModelError(
                    $"{nameof(ProductsPageViewModel.NewProduct)}.{nameof(AddProductFormModel.Category)}",
                    "Select a category from the list only — custom category text is not allowed.");
            }
            else
            {
                categoryToStore = match.Name;
                model.Category = match.Name;
            }
        }

        Guid? brandIdToStore = null;
        if (model.BrandId is { } postedBrand && postedBrand != Guid.Empty)
        {
            if (!await _db.CatalogBrands.AnyAsync(b => b.Id == postedBrand, cancellationToken))
            {
                ModelState.AddModelError(
                    $"{nameof(ProductsPageViewModel.NewProduct)}.{nameof(AddProductFormModel.BrandId)}",
                    "Select a brand from the list only.");
            }
            else
            {
                brandIdToStore = postedBrand;
            }
        }

        if (!ModelState.IsValid)
        {
            return View("Products", await BuildProductsPageViewModelAsync(model, true, null, false, cancellationToken));
        }

        _db.CatalogProducts.Add(new CatalogProduct
        {
            Id = Guid.NewGuid(),
            Sku = model.Sku.Trim(),
            Name = model.Name.Trim(),
            Category = categoryToStore!,
            BrandId = brandIdToStore,
            Barcode = barcodeNorm,
            Cost = decimal.Round(model.Cost, 2, MidpointRounding.AwayFromZero),
            Price = decimal.Round(model.Price, 2, MidpointRounding.AwayFromZero),
            StockQuantity = model.StockQuantity,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);

        TempData["ProductMessage"] = $"“{model.Name.Trim()}” was added to the catalog.";
        return RedirectToAction(nameof(Products));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditProduct(
        [Bind(Prefix = nameof(ProductsPageViewModel.EditProduct))] EditProductFormModel model,
        CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Products";
        ViewData["Module"] = "Inventory";

        var product = await _db.CatalogProducts.FirstOrDefaultAsync(p => p.Id == model.ProductId, cancellationToken);
        if (product is null)
        {
            TempData["ProductError"] = "Product not found.";
            return RedirectToAction(nameof(Products));
        }

        var skuNormalized = model.Sku.Trim().ToUpperInvariant();
        if (skuNormalized.Length > 0 &&
            await _db.CatalogProducts.AnyAsync(
                p => p.Id != model.ProductId && p.Sku.ToUpper() == skuNormalized,
                cancellationToken))
        {
            ModelState.AddModelError(
                $"{nameof(ProductsPageViewModel.EditProduct)}.{nameof(EditProductFormModel.Sku)}",
                "That SKU is already in use. Choose another SKU.");
        }

        var barcodeNorm = string.IsNullOrWhiteSpace(model.Barcode) ? null : model.Barcode.Trim();
        if (barcodeNorm is not null &&
            await _db.CatalogProducts.AnyAsync(
                p => p.Id != model.ProductId && p.Barcode != null && p.Barcode.ToUpper() == barcodeNorm.ToUpperInvariant(),
                cancellationToken))
        {
            ModelState.AddModelError(
                $"{nameof(ProductsPageViewModel.EditProduct)}.{nameof(EditProductFormModel.Barcode)}",
                "That barcode is already assigned to another product.");
        }

        var categoryOptions = await LoadOrderedCategoriesAsync(cancellationToken);
        string? categoryToStore = null;
        if (categoryOptions.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Add at least one category under Inventory → Categories.");
        }
        else if (string.IsNullOrWhiteSpace(model.Category))
        {
            ModelState.AddModelError(
                $"{nameof(ProductsPageViewModel.EditProduct)}.{nameof(EditProductFormModel.Category)}",
                "Please select a category.");
        }
        else
        {
            var submitted = model.Category.Trim();
            var match = categoryOptions.FirstOrDefault(c =>
                string.Equals(c.Name, submitted, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                ModelState.AddModelError(
                    $"{nameof(ProductsPageViewModel.EditProduct)}.{nameof(EditProductFormModel.Category)}",
                    "Select a category from the list only.");
            }
            else
            {
                categoryToStore = match.Name;
                model.Category = match.Name;
            }
        }

        Guid? brandIdToStore = null;
        if (model.BrandId is { } postedBrand && postedBrand != Guid.Empty)
        {
            if (!await _db.CatalogBrands.AnyAsync(b => b.Id == postedBrand, cancellationToken))
            {
                ModelState.AddModelError(
                    $"{nameof(ProductsPageViewModel.EditProduct)}.{nameof(EditProductFormModel.BrandId)}",
                    "Select a brand from the list only.");
            }
            else
            {
                brandIdToStore = postedBrand;
            }
        }

        if (!ModelState.IsValid)
        {
            return View("Products", await BuildProductsPageViewModelAsync(null, false, model, true, cancellationToken));
        }

        product.Sku = model.Sku.Trim();
        product.Name = model.Name.Trim();
        product.Category = categoryToStore!;
        product.BrandId = brandIdToStore;
        product.Barcode = barcodeNorm;
        product.Cost = decimal.Round(model.Cost, 2, MidpointRounding.AwayFromZero);
        product.Price = decimal.Round(model.Price, 2, MidpointRounding.AwayFromZero);
        product.StockQuantity = model.StockQuantity;
        product.ModifiedAtUtc = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        TempData["ProductMessage"] = $"“{model.Name.Trim()}” was updated.";
        return RedirectToAction(nameof(Products));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteProduct(Guid id, CancellationToken cancellationToken)
    {
        var product = await _db.CatalogProducts.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            TempData["ProductError"] = "Product not found.";
            return RedirectToAction(nameof(Products));
        }

        var poLineCount = await _db.PurchaseOrderLines.CountAsync(l => l.CatalogProductId == id, cancellationToken);
        if (poLineCount > 0)
        {
            TempData["ProductError"] =
                $"Cannot delete “{product.Name}” — it appears on {poLineCount} purchase order line(s). Remove those lines first.";
            return RedirectToAction(nameof(Products));
        }

        var movements = await _db.InventoryMovements
            .Where(m => m.CatalogProductId == id)
            .ToListAsync(cancellationToken);

        foreach (var movement in movements)
        {
            movement.ProductSku ??= product.Sku;
            movement.ProductName ??= product.Name;
            movement.CatalogProductId = null;
        }

        _db.CatalogProducts.Remove(product);
        await _db.SaveChangesAsync(cancellationToken);

        TempData["ProductMessage"] = movements.Count > 0
            ? $"“{product.Name}” was removed from the catalog. {movements.Count} stock movement(s) were kept for history."
            : $"“{product.Name}” was removed from the catalog.";
        return RedirectToAction(nameof(Products));
    }

    [HttpGet]
    public IActionResult Import()
    {
        ViewData["Title"] = "Import products";
        ViewData["Module"] = "Inventory";
        ViewData["ModuleSubtitle"] = "Upload an Excel workbook to add or update catalog SKUs in bulk.";
        return View(new InventoryImportPageViewModel());
    }

    [HttpGet]
    public async Task<IActionResult> DownloadImportTemplate(CancellationToken cancellationToken)
    {
        var products = await _db.CatalogProducts.AsNoTracking()
            .OrderBy(p => p.Sku)
            .Select(p => new
            {
                p.Sku,
                p.Name,
                p.Category,
                Brand = p.Brand != null ? p.Brand.Name : null,
                p.Barcode,
                p.Cost,
                p.Price,
                p.StockQuantity
            })
            .ToListAsync(cancellationToken);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Products");
        sheet.Cell(1, 1).Value = "SKU";
        sheet.Cell(1, 2).Value = "Name";
        sheet.Cell(1, 3).Value = "Category";
        sheet.Cell(1, 4).Value = "Brand";
        sheet.Cell(1, 5).Value = "Barcode";
        sheet.Cell(1, 6).Value = "Cost";
        sheet.Cell(1, 7).Value = "Price";
        sheet.Cell(1, 8).Value = "Stock";
        sheet.Row(1).Style.Font.Bold = true;
        sheet.Column(5).Style.NumberFormat.Format = "@";

        var row = 2;
        foreach (var product in products)
        {
            sheet.Cell(row, 1).Value = product.Sku;
            sheet.Cell(row, 2).Value = product.Name;
            sheet.Cell(row, 3).Value = product.Category;
            sheet.Cell(row, 4).Value = product.Brand ?? string.Empty;
            sheet.Cell(row, 5).Value = product.Barcode ?? string.Empty;
            sheet.Cell(row, 6).Value = product.Cost;
            sheet.Cell(row, 7).Value = product.Price;
            sheet.Cell(row, 8).Value = product.StockQuantity;
            row++;
        }

        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "biktal-products.xlsx");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Import(IFormFile? file, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Import products";
        ViewData["Module"] = "Inventory";
        ViewData["ModuleSubtitle"] = "Upload an Excel workbook to add or update catalog SKUs in bulk.";

        if (file is null || file.Length == 0)
        {
            ModelState.AddModelError(string.Empty, "Choose an Excel file (.xlsx) to upload.");
            return View(new InventoryImportPageViewModel());
        }

        var extension = Path.GetExtension(file.FileName);
        if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(string.Empty, "Only .xlsx Excel files are supported.");
            return View(new InventoryImportPageViewModel());
        }

        await using var stream = file.OpenReadStream();
        var result = await _productImport.ImportAsync(stream, cancellationToken);

        if (!result.Succeeded)
        {
            return View(new InventoryImportPageViewModel { LastResult = result });
        }

        var message = $"Import complete — {result.Inserted} added, {result.Updated} updated.";
        var rowErrors = result.Issues.Count(i => i.Severity == CatalogProductExcelImportIssueSeverity.Error);
        if (rowErrors > 0)
            message += $" {rowErrors} row(s) were skipped due to errors.";
        TempData["ProductMessage"] = message;
        return RedirectToAction(nameof(Products));
    }

    public async Task<IActionResult> Categories(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Categories";
        ViewData["Module"] = "Inventory";
        ViewData["ModuleSubtitle"] = "Canonical groups for products, POS search, and reporting.";

        var vm = await BuildCategoriesPageAsync(cancellationToken);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddCategory(
        [Bind(Prefix = nameof(CategoriesPageViewModel.NewCategory))] AddCategoryFormModel model,
        CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Categories";
        ViewData["Module"] = "Inventory";
        ViewData["ModuleSubtitle"] = "Canonical groups for products, POS search, and reporting.";

        var nameNorm = model.Name.Trim();
        if (nameNorm.Length > 0 &&
            await _db.CatalogCategories.AnyAsync(
                c => c.Name.ToUpper() == nameNorm.ToUpperInvariant(),
                cancellationToken))
        {
            ModelState.AddModelError(
                $"{nameof(CategoriesPageViewModel.NewCategory)}.{nameof(AddCategoryFormModel.Name)}",
                "A category with that name already exists.");
        }

        if (!ModelState.IsValid)
        {
            var vm = await BuildCategoriesPageAsync(cancellationToken);
            return View("Categories", new CategoriesPageViewModel
            {
                Categories = vm.Categories,
                NewCategory = model,
                OpenAddModal = true
            });
        }

        var maxSort = await _db.CatalogCategories.AnyAsync(cancellationToken)
            ? await _db.CatalogCategories.MaxAsync(c => c.SortOrder, cancellationToken)
            : 0;
        var sort = model.SortOrder ?? maxSort + 10;

        _db.CatalogCategories.Add(new CatalogCategory
        {
            Id = Guid.NewGuid(),
            Name = nameNorm,
            Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim(),
            SortOrder = sort,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);

        TempData["CategoryMessage"] = $"Category “{nameNorm}” was created.";
        return RedirectToAction(nameof(Categories));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCategory(Guid id, CancellationToken cancellationToken)
    {
        var category = await _db.CatalogCategories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (category is null)
            return RedirectToAction(nameof(Categories));

        var productCount = await _db.CatalogProducts.CountAsync(
            p => p.Category.ToUpper() == category.Name.ToUpperInvariant(),
            cancellationToken);

        if (productCount > 0)
        {
            TempData["CategoryError"] =
                $"Cannot delete “{category.Name}” — {productCount} product(s) still use this category. Reassign products first.";
            return RedirectToAction(nameof(Categories));
        }

        _db.CatalogCategories.Remove(category);
        await _db.SaveChangesAsync(cancellationToken);

        TempData["CategoryMessage"] = $"Category “{category.Name}” was removed.";
        return RedirectToAction(nameof(Categories));
    }

    public async Task<IActionResult> Brands(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Brands";
        ViewData["Module"] = "Inventory";
        ViewData["ModuleSubtitle"] = "Manufacturer master list — optional on each product.";

        var vm = await BuildBrandsPageAsync(cancellationToken);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddBrand(
        [Bind(Prefix = nameof(BrandsPageViewModel.NewBrand))] AddBrandFormModel model,
        CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Brands";
        ViewData["Module"] = "Inventory";
        ViewData["ModuleSubtitle"] = "Manufacturer master list — optional on each product.";

        var nameNorm = model.Name.Trim();
        if (nameNorm.Length > 0 &&
            await _db.CatalogBrands.AnyAsync(
                b => b.Name.ToUpper() == nameNorm.ToUpperInvariant(),
                cancellationToken))
        {
            ModelState.AddModelError(
                $"{nameof(BrandsPageViewModel.NewBrand)}.{nameof(AddBrandFormModel.Name)}",
                "A brand with that name already exists.");
        }

        if (!ModelState.IsValid)
        {
            var vm = await BuildBrandsPageAsync(cancellationToken);
            return View("Brands", new BrandsPageViewModel
            {
                Brands = vm.Brands,
                NewBrand = model,
                OpenAddModal = true
            });
        }

        var maxSort = await _db.CatalogBrands.AnyAsync(cancellationToken)
            ? await _db.CatalogBrands.MaxAsync(b => b.SortOrder, cancellationToken)
            : 0;
        var sort = model.SortOrder ?? maxSort + 10;

        _db.CatalogBrands.Add(new CatalogBrand
        {
            Id = Guid.NewGuid(),
            Name = nameNorm,
            Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim(),
            SortOrder = sort,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);

        TempData["BrandMessage"] = $"Brand “{nameNorm}” was created.";
        return RedirectToAction(nameof(Brands));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteBrand(Guid id, CancellationToken cancellationToken)
    {
        var brand = await _db.CatalogBrands.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (brand is null)
            return RedirectToAction(nameof(Brands));

        var productCount = await _db.CatalogProducts.CountAsync(p => p.BrandId == id, cancellationToken);

        if (productCount > 0)
        {
            TempData["BrandError"] =
                $"Cannot delete “{brand.Name}” — {productCount} product(s) still reference this brand. Clear the brand on those SKUs first.";
            return RedirectToAction(nameof(Brands));
        }

        _db.CatalogBrands.Remove(brand);
        await _db.SaveChangesAsync(cancellationToken);

        TempData["BrandMessage"] = $"Brand “{brand.Name}” was removed.";
        return RedirectToAction(nameof(Brands));
    }

    private async Task<BrandsPageViewModel> BuildBrandsPageAsync(CancellationToken cancellationToken)
    {
        var productBrandIds = await _db.CatalogProducts
            .AsNoTracking()
            .Where(p => p.BrandId != null)
            .Select(p => p.BrandId!.Value)
            .ToListAsync(cancellationToken);

        var counts = new Dictionary<Guid, int>();
        foreach (var bid in productBrandIds)
            counts[bid] = counts.TryGetValue(bid, out var n) ? n + 1 : 1;

        var brands = await _db.CatalogBrands
            .AsNoTracking()
            .OrderBy(b => b.SortOrder)
            .ThenBy(b => b.Name)
            .ToListAsync(cancellationToken);

        var rows = brands
            .Select(b => new BrandRowViewModel
            {
                Id = b.Id,
                Name = b.Name,
                Description = b.Description,
                SortOrder = b.SortOrder,
                ProductCount = counts.TryGetValue(b.Id, out var pc) ? pc : 0
            })
            .ToList();

        return new BrandsPageViewModel { Brands = rows };
    }

    private async Task<CategoriesPageViewModel> BuildCategoriesPageAsync(CancellationToken cancellationToken)
    {
        var productCategoryStrings = await _db.CatalogProducts
            .AsNoTracking()
            .Select(p => p.Category)
            .ToListAsync(cancellationToken);

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in productCategoryStrings)
        {
            var t = (raw ?? string.Empty).Trim();
            if (t.Length == 0)
                continue;

            counts[t] = counts.TryGetValue(t, out var n) ? n + 1 : 1;
        }

        var categories = await _db.CatalogCategories
            .AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var rows = categories
            .Select(c => new CategoryRowViewModel
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                SortOrder = c.SortOrder,
                ProductCount = counts.TryGetValue(c.Name, out var pc) ? pc : 0
            })
            .ToList();

        return new CategoriesPageViewModel { Categories = rows };
    }

    private async Task<List<CatalogCategory>> LoadOrderedCategoriesAsync(CancellationToken cancellationToken) =>
        await _db.CatalogCategories
            .AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

    private async Task<List<CatalogBrand>> LoadOrderedBrandsAsync(CancellationToken cancellationToken) =>
        await _db.CatalogBrands
            .AsNoTracking()
            .OrderBy(b => b.SortOrder)
            .ThenBy(b => b.Name)
            .ToListAsync(cancellationToken);

    private async Task<StockPageViewModel> BuildStockPageViewModelAsync(CancellationToken cancellationToken)
    {
        const int lowThreshold = 5;
        var products = await _db.CatalogProducts
            .AsNoTracking()
            .Include(p => p.Brand)
            .OrderBy(p => p.StockQuantity)
            .ThenBy(p => p.Category)
            .ThenBy(p => p.Name)
            .ToListAsync(cancellationToken);

        var rows = products
            .Select(p => new StockRowViewModel
            {
                ProductId = p.Id,
                Sku = p.Sku,
                Name = p.Name,
                Category = p.Category,
                BrandName = p.Brand?.Name,
                Barcode = p.Barcode,
                StockQuantity = p.StockQuantity
            })
            .ToList();

        return new StockPageViewModel
        {
            LowStockThreshold = lowThreshold,
            Rows = rows
        };
    }

    private async Task<ProductsPageViewModel> BuildProductsPageViewModelAsync(
        AddProductFormModel? newProduct,
        bool openAddProductModal,
        EditProductFormModel? editProduct,
        bool openEditProductModal,
        CancellationToken cancellationToken)
    {
        var products = await _db.CatalogProducts
            .AsNoTracking()
            .Include(p => p.Brand)
            .OrderBy(p => p.Category)
            .ThenBy(p => p.Name)
            .ToListAsync(cancellationToken);

        var options = await LoadOrderedCategoriesAsync(cancellationToken);
        var brandOptions = await LoadOrderedBrandsAsync(cancellationToken);

        decimal? averageMargin = null;
        if (products.Count > 0)
        {
            var marginSamples = products
                .Select(p => ProductCatalogDisplay.MarkupOnCostPercent(p.Price, p.Cost))
                .Where(m => m.HasValue)
                .Select(m => m!.Value)
                .ToList();
            if (marginSamples.Count > 0)
                averageMargin = Math.Round(marginSamples.Average(), 1, MidpointRounding.AwayFromZero);
        }

        return new ProductsPageViewModel
        {
            Products = products,
            AverageMarginPercent = averageMargin,
            CategoryOptions = options,
            BrandOptions = brandOptions,
            NewProduct = newProduct ?? new AddProductFormModel(),
            EditProduct = editProduct,
            OpenAddProductModal = openAddProductModal,
            OpenEditProductModal = openEditProductModal
        };
    }

    public async Task<IActionResult> Stock(CancellationToken cancellationToken)
    {
        ViewData["BodyClass"] = "bk-page-stock";
        ViewData["Title"] = "Stock";
        ViewData["Module"] = "Inventory";
        ViewData["ModuleSubtitle"] = "On-hand counts from your product catalog. Branches and reservations can layer on later.";

        var vm = await BuildStockPageViewModelAsync(cancellationToken);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStock(Guid productId, int stockQuantity, CancellationToken cancellationToken)
    {
        if (stockQuantity < 0 || stockQuantity > 99_999_999)
        {
            TempData["StockError"] = "Stock must be between 0 and 99,999,999.";
            return RedirectToAction(nameof(Stock));
        }

        var product = await _db.CatalogProducts.FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);
        if (product is null)
        {
            TempData["StockError"] = "Product not found.";
            return RedirectToAction(nameof(Stock));
        }

        var previous = product.StockQuantity;
        var result = await _movements.RecordAdjustmentAsync(productId, previous, stockQuantity, null, cancellationToken);

        if (!result.Success)
        {
            TempData["StockError"] = result.Error;
            return RedirectToAction(nameof(Stock));
        }

        var glNote = result.JournalReference is not null
            ? $" Journal {result.JournalReference}."
            : result.GlSkipReason is not null
                ? $" ({result.GlSkipReason})"
                : string.Empty;

        TempData["StockMessage"] =
            $"Stock for “{product.Name}” ({product.Sku}) is now {stockQuantity:N0}. Movement {result.MovementNumber}.{glNote}";
        return RedirectToAction(nameof(Stock));
    }

    public async Task<IActionResult> Warehouses(CancellationToken cancellationToken)
    {
        ViewData["BodyClass"] = "bk-page-warehouses";
        ViewData["Title"] = "Warehouses";
        ViewData["Module"] = "Inventory";
        ViewData["ModuleSubtitle"] = "Define locations now; per-warehouse quantities and transfers can plug in later.";

        var vm = await BuildWarehousesPageAsync(cancellationToken);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddWarehouse(
        [Bind(Prefix = nameof(WarehousesPageViewModel.NewWarehouse))] AddWarehouseFormModel model,
        CancellationToken cancellationToken)
    {
        ViewData["BodyClass"] = "bk-page-warehouses";
        ViewData["Title"] = "Warehouses";
        ViewData["Module"] = "Inventory";
        ViewData["ModuleSubtitle"] = "Define locations now; per-warehouse quantities and transfers can plug in later.";

        var nameNorm = model.Name.Trim();
        if (nameNorm.Length > 0 &&
            await _db.CatalogWarehouses.AnyAsync(
                w => w.Name.ToUpper() == nameNorm.ToUpperInvariant(),
                cancellationToken))
        {
            ModelState.AddModelError(
                $"{nameof(WarehousesPageViewModel.NewWarehouse)}.{nameof(AddWarehouseFormModel.Name)}",
                "A warehouse with that name already exists.");
        }

        var codeNorm = string.IsNullOrWhiteSpace(model.Code) ? null : model.Code.Trim();
        if (codeNorm is not null &&
            await _db.CatalogWarehouses.AnyAsync(
                w => w.Code != null && w.Code.ToUpper() == codeNorm.ToUpperInvariant(),
                cancellationToken))
        {
            ModelState.AddModelError(
                $"{nameof(WarehousesPageViewModel.NewWarehouse)}.{nameof(AddWarehouseFormModel.Code)}",
                "That code is already in use. Choose another code or leave it blank.");
        }

        if (!ModelState.IsValid)
        {
            var vm = await BuildWarehousesPageAsync(cancellationToken);
            return View("Warehouses", new WarehousesPageViewModel
            {
                Warehouses = vm.Warehouses,
                NewWarehouse = model,
                OpenAddModal = true
            });
        }

        var maxSort = await _db.CatalogWarehouses.AnyAsync(cancellationToken)
            ? await _db.CatalogWarehouses.MaxAsync(w => w.SortOrder, cancellationToken)
            : 0;
        var sort = model.SortOrder ?? maxSort + 10;

        _db.CatalogWarehouses.Add(new CatalogWarehouse
        {
            Id = Guid.NewGuid(),
            Name = nameNorm,
            Code = codeNorm,
            Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim(),
            SortOrder = sort,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);

        TempData["WarehouseMessage"] = $"Warehouse “{nameNorm}” was created.";
        return RedirectToAction(nameof(Warehouses));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteWarehouse(Guid id, CancellationToken cancellationToken)
    {
        var warehouse = await _db.CatalogWarehouses.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
        if (warehouse is null)
            return RedirectToAction(nameof(Warehouses));

        _db.CatalogWarehouses.Remove(warehouse);
        await _db.SaveChangesAsync(cancellationToken);

        TempData["WarehouseMessage"] = $"Warehouse “{warehouse.Name}” was removed.";
        return RedirectToAction(nameof(Warehouses));
    }

    private async Task<WarehousesPageViewModel> BuildWarehousesPageAsync(CancellationToken cancellationToken)
    {
        var rows = await _db.CatalogWarehouses
            .AsNoTracking()
            .OrderBy(w => w.SortOrder)
            .ThenBy(w => w.Name)
            .Select(w => new WarehouseRowViewModel
            {
                Id = w.Id,
                Name = w.Name,
                Code = w.Code,
                Description = w.Description,
                SortOrder = w.SortOrder
            })
            .ToListAsync(cancellationToken);

        return new WarehousesPageViewModel { Warehouses = rows };
    }

    public async Task<IActionResult> PurchaseOrders(CancellationToken cancellationToken)
    {
        ViewData["BodyClass"] = "bk-po-page";
        ViewData["Title"] = "Purchase orders";
        ViewData["Module"] = "Inventory";
        ViewData["ModuleSubtitle"] = "Draft supplier orders, add lines, submit, then receive into catalog stock.";

        var vm = await BuildPurchaseOrdersPageAsync(cancellationToken);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePurchaseOrder(
        [Bind(Prefix = nameof(PurchaseOrdersPageViewModel.NewOrder))] CreatePurchaseOrderFormModel model,
        CancellationToken cancellationToken)
    {
        ViewData["BodyClass"] = "bk-po-page";
        ViewData["Title"] = "Purchase orders";
        ViewData["Module"] = "Inventory";
        ViewData["ModuleSubtitle"] = "Draft supplier orders, add lines, submit, then receive into catalog stock.";

        if (!ModelState.IsValid)
        {
            var list = await BuildPurchaseOrdersPageAsync(cancellationToken);
            return View("PurchaseOrders", new PurchaseOrdersPageViewModel
            {
                Orders = list.Orders,
                SupplierOptions = list.SupplierOptions,
                NewOrder = model,
                OpenCreateModal = true
            });
        }

        if (model.SupplierId is Guid postedSupplierId && postedSupplierId != Guid.Empty &&
            !await _db.CatalogSuppliers.AnyAsync(s => s.Id == postedSupplierId, cancellationToken))
        {
            ModelState.AddModelError(
                $"{nameof(PurchaseOrdersPageViewModel.NewOrder)}.{nameof(CreatePurchaseOrderFormModel.SupplierId)}",
                "That supplier is no longer in the directory.");
            var listBad = await BuildPurchaseOrdersPageAsync(cancellationToken);
            return View("PurchaseOrders", new PurchaseOrdersPageViewModel
            {
                Orders = listBad.Orders,
                SupplierOptions = listBad.SupplierOptions,
                NewOrder = model,
                OpenCreateModal = true
            });
        }

        var poNumber = await AllocatePoNumberAsync(cancellationToken);
        DateTimeOffset? expected = model.ExpectedDate is { } d
            ? new DateTimeOffset(d.Date, TimeSpan.Zero)
            : null;

        Guid? supplierId = null;
        string supplierName;
        if (model.SupplierId is Guid sid && sid != Guid.Empty)
        {
            var sup = await _db.CatalogSuppliers.FirstAsync(s => s.Id == sid, cancellationToken);
            supplierId = sup.Id;
            supplierName = sup.Name;
        }
        else
        {
            supplierName = model.SupplierName!.Trim();
        }

        var po = new PurchaseOrder
        {
            Id = Guid.NewGuid(),
            PoNumber = poNumber,
            SupplierId = supplierId,
            SupplierName = supplierName,
            Status = PurchaseOrderStatus.Draft,
            Notes = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim(),
            ExpectedAtUtc = expected,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync(cancellationToken);

        TempData["PoMessage"] = $"Purchase order {poNumber} was created.";
        return RedirectToAction(nameof(PurchaseOrderDetail), new { id = po.Id });
    }

    public async Task<IActionResult> PurchaseOrderDetail(Guid id, CancellationToken cancellationToken)
    {
        ViewData["BodyClass"] = "bk-po-detail-page";
        ViewData["Title"] = "Purchase order";
        ViewData["Module"] = "Inventory";
        ViewData["ModuleSubtitle"] = "Lines, submit for receiving, then post receipts to stock.";

        var vm = await BuildPurchaseOrderDetailAsync(id, null, false, cancellationToken);
        if (vm is null)
            return NotFound();

        ViewData["Title"] = $"PO {vm.PoNumber}";
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPurchaseOrderLine(
        Guid purchaseOrderId,
        [Bind(Prefix = nameof(PurchaseOrderDetailViewModel.NewLine))] AddPurchaseOrderLineFormModel model,
        CancellationToken cancellationToken)
    {
        var po = await _db.PurchaseOrders.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == purchaseOrderId, cancellationToken);
        if (po is null || po.Status != PurchaseOrderStatus.Draft)
            return RedirectToAction(nameof(PurchaseOrders));

        if (!ModelState.IsValid)
        {
            var vm = await BuildPurchaseOrderDetailAsync(purchaseOrderId, model, true, cancellationToken);
            return vm is null ? NotFound() : View("PurchaseOrderDetail", vm);
        }

        if (model.CatalogProductId == Guid.Empty)
        {
            ModelState.AddModelError(
                $"{nameof(PurchaseOrderDetailViewModel.NewLine)}.{nameof(AddPurchaseOrderLineFormModel.CatalogProductId)}",
                "Please select a product.");
            var vmEmpty = await BuildPurchaseOrderDetailAsync(purchaseOrderId, model, true, cancellationToken);
            return vmEmpty is null ? NotFound() : View("PurchaseOrderDetail", vmEmpty);
        }

        if (!await _db.CatalogProducts.AnyAsync(p => p.Id == model.CatalogProductId, cancellationToken))
        {
            ModelState.AddModelError(
                $"{nameof(PurchaseOrderDetailViewModel.NewLine)}.{nameof(AddPurchaseOrderLineFormModel.CatalogProductId)}",
                "Select a valid product.");
            var vmErr = await BuildPurchaseOrderDetailAsync(purchaseOrderId, model, true, cancellationToken);
            return vmErr is null ? NotFound() : View("PurchaseOrderDetail", vmErr);
        }

        if (po.Lines.Any(l => l.CatalogProductId == model.CatalogProductId))
        {
            ModelState.AddModelError(
                $"{nameof(PurchaseOrderDetailViewModel.NewLine)}.{nameof(AddPurchaseOrderLineFormModel.CatalogProductId)}",
                "That product is already on this PO. Remove the line first to change quantities.");
            var vmDup = await BuildPurchaseOrderDetailAsync(purchaseOrderId, model, true, cancellationToken);
            return vmDup is null ? NotFound() : View("PurchaseOrderDetail", vmDup);
        }

        _db.PurchaseOrderLines.Add(new PurchaseOrderLine
        {
            Id = Guid.NewGuid(),
            PurchaseOrderId = po.Id,
            CatalogProductId = model.CatalogProductId,
            QuantityOrdered = model.QuantityOrdered,
            UnitCost = decimal.Round(model.UnitCost, 2, MidpointRounding.AwayFromZero),
            QuantityReceived = 0
        });

        po.ModifiedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        TempData["PoMessage"] = "Line added.";
        return RedirectToAction(nameof(PurchaseOrderDetail), new { id = purchaseOrderId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePurchaseOrderLine(Guid lineId, CancellationToken cancellationToken)
    {
        var line = await _db.PurchaseOrderLines.Include(l => l.PurchaseOrder).FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);
        if (line is null)
            return RedirectToAction(nameof(PurchaseOrders));

        var poId = line.PurchaseOrderId;
        if (line.PurchaseOrder.Status != PurchaseOrderStatus.Draft)
            return RedirectToAction(nameof(PurchaseOrderDetail), new { id = poId });

        _db.PurchaseOrderLines.Remove(line);
        line.PurchaseOrder.ModifiedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        TempData["PoMessage"] = "Line removed.";
        return RedirectToAction(nameof(PurchaseOrderDetail), new { id = poId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitPurchaseOrder(Guid id, CancellationToken cancellationToken)
    {
        var po = await _db.PurchaseOrders.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (po is null || po.Status != PurchaseOrderStatus.Draft)
            return RedirectToAction(nameof(PurchaseOrders));

        if (po.Lines.Count == 0)
        {
            TempData["PoError"] = "Add at least one line before submitting this PO.";
            return RedirectToAction(nameof(PurchaseOrderDetail), new { id });
        }

        po.Status = PurchaseOrderStatus.Open;
        po.ModifiedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        TempData["PoMessage"] = "PO submitted — you can now receive against open lines.";
        return RedirectToAction(nameof(PurchaseOrderDetail), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelPurchaseOrder(Guid id, CancellationToken cancellationToken)
    {
        var po = await _db.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (po is null)
            return RedirectToAction(nameof(PurchaseOrders));

        if (po.Status != PurchaseOrderStatus.Draft)
            return RedirectToAction(nameof(PurchaseOrderDetail), new { id });

        po.Status = PurchaseOrderStatus.Cancelled;
        po.ModifiedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        TempData["PoMessage"] = $"PO {po.PoNumber} was cancelled.";
        return RedirectToAction(nameof(PurchaseOrderDetail), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePurchaseOrder(Guid id, CancellationToken cancellationToken)
    {
        var po = await _db.PurchaseOrders.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (po is null || po.Status != PurchaseOrderStatus.Draft)
            return RedirectToAction(nameof(PurchaseOrders));

        _db.PurchaseOrders.Remove(po);
        await _db.SaveChangesAsync(cancellationToken);

        TempData["PoMessage"] = $"Draft PO {po.PoNumber} was deleted.";
        return RedirectToAction(nameof(PurchaseOrders));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReceivePurchaseOrderLine(Guid lineId, int quantity, CancellationToken cancellationToken)
    {
        if (quantity <= 0 || quantity > 99_999_999)
        {
            TempData["PoError"] = "Receive quantity must be between 1 and 99,999,999.";
            var lineBad = await _db.PurchaseOrderLines.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);
            return lineBad is null
                ? RedirectToAction(nameof(PurchaseOrders))
                : RedirectToAction(nameof(PurchaseOrderDetail), new { id = lineBad.PurchaseOrderId });
        }

        var line = await _db.PurchaseOrderLines
            .Include(l => l.PurchaseOrder)
            .ThenInclude(p => p.Lines)
            .Include(l => l.CatalogProduct)
            .FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);

        if (line is null)
            return RedirectToAction(nameof(PurchaseOrders));

        var po = line.PurchaseOrder;
        if (po.Status != PurchaseOrderStatus.Open)
        {
            TempData["PoError"] = "Receiving is only allowed while the PO is open.";
            return RedirectToAction(nameof(PurchaseOrderDetail), new { id = po.Id });
        }

        var remaining = line.QuantityOrdered - line.QuantityReceived;
        if (quantity > remaining)
        {
            TempData["PoError"] = $"Cannot receive more than remaining ({remaining}) for this line.";
            return RedirectToAction(nameof(PurchaseOrderDetail), new { id = po.Id });
        }

        line.QuantityReceived += quantity;
        po.ModifiedAtUtc = DateTimeOffset.UtcNow;

        if (po.Lines.All(l => l.QuantityReceived >= l.QuantityOrdered))
            po.Status = PurchaseOrderStatus.Received;

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);

            var move = await _movements.RecordAsync(new InventoryMovementCommand
            {
                Type = InventoryMovementType.Purchase,
                CatalogProductId = line.CatalogProductId,
                Quantity = quantity,
                UnitCost = line.UnitCost,
                SourceReference = po.PoNumber,
                Notes = $"PO line receive"
            }, cancellationToken);

            if (!move.Success)
            {
                await tx.RollbackAsync(cancellationToken);
                TempData["PoError"] = move.Error;
                return RedirectToAction(nameof(PurchaseOrderDetail), new { id = po.Id });
            }

            await tx.CommitAsync(cancellationToken);

            var msg = po.Status == PurchaseOrderStatus.Received
                ? "Receipt posted — PO fully received and closed."
                : $"Received {quantity:N0} unit(s) into stock.";

            msg += $" Movement {move.MovementNumber}.";
            if (move.JournalReference is not null)
                msg += $" GL {move.JournalReference}.";
            else if (!string.IsNullOrWhiteSpace(move.GlSkipReason))
                msg += $" ({move.GlSkipReason})";

            TempData["PoMessage"] = msg;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }

        return RedirectToAction(nameof(PurchaseOrderDetail), new { id = po.Id });
    }

    private async Task<string> AllocatePoNumberAsync(CancellationToken cancellationToken)
    {
        var prefix = $"PO-{DateTime.UtcNow:yyyyMMdd}-";
        var existing = await _db.PurchaseOrders
            .AsNoTracking()
            .Where(p => p.PoNumber.StartsWith(prefix))
            .Select(p => p.PoNumber)
            .ToListAsync(cancellationToken);

        var max = 0;
        foreach (var pn in existing)
        {
            if (pn.Length <= prefix.Length)
                continue;
            var suffix = pn[prefix.Length..];
            if (int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > max)
                max = n;
        }

        return $"{prefix}{(max + 1):D4}";
    }

    private async Task<PurchaseOrdersPageViewModel> BuildPurchaseOrdersPageAsync(CancellationToken cancellationToken)
    {
        var pos = await _db.PurchaseOrders
            .AsNoTracking()
            .Include(p => p.Lines)
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var rows = pos
            .Select(p => new PurchaseOrderListRowViewModel
            {
                Id = p.Id,
                PoNumber = p.PoNumber,
                SupplierName = p.SupplierName,
                Status = p.Status,
                LineCount = p.Lines.Count,
                LineTotal = p.Lines.Sum(l => l.QuantityOrdered * l.UnitCost),
                CreatedAtUtc = p.CreatedAtUtc
            })
            .ToList();

        return new PurchaseOrdersPageViewModel
        {
            Orders = rows,
            SupplierOptions = await LoadCatalogSupplierOptionsAsync(cancellationToken)
        };
    }

    private async Task<IReadOnlyList<CatalogSupplierOption>> LoadCatalogSupplierOptionsAsync(CancellationToken cancellationToken) =>
        await _db.CatalogSuppliers
            .AsNoTracking()
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Name)
            .Select(s => new CatalogSupplierOption { Id = s.Id, Name = s.Name, Code = s.Code })
            .ToListAsync(cancellationToken);

    private async Task<PurchaseOrderDetailViewModel?> BuildPurchaseOrderDetailAsync(
        Guid purchaseOrderId,
        AddPurchaseOrderLineFormModel? newLine,
        bool openAddLineModal,
        CancellationToken cancellationToken)
    {
        var po = await _db.PurchaseOrders
            .AsNoTracking()
            .Include(p => p.Supplier)
            .Include(p => p.Lines)
            .ThenInclude(l => l.CatalogProduct)
            .FirstOrDefaultAsync(p => p.Id == purchaseOrderId, cancellationToken);

        if (po is null)
            return null;

        var lineRows = po.Lines
            .OrderBy(l => l.CatalogProduct.Sku)
            .Select(l => new PurchaseOrderLineRowViewModel
            {
                LineId = l.Id,
                CatalogProductId = l.CatalogProductId,
                ProductSku = l.CatalogProduct.Sku,
                ProductName = l.CatalogProduct.Name,
                QuantityOrdered = l.QuantityOrdered,
                UnitCost = l.UnitCost,
                QuantityReceived = l.QuantityReceived
            })
            .ToList();

        var productOptions = await _db.CatalogProducts
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new CatalogProductOption
            {
                Id = p.Id,
                Sku = p.Sku,
                Name = p.Name
            })
            .ToListAsync(cancellationToken);

        return new PurchaseOrderDetailViewModel
        {
            PurchaseOrderId = po.Id,
            PoNumber = po.PoNumber,
            SupplierName = po.SupplierName,
            UsesDirectorySupplier = po.SupplierId.HasValue,
            SupplierDirectoryCode = po.Supplier?.Code,
            Status = po.Status,
            Notes = po.Notes,
            ExpectedAtUtc = po.ExpectedAtUtc,
            CreatedAtUtc = po.CreatedAtUtc,
            Lines = lineRows,
            ProductOptions = productOptions,
            NewLine = newLine ?? new AddPurchaseOrderLineFormModel(),
            OpenAddLineModal = openAddLineModal
        };
    }

    public async Task<IActionResult> Suppliers(CancellationToken cancellationToken)
    {
        ViewData["BodyClass"] = "bk-suppliers-page";
        ViewData["Title"] = "Suppliers";
        ViewData["Module"] = "Inventory";
        ViewData["ModuleSubtitle"] = "Vendor directory — pick these when creating purchase orders, or type a one-off name.";

        var vm = await BuildSuppliersPageAsync(cancellationToken);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddSupplier(
        [Bind(Prefix = nameof(SuppliersPageViewModel.NewSupplier))] AddSupplierFormModel model,
        CancellationToken cancellationToken)
    {
        ViewData["BodyClass"] = "bk-suppliers-page";
        ViewData["Title"] = "Suppliers";
        ViewData["Module"] = "Inventory";
        ViewData["ModuleSubtitle"] = "Vendor directory — pick these when creating purchase orders, or type a one-off name.";

        var nameNorm = model.Name.Trim();
        if (nameNorm.Length > 0 &&
            await _db.CatalogSuppliers.AnyAsync(
                s => s.Name.ToUpper() == nameNorm.ToUpperInvariant(),
                cancellationToken))
        {
            ModelState.AddModelError(
                $"{nameof(SuppliersPageViewModel.NewSupplier)}.{nameof(AddSupplierFormModel.Name)}",
                "A supplier with that name already exists.");
        }

        var codeNorm = string.IsNullOrWhiteSpace(model.Code) ? null : model.Code.Trim();
        if (codeNorm is not null &&
            await _db.CatalogSuppliers.AnyAsync(
                s => s.Code != null && s.Code.ToUpper() == codeNorm.ToUpperInvariant(),
                cancellationToken))
        {
            ModelState.AddModelError(
                $"{nameof(SuppliersPageViewModel.NewSupplier)}.{nameof(AddSupplierFormModel.Code)}",
                "That code is already in use. Choose another code or leave it blank.");
        }

        if (!ModelState.IsValid)
        {
            var vm = await BuildSuppliersPageAsync(cancellationToken);
            return View("Suppliers", new SuppliersPageViewModel
            {
                Suppliers = vm.Suppliers,
                NewSupplier = model,
                OpenAddModal = true
            });
        }

        var emailNorm = string.IsNullOrWhiteSpace(model.Email) ? null : model.Email.Trim();

        var maxSort = await _db.CatalogSuppliers.AnyAsync(cancellationToken)
            ? await _db.CatalogSuppliers.MaxAsync(s => s.SortOrder, cancellationToken)
            : 0;
        var sort = model.SortOrder ?? maxSort + 10;

        _db.CatalogSuppliers.Add(new CatalogSupplier
        {
            Id = Guid.NewGuid(),
            Name = nameNorm,
            Code = codeNorm,
            Phone = string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim(),
            Email = emailNorm,
            Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim(),
            SortOrder = sort,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);

        TempData["SupplierMessage"] = $"Supplier “{nameNorm}” was added.";
        return RedirectToAction(nameof(Suppliers));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSupplier(Guid id, CancellationToken cancellationToken)
    {
        var supplier = await _db.CatalogSuppliers.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (supplier is null)
            return RedirectToAction(nameof(Suppliers));

        var poCount = await _db.PurchaseOrders.CountAsync(p => p.SupplierId == id, cancellationToken);
        if (poCount > 0)
        {
            TempData["SupplierError"] =
                $"Cannot delete “{supplier.Name}” — {poCount} purchase order(s) reference this supplier. Unlink or archive those POs first.";
            return RedirectToAction(nameof(Suppliers));
        }

        _db.CatalogSuppliers.Remove(supplier);
        await _db.SaveChangesAsync(cancellationToken);

        TempData["SupplierMessage"] = $"Supplier “{supplier.Name}” was removed.";
        return RedirectToAction(nameof(Suppliers));
    }

    private async Task<SuppliersPageViewModel> BuildSuppliersPageAsync(CancellationToken cancellationToken)
    {
        var poCounts = await _db.PurchaseOrders
            .AsNoTracking()
            .Where(p => p.SupplierId != null)
            .GroupBy(p => p.SupplierId!.Value)
            .Select(g => new { SupplierId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var countMap = poCounts.ToDictionary(x => x.SupplierId, x => x.Count);

        var suppliers = await _db.CatalogSuppliers
            .AsNoTracking()
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Name)
            .ToListAsync(cancellationToken);

        var rows = suppliers
            .Select(s => new SupplierRowViewModel
            {
                Id = s.Id,
                Name = s.Name,
                Code = s.Code,
                Phone = s.Phone,
                Email = s.Email,
                Description = s.Description,
                SortOrder = s.SortOrder,
                PurchaseOrderCount = countMap.TryGetValue(s.Id, out var c) ? c : 0
            })
            .ToList();

        return new SuppliersPageViewModel { Suppliers = rows };
    }

    [HttpGet]
    public async Task<IActionResult> Movements(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Inventory movements";
        ViewData["Module"] = "Inventory";
        ViewData["ModuleSubtitle"] = "Every stock in/out is logged with automatic GL journals where applicable.";

        return View(await BuildMovementsPageAsync(cancellationToken, null));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateMovement(
        [Bind(Prefix = "Form")] CreateInventoryMovementFormModel model,
        CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Inventory movements";
        ViewData["Module"] = "Inventory";
        ViewData["ModuleSubtitle"] = "Every stock in/out is logged with automatic GL journals where applicable.";

        if (!ModelState.IsValid)
            return View("Movements", await BuildMovementsPageAsync(cancellationToken, model));

        if (model.Type is InventoryMovementType.Purchase or InventoryMovementType.Sale)
        {
            ModelState.AddModelError("Form.Type", "Use Purchase orders or POS for purchases and sales.");
            return View("Movements", await BuildMovementsPageAsync(cancellationToken, model));
        }

        var result = await _movements.RecordAsync(new InventoryMovementCommand
        {
            Type = model.Type,
            CatalogProductId = model.CatalogProductId,
            Quantity = model.Quantity,
            UnitCost = model.UnitCost,
            FromWarehouseId = model.FromWarehouseId,
            ToWarehouseId = model.ToWarehouseId,
            RefundAmount = model.RefundAmount,
            SourceReference = model.SourceReference,
            Notes = model.Notes
        }, cancellationToken);

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Could not record movement.");
            return View("Movements", await BuildMovementsPageAsync(cancellationToken, model));
        }

        var gl = result.JournalReference is not null
            ? $" · GL {result.JournalReference}"
            : result.GlSkipReason is not null
                ? $" ({result.GlSkipReason})"
                : string.Empty;

        TempData["MovementMessage"] = $"Recorded {InventoryMovementLabels.For(model.Type)} — {result.MovementNumber}{gl}.";
        return RedirectToAction(nameof(Movements));
    }

    private async Task<InventoryMovementsPageViewModel> BuildMovementsPageAsync(
        CancellationToken cancellationToken,
        CreateInventoryMovementFormModel? form)
    {
        var movements = await _db.InventoryMovements.AsNoTracking()
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(80)
            .Select(m => new
            {
                m.Id,
                m.MovementNumber,
                m.Type,
                m.CreatedAtUtc,
                m.Quantity,
                m.QuantityDelta,
                m.TotalCost,
                m.JournalReference,
                m.GlSkipReason,
                ProductName = m.ProductName ?? (m.CatalogProduct != null ? m.CatalogProduct.Name : "Removed product"),
                Sku = m.ProductSku ?? (m.CatalogProduct != null ? m.CatalogProduct.Sku : "—")
            })
            .ToListAsync(cancellationToken);

        var rows = movements.Select(m => new InventoryMovementRowViewModel
        {
            Id = m.Id,
            MovementNumber = m.MovementNumber,
            Type = m.Type,
            TypeLabel = InventoryMovementLabels.For(m.Type),
            CreatedAtUtc = m.CreatedAtUtc,
            ProductName = m.ProductName,
            Sku = m.Sku,
            Quantity = m.Quantity,
            QuantityDelta = m.QuantityDelta,
            TotalCost = m.TotalCost,
            JournalReference = m.JournalReference,
            GlSkipReason = m.GlSkipReason
        }).ToList();

        var products = await _db.CatalogProducts.AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new ProductPickOptionViewModel
            {
                Id = p.Id,
                Label = $"{p.Sku} — {p.Name}",
                StockQuantity = p.StockQuantity,
                Cost = p.Cost
            })
            .ToListAsync(cancellationToken);

        var warehouses = await _db.CatalogWarehouses.AsNoTracking()
            .OrderBy(w => w.SortOrder)
            .ThenBy(w => w.Name)
            .Select(w => new WarehousePickOptionViewModel
            {
                Id = w.Id,
                Label = string.IsNullOrWhiteSpace(w.Code) ? w.Name : $"{w.Code} — {w.Name}"
            })
            .ToListAsync(cancellationToken);

        return new InventoryMovementsPageViewModel
        {
            RecentMovements = rows,
            Products = products,
            Warehouses = warehouses,
            Form = form ?? new CreateInventoryMovementFormModel()
        };
    }
}
