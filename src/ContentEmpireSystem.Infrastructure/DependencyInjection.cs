using System.Net.Http;
using ContentEmpireSystem.Core.Entities;
using ContentEmpireSystem.Core.Interfaces;
using ContentEmpireSystem.Infrastructure.Auth;
using ContentEmpireSystem.Infrastructure.Data;
using ContentEmpireSystem.Infrastructure.Services;
using ContentEmpireSystem.Infrastructure.Services.Ai;
using ContentEmpireSystem.Infrastructure.Services.Local;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ContentEmpireSystem.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, string contentRootPath, IConfiguration configuration)
    {
        var (databaseOptions, connectionString) = DatabaseConnectionResolver.Resolve(configuration, contentRootPath);
        services.AddSingleton(databaseOptions);
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
        services.AddHttpContextAccessor();
        services.AddScoped<IBackgroundJobUserContext, BackgroundJobUserContext>();
        services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
        services.AddSingleton<JwtTokenService>();
        services.AddScoped<IAuthService, AuthService>();

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredLength = 8;
                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager();

        services.AddSingleton<IDatabaseBootstrapState, DatabaseBootstrapState>();
        services.AddHostedService<DatabaseBootstrapHostedService>();
        services.Configure<WorkloadOptions>(configuration.GetSection("Workload"));
        services.AddSingleton<IWorkloadGate, WorkloadGate>();
        services.AddSingleton<IProjectOperationCoordinator, ProjectOperationCoordinator>();
        services.AddHostedService<ProjectOperationStartupCleanupHostedService>();
        services.AddScoped<IProjectWorkflowBuilder, ProjectWorkflowBuilder>();
        services.AddSingleton<ProductionQueueHostedService>();
        services.AddSingleton<IProductionQueue>(sp => sp.GetRequiredService<ProductionQueueHostedService>());
        services.AddHostedService(sp => sp.GetRequiredService<ProductionQueueHostedService>());
        services.AddSingleton<ImageGenerationQueueHostedService>();
        services.AddSingleton<IImageGenerationQueue>(sp => sp.GetRequiredService<ImageGenerationQueueHostedService>());
        services.AddHostedService(sp => sp.GetRequiredService<ImageGenerationQueueHostedService>());
        services.AddSingleton<VideoGenerationQueueHostedService>();
        services.AddSingleton<IVideoGenerationQueue>(sp => sp.GetRequiredService<VideoGenerationQueueHostedService>());
        services.AddHostedService(sp => sp.GetRequiredService<VideoGenerationQueueHostedService>());
        services.AddSingleton<AiLlmGenerationQueueHostedService>();
        services.AddSingleton<IAiLlmGenerationQueue>(sp => sp.GetRequiredService<AiLlmGenerationQueueHostedService>());
        services.AddHostedService(sp => sp.GetRequiredService<AiLlmGenerationQueueHostedService>());
        services.AddSingleton<PipelineOrchestrationQueueHostedService>();
        services.AddSingleton<IPipelineOrchestrationQueue>(sp => sp.GetRequiredService<PipelineOrchestrationQueueHostedService>());
        services.AddHostedService(sp => sp.GetRequiredService<PipelineOrchestrationQueueHostedService>());

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));

            if (databaseOptions.IsSqlServer)
            {
                options.UseSqlServer(connectionString, sql =>
                {
                    sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null);
                    sql.CommandTimeout(30);
                });
            }
            else
            {
                options.UseSqlite(connectionString, sqlite => sqlite.CommandTimeout(30));
                options.AddInterceptors(new SqlitePerformanceInterceptor());
            }
        });

        services.AddSingleton<IOutputPathService>(_ => new OutputPathService(contentRootPath));
        services.AddSingleton<IOperationFileLogger, OperationFileLogger>();
        services.AddHttpClient("HuggingFaceImage", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        });
        services.AddHttpClient("HuggingFaceSpace", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(15);
        });
        services.AddHttpClient("ComfyUI", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(60);
        });
        services.AddHttpClient("AiScript", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(12);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(20),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        });
        services.AddHttpClient("AiHealth", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(6)
        });
        services.AddHttpClient("LMStudio", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddHttpClient();
        services.AddSingleton<SystemActivityService>();
        services.AddSingleton<ISystemActivityService>(sp => sp.GetRequiredService<SystemActivityService>());
        services.AddSingleton<IdempotencyCache>();
        services.AddSingleton<ILocalGpuOrchestrator, LocalGpuOrchestrator>();
        services.AddScoped<ILocalQwenConnector, LocalQwenConnector>();
        services.AddHostedService<LocalQwenAutoConnectHostedService>();
        services.AddScoped<LmStudioGpuService>();
        services.AddScoped<LocalEndpointResolver>();
        services.AddSingleton<ComfyUiRestartState>();
        services.AddScoped<IComfyUiProcessManager, ComfyUiProcessManager>();
        services.AddScoped<ComfyUiClient>();
        services.AddScoped<OpenAiCompatibleClient>();
        services.AddScoped<ILocalAiHealthService, LocalAiHealthService>();
        services.AddScoped<ISystemSettingsService, SystemSettingsService>();
        services.AddScoped<AiProviderResolver>();
        services.AddScoped<AiChatService>();
        services.AddScoped<IVideoProjectService, VideoProjectService>();
        services.AddScoped<IIdeaGeneratorService, IdeaGeneratorService>();
        services.AddScoped<IScriptGeneratorService, ScriptGeneratorService>();
        services.AddScoped<IImageGeneratorService, ImageGeneratorService>();
        services.AddScoped<IVideoGeneratorService, VideoGeneratorService>();
        services.AddScoped<ITextToSpeechService, TextToSpeechService>();
        services.AddScoped<IFfmpegVideoService, FfmpegVideoService>();
        services.AddScoped<IVideoProductionPipeline, VideoProductionPipeline>();
        services.AddScoped<IVoiceCodeService, VoiceCodeService>();

        return services;
    }
}
