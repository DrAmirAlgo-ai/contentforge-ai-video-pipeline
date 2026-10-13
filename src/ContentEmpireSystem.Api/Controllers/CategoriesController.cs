using ContentEmpireSystem.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContentEmpireSystem.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class CategoriesController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var categories = await db.ContentCategories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Id)
            .Select(c => new
            {
                id = c.Id,
                categoryType = c.CategoryType.ToString(),
                nameAr = c.NameAr,
                nameEn = c.NameEn,
                description = c.Description,
            })
            .ToListAsync(ct);

        return Ok(categories);
    }
}
