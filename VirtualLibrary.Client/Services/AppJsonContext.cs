using System.Text.Json;
using System.Text.Json.Serialization;
using VirtualLibrary.Shared;

namespace VirtualLibrary.Client.Services;

/// <summary>
/// Source-generated <see cref="System.Text.Json.Serialization.JsonSerializerContext"/>
/// covering every DTO and request type that flows over HTTP between
/// <see cref="ApiClient"/> and the VirtualLibrary API.
///
/// <para>Using <see cref="System.Text.Json.JsonSerializerDefaults.Web"/> for the
/// generation options ensures behavioural parity with the previous
/// <c>new JsonSerializerOptions(JsonSerializerDefaults.Web)</c> while
/// eliminating all IL2026 / IL3050 warnings produced by the reflection-based
/// overloads of <c>ReadFromJsonAsync</c> and <c>PostAsJsonAsync</c>.</para>
///
/// <para>Each <c>[JsonSerializable]</c> entry causes the Roslyn source generator
/// to emit a strongly-typed <c>JsonTypeInfo&lt;T&gt;</c> property on
/// <see cref="AppJsonContext.Default"/>, which is then passed directly to the
/// trim-safe overloads of the <c>System.Net.Http.Json</c> extension methods.</para>
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
// ── Auth ────────────────────────────────────────────────────────────────────
[JsonSerializable(typeof(AuthResponse))]
[JsonSerializable(typeof(UserResponse))]
[JsonSerializable(typeof(List<UserResponse>))]
[JsonSerializable(typeof(PasswordLoginRequest))]
[JsonSerializable(typeof(ExternalLoginRequest))]
[JsonSerializable(typeof(ApproveUserRequest))]
[JsonSerializable(typeof(ChangeRoleRequest))]
// ── Library ─────────────────────────────────────────────────────────────────
[JsonSerializable(typeof(IsbnLookupResponse))]
[JsonSerializable(typeof(UserBookDto))]
[JsonSerializable(typeof(List<UserBookDto>))]
[JsonSerializable(typeof(AddUserBookRequest))]
[JsonSerializable(typeof(UpdateUserBookRequest))]
[JsonSerializable(typeof(ReadRecordDto))]
[JsonSerializable(typeof(List<ReadRecordDto>))]
[JsonSerializable(typeof(AddReadRecordRequest))]
// ── Shelf ────────────────────────────────────────────────────────────────────
[JsonSerializable(typeof(ShelfDto))]
[JsonSerializable(typeof(SaveShelfPlacementsRequest))]
// ── Stats ─────────────────────────────────────────────────────────────────────
[JsonSerializable(typeof(LibraryStatsDto))]
[JsonSerializable(typeof(RankedItemDto))]
[JsonSerializable(typeof(List<RankedItemDto>))]
// ── Bulk import ──────────────────────────────────────────────────────────────
[JsonSerializable(typeof(BulkImportRequest))]
[JsonSerializable(typeof(BulkImportResponse))]
[JsonSerializable(typeof(ImportRowResult))]
[JsonSerializable(typeof(List<ImportRowResult>))]
[JsonSerializable(typeof(ImportSummary))]
// ── PKCE ─────────────────────────────────────────────────────────────────────
[JsonSerializable(typeof(TokenExchangeRequest))]
#if DEBUG
// ── Dev login (DEBUG only — not compiled into Release/production builds) ─────
[JsonSerializable(typeof(DevLoginRequest))]
#endif
internal sealed partial class AppJsonContext : JsonSerializerContext
{
}
