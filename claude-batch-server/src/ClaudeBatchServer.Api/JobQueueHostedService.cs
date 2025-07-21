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