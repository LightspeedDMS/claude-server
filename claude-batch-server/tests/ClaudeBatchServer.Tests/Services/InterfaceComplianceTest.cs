using ClaudeBatchServer.Core.Services;

namespace ClaudeBatchServer.Tests.Services;

/// <summary>
/// Simple test to verify interface compliance for staging area methods
/// This ensures the interface and implementation are in sync
/// </summary>
public class InterfaceComplianceTest
{
    [Fact]
    public void JobPersistenceService_ImplementsAllInterfaceMethods()
    {
        // This test will compile only if JobPersistenceService implements all IJobPersistenceService methods
        // including the new staging area methods
        
        var serviceType = typeof(JobPersistenceService);
        var interfaceType = typeof(IJobPersistenceService);
        
        // Verify the service implements the interface
        Assert.True(interfaceType.IsAssignableFrom(serviceType));
        
        // Verify all interface methods are implemented
        var interfaceMethods = interfaceType.GetMethods();
        foreach (var interfaceMethod in interfaceMethods)
        {
            var implementationMethod = serviceType.GetMethod(interfaceMethod.Name, 
                interfaceMethod.GetParameters().Select(p => p.ParameterType).ToArray());
            
            Assert.NotNull(implementationMethod);
            Assert.Equal(interfaceMethod.ReturnType, implementationMethod.ReturnType);
        }
    }
    
    [Fact]
    public void StagingAreaMethods_ExistInInterface()
    {
        var interfaceType = typeof(IJobPersistenceService);
        
        // Verify staging area methods are defined in interface
        Assert.NotNull(interfaceType.GetMethod("GetJobStagingPath"));
        Assert.NotNull(interfaceType.GetMethod("GetStagedFiles"));
        Assert.NotNull(interfaceType.GetMethod("CopyStagedFilesToCowWorkspaceAsync"));
        Assert.NotNull(interfaceType.GetMethod("CleanupStagingDirectory"));
    }
}