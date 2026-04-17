using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using VirtualLibrary.Shared;

namespace VirtualLibrary.Client.Services;

public class ApiClient
{
    // Shared instance accessible from all pages
    public static ApiClient Instance { get; } = new();

    private readonly HttpClient _http;
    private string? _token;
    public UserResponse? CurrentUser { get; private set; }

    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public ApiClient()
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(GetBaseUri())
        };
    }

    private static string GetBaseUri()
    {
#if __WASM__
        // In WASM, use the same origin; nginx reverse-proxies /api/* to the backend.
        return "";
#elif __ANDROID__
        // 10.0.2.2 is the Android emulator's loopback alias for the host machine.
        return "http://10.0.2.2:5179";
#else
        return "http://localhost:5179";
#endif
    }

    public void SetToken(string token)
    {
        _token = token;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public void ClearToken()
    {
        _token = null;
        _http.DefaultRequestHeaders.Authorization = null;
    }

    public bool IsAuthenticated => !string.IsNullOrEmpty(_token);

    // --- Auth ---

    public async Task<AuthResponse?> PasswordLoginAsync(string email, string password)
    {
        var response = await _http.PostAsJsonAsync("/api/auth/login/password",
            new PasswordLoginRequest(email, password));
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AuthResponse>(_json);
        if (result != null)
        {
            SetToken(result.Token);
            CurrentUser = result.User;
        }
        return result;
    }

    public async Task<AuthResponse?> ExternalLoginAsync(string provider, string idToken)
    {
        var response = await _http.PostAsJsonAsync("/api/auth/login",
            new ExternalLoginRequest(provider, idToken));
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AuthResponse>(_json);
        if (result != null)
        {
            SetToken(result.Token);
            CurrentUser = result.User;
        }
        return result;
    }

    public void Logout()
    {
        ClearToken();
        CurrentUser = null;
    }

    public async Task<UserResponse?> GetMeAsync()
    {
        var response = await _http.GetAsync("/api/auth/me");
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            ClearToken();
            return null;
        }
        response.EnsureSuccessStatusCode();
        var me = await response.Content.ReadFromJsonAsync<UserResponse>(_json);
        CurrentUser = me;
        return me;
    }

    public async Task<AuthResponse?> RefreshAsync()
    {
        var response = await _http.PostAsync("/api/auth/refresh", content: null);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AuthResponse>(_json);
        if (result != null)
        {
            SetToken(result.Token);
            CurrentUser = result.User;
        }
        return result;
    }

    // --- User Management ---

    public async Task<List<UserResponse>> GetUsersAsync(UserStatus? status = null)
    {
        var url = "/api/users" + (status.HasValue ? $"?status={status.Value}" : "");
        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<UserResponse>>(_json) ?? new List<UserResponse>();
    }

    public async Task<UserResponse?> ApproveUserAsync(string userId, bool approved)
    {
        var response = await _http.PostAsJsonAsync($"/api/users/{userId}/approve",
            new ApproveUserRequest(approved));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UserResponse>(_json);
    }

    public async Task<UserResponse?> ChangeRoleAsync(string userId, UserRole role)
    {
        var response = await _http.PostAsJsonAsync($"/api/users/{userId}/role",
            new ChangeRoleRequest(role));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UserResponse>(_json);
    }

    public async Task<UserResponse?> SuspendUserAsync(string userId)
    {
        var response = await _http.PostAsync($"/api/users/{userId}/suspend", content: null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UserResponse>(_json);
    }

    public async Task<UserResponse?> ReactivateUserAsync(string userId)
    {
        var response = await _http.PostAsync($"/api/users/{userId}/reactivate", content: null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UserResponse>(_json);
    }

    // --- Library ---

    public async Task<IsbnLookupResponse?> LookupIsbnAsync(string isbn)
    {
        var response = await _http.PostAsync($"/api/lookup/{isbn}", content: null);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IsbnLookupResponse>(_json);
    }

    public async Task<List<UserBookDto>> GetBooksAsync(BookStatus? status = null)
    {
        var url = "/api/books" + (status.HasValue ? $"?status={status.Value}" : "");
        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<UserBookDto>>(_json) ?? new();
    }

    public async Task<UserBookDto?> GetBookAsync(Guid id)
    {
        var response = await _http.GetAsync($"/api/books/{id}");
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UserBookDto>(_json);
    }

    public async Task<UserBookDto?> AddBookAsync(string isbn, BookStatus status = BookStatus.Owned)
    {
        var response = await _http.PostAsJsonAsync("/api/books",
            new AddUserBookRequest(isbn, status));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UserBookDto>(_json);
    }

    public async Task UpdateBookAsync(Guid id, UpdateUserBookRequest request)
    {
        var response = await _http.PatchAsJsonAsync($"/api/books/{id}", request);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteBookAsync(Guid id)
    {
        var response = await _http.DeleteAsync($"/api/books/{id}");
        response.EnsureSuccessStatusCode();
    }
}
