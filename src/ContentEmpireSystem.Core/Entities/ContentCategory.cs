using ContentEmpireSystem.Core.Enums;

namespace ContentEmpireSystem.Core.Entities;

public class ContentCategory
{
    public int Id { get; set; }
    public ContentCategoryType CategoryType { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public ICollection<VideoProject> VideoProjects { get; set; } = [];
}
