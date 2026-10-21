#nullable enable

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Security.Credentials;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.Services.Implementations;

public sealed class CredentialStorageService : ICredentialStorageService
{
    private const string ResourceName = "YMusic.Desktop";
    private const string TokenCredentialName = "OAuthToken";
    private const string UserIdCredentialName = "CurrentUserId";

    private readonly PasswordVault _vault = new();
    private readonly object _lock = new();

    private string? _cachedToken;
    private string? _cachedUserId;

    private readonly string _localSessionPath;
    private readonly string _roamingSessionPath;

    private sealed class SessionPayload
    {
        [JsonPropertyName("token")]
        public string? Token { get; set; }

        [JsonPropertyName("userId")]
        public string? UserId { get; set; }

        [JsonPropertyName("updatedAt")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public CredentialStorageService()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string localDir = Path.Combine(localAppData, "YMusic");
        Directory.CreateDirectory(localDir);
        _localSessionPath = Path.Combine(localDir, "session.json");

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string roamingDir = Path.Combine(appData, "YMusic");
        Directory.CreateDirectory(roamingDir);
        _roamingSessionPath = Path.Combine(roamingDir, "session.json");

        LoadInitialCredentials();
    }

    private void LoadInitialCredentials()
    {
        lock (_lock)
        {
            // 1. Try local session file
            if (TryLoadFromFile(_localSessionPath)) return;

            // 2. Try roaming session file
            if (TryLoadFromFile(_roamingSessionPath))
            {
                SaveToFile(_localSessionPath, _cachedToken, _cachedUserId);
                return;
            }

            // 3. Try Windows PasswordVault
            try
            {
                var cred = _vault.Retrieve(ResourceName, TokenCredentialName);
                cred.RetrievePassword();
                if (!string.IsNullOrWhiteSpace(cred.Password))
                {
                    _cachedToken = cred.Password;
                    try
                    {
                        var userCred = _vault.Retrieve(ResourceName, UserIdCredentialName);
                        userCred.RetrievePassword();
                        _cachedUserId = userCred.Password;
                    }
                    catch { }

                    SaveToFile(_localSessionPath, _cachedToken, _cachedUserId);
                    SaveToFile(_roamingSessionPath, _cachedToken, _cachedUserId);
                    return;
                }
            }
            catch { }

            // 4. Try Environment variables / .env
            string envToken = EnvironmentConfig.Get("YANDEX_TOKEN");
            if (string.IsNullOrWhiteSpace(envToken))
            {
                envToken = Environment.GetEnvironmentVariable("YANDEX_MUSIC_TOKEN") ?? string.Empty;
            }
            if (!string.IsNullOrWhiteSpace(envToken))
            {
                _cachedToken = envToken.Trim();
                SaveToFile(_localSessionPath, _cachedToken, null);
                SaveToFile(_roamingSessionPath, _cachedToken, null);
            }
        }
    }

    private bool TryLoadFromFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var payload = JsonSerializer.Deserialize<SessionPayload>(json);
                if (payload != null && !string.IsNullOrWhiteSpace(payload.Token))
                {
                    _cachedToken = payload.Token;
                    _cachedUserId = payload.UserId;
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CredentialStorage] Read error ({path}): {ex.Message}");
        }
        return false;
    }

    private void SaveToFile(string path, string? token, string? userId)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        try
        {
            string dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);
            var payload = new SessionPayload
            {
                Token = token,
                UserId = userId,
                UpdatedAt = DateTime.UtcNow
            };
            string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CredentialStorage] Write error ({path}): {ex.Message}");
        }
    }

    public string? GetToken()
    {
        lock (_lock)
        {
            if (!string.IsNullOrWhiteSpace(_cachedToken))
            {
                return _cachedToken;
            }

            LoadInitialCredentials();
            return _cachedToken;
        }
    }

    public void SaveToken(string token, string? userId = null)
    {
        if (string.IsNullOrWhiteSpace(token)) return;

        lock (_lock)
        {
            _cachedToken = token.Trim();
            if (!string.IsNullOrWhiteSpace(userId))
            {
                _cachedUserId = userId.Trim();
            }

            // Persist to disk files (both LocalAppData and ApplicationData)
            SaveToFile(_localSessionPath, _cachedToken, _cachedUserId);
            SaveToFile(_roamingSessionPath, _cachedToken, _cachedUserId);

            // Also persist to PasswordVault as secondary storage
            try
            {
                try
                {
                    var existing = _vault.Retrieve(ResourceName, TokenCredentialName);
                    _vault.Remove(existing);
                }
                catch { }

                var tokenCredential = new PasswordCredential(ResourceName, TokenCredentialName, _cachedToken);
                _vault.Add(tokenCredential);

                if (!string.IsNullOrWhiteSpace(_cachedUserId))
                {
                    SaveUserId(_cachedUserId);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CredentialStorage] PasswordVault save warning: {ex.Message}");
            }
        }
    }

    public void RemoveToken()
    {
        lock (_lock)
        {
            _cachedToken = null;
            _cachedUserId = null;

            try
            {
                if (File.Exists(_localSessionPath)) File.Delete(_localSessionPath);
            }
            catch { }

            try
            {
                if (File.Exists(_roamingSessionPath)) File.Delete(_roamingSessionPath);
            }
            catch { }

            try
            {
                var credential = _vault.Retrieve(ResourceName, TokenCredentialName);
                _vault.Remove(credential);
            }
            catch { }

            try
            {
                var userCred = _vault.Retrieve(ResourceName, UserIdCredentialName);
                _vault.Remove(userCred);
            }
            catch { }
        }
    }

    public bool HasToken()
    {
        return !string.IsNullOrWhiteSpace(GetToken());
    }

    public string? GetUserId()
    {
        lock (_lock)
        {
            if (!string.IsNullOrWhiteSpace(_cachedUserId))
            {
                return _cachedUserId;
            }

            GetToken(); // forces reload from files/vault
            return _cachedUserId;
        }
    }

    public void SaveUserId(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return;

        lock (_lock)
        {
            _cachedUserId = userId.Trim();
            SaveToFile(_localSessionPath, _cachedToken, _cachedUserId);
            SaveToFile(_roamingSessionPath, _cachedToken, _cachedUserId);

            try
            {
                try
                {
                    var existing = _vault.Retrieve(ResourceName, UserIdCredentialName);
                    _vault.Remove(existing);
                }
                catch { }

                var cred = new PasswordCredential(ResourceName, UserIdCredentialName, _cachedUserId);
                _vault.Add(cred);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CredentialStorage] PasswordVault SaveUserId warning: {ex.Message}");
            }
        }
    }
}
