using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeServerCLI.Models;
using ClaudeBatchServer.Core.DTOs;

namespace ClaudeServerCLI.Serialization;

/// <summary>
/// JSON serialization context for trim-safe serialization in CLI
/// This eliminates IL2026 warnings when using --self-contained publishing
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CliConfiguration))]
[JsonSerializable(typeof(ProfileConfiguration))]
[JsonSerializable(typeof(Dictionary<string, ProfileConfiguration>))]
[JsonSerializable(typeof(JobInfo))]
[JsonSerializable(typeof(RepositoryInfo))]
[JsonSerializable(typeof(List<JobInfo>))]
[JsonSerializable(typeof(List<RepositoryInfo>))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(FileUpload))]
[JsonSerializable(typeof(JobFile))]
[JsonSerializable(typeof(CliJobFilter))]
[JsonSerializable(typeof(FileUploadResponse))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(LoginResponse))]
[JsonSerializable(typeof(LogoutRequest))]
[JsonSerializable(typeof(LogoutResponse))]
[JsonSerializable(typeof(JobStatusResponse))]
[JsonSerializable(typeof(StartJobResponse))]
[JsonSerializable(typeof(CancelJobResponse))]
[JsonSerializable(typeof(DeleteJobResponse))]
[JsonSerializable(typeof(CreateJobResponse))]
[JsonSerializable(typeof(JobListResponse))]
[JsonSerializable(typeof(IEnumerable<JobListResponse>))]
[JsonSerializable(typeof(IEnumerable<RepositoryInfo>))]
[JsonSerializable(typeof(CreateJobRequest))]
[JsonSerializable(typeof(RepositoryResponse))]
[JsonSerializable(typeof(IEnumerable<RepositoryResponse>))]
[JsonSerializable(typeof(RegisterRepositoryRequest))]
[JsonSerializable(typeof(RegisterRepositoryResponse))]
[JsonSerializable(typeof(UnregisterRepositoryResponse))]
[JsonSerializable(typeof(AheadBehindStatus))]
[JsonSerializable(typeof(FileInfoResponse))]
[JsonSerializable(typeof(IEnumerable<FileInfoResponse>))]
[JsonSerializable(typeof(FileContentResponse))]
[JsonSerializable(typeof(DirectoryInfoResponse))]
public partial class CliJsonSerializerContext : JsonSerializerContext
{
}