using ContentEmpireSystem.Core;
using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Enums;
using ContentEmpireSystem.Core.Interfaces;
using ContentEmpireSystem.Infrastructure.Data;
using ContentEmpireSystem.Infrastructure.Services.Local;
using Microsoft.EntityFrameworkCore;

namespace ContentEmpireSystem.Infrastructure.Services;

public class VideoProjectService(
    ApplicationDbContext db,
    IProjectWorkflowBuilder workflowBuilder,
    IOutputPathService paths,
    ISystemSettingsService settings) : IVideoProjectService
{
    public async Task<List<VideoProjectDto>> GetQueueAsync(VideoProjectStatus? status = null, CancellationToken ct = default)
    {
        var query = db.VideoProjects
            .Include(p => p.GeneratedImages)
            .AsNoTracking()
            .OrderByDescending(p => p.UpdatedAt)
            .AsQueryable();

        if (status.HasValue)
            query = query.Where(p => p.Status == status.Value);

        var projects = await query.Take(100).ToListAsync(ct);
        var profile = await DeploymentProfileSource.ResolveAsync(settings, ct);
        var videoSupported = DeploymentProfileResolver.SupportsVideoGeneration(profile);
        var result = new List<VideoProjectDto>(projects.Count);
        foreach (var project in projects)
        {
            var dto = VideoProjectMapper.Map(project, paths);
            dto.Workflow = workflowBuilder.Build(project, videoSupported);
            result.Add(dto);
        }

        return result;
    }

    public async Task<VideoProjectDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var project = await db.VideoProjects
            .Include(p => p.GeneratedImages)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (project is null)
            return null;

        var dto = VideoProjectMapper.Map(project, paths);
        dto.Workflow = await workflowBuilder.BuildAsync(project, ct);
        return dto;
    }

    public async Task<VideoProjectDto> SetContentLanguageAsync(Guid projectId, string language, CancellationToken ct = default)
    {
        var project = await db.VideoProjects
            .Include(p => p.GeneratedImages)
            .FirstAsync(p => p.Id == projectId, ct);

        project.ContentLanguage = ContentLanguageHelper.Normalize(language);
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var dto = VideoProjectMapper.Map(project, paths);
        dto.Workflow = await workflowBuilder.BuildAsync(project, ct);
        return dto;
    }
}
