using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using WorkLogManagementSystem_UI.Models;

namespace WorkLogManagementSystem_UI.Services;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true)]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(TokenResponse))]
[JsonSerializable(typeof(RefreshTokenRequest))]
[JsonSerializable(typeof(UserDto))]
[JsonSerializable(typeof(RegisterRequest))]
[JsonSerializable(typeof(ChangePasswordRequest))]
[JsonSerializable(typeof(List<TaskDto>))]
[JsonSerializable(typeof(TaskDto))]
[JsonSerializable(typeof(TaskWriteRequest))]
[JsonSerializable(typeof(WorkEntryDto))]
[JsonSerializable(typeof(WorkEntryWriteRequest))]
[JsonSerializable(typeof(DailyNoteDto))]
[JsonSerializable(typeof(DailyNoteWriteRequest))]
[JsonSerializable(typeof(AppSettingsResult))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{
    public static JsonTypeInfo<T> TypeInfo<T>()
    {
        return (JsonTypeInfo<T>)(Default.GetTypeInfo(typeof(T))
            ?? throw new InvalidOperationException($"JSON metadata for {typeof(T)} is not registered."));
    }
}
