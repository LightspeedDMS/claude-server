using ClaudeBatchServer.Core.Services;

namespace ClaudeBatchServer.Api;

public class JobQueueHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<JobQueueHostedService> _logger;

    public JobQueueHostedService(IServiceProvider serviceProvider, ILogger<JobQueueHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Job Queue Hosted Service started");

        // Initialize JobService with persisted jobs for crash recovery
        try
        {
            using var initScope = _serviceProvider.CreateScope();
            var jobService = initScope.ServiceProvider.GetRequiredService<IJobService>();
            await jobService.InitializeAsync();
            _logger.LogInformation("JobService initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize JobService");
            return; // Don't continue if initialization fails
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var jobService = scope.ServiceProvider.GetRequiredService<IJobService>();
                
                await jobService.ProcessJobQueueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Job Queue Hosted Service is stopping");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Job Queue Hosted Service");
                await Task.Delay(5000, stoppingToken);
            }
        }

        _logger.LogInformation("Job Queue Hosted Service stopped");
    }
}