using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeServerCLI.Models;

namespace ClaudeServerCLI.Serialization;

/// <summary>
/// JSON serialization context for trim-safe serialization in CLI
/// This eliminates IL2026 warnings when using --self-contained publishing
/// </summary>
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
public partial class CliJsonSerializerContext : JsonSerializerContext
{
}