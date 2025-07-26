using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeBatchServer.Core.DTOs;
using ClaudeBatchServer.Core.Models;

namespace ClaudeBatchServer.Core.Serialization;

/// <summary>
/// JSON serialization context for trim-safe serialization
/// This eliminates IL2026 warnings when using --self-contained publishing
/// </summary>
[JsonSerializable(typeof(Job))]
[JsonSerializable(typeof(Repository))]
[JsonSerializable(typeof(RepositorySettings))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(List<Job>))]
[JsonSerializable(typeof(List<Repository>))]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(DateTime))]
public partial class AppJsonSerializerContext : JsonSerializerContext
{
}