#nullable enable

using Windows.Security.Credentials;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.Services.Implementations;

public sealed class CredentialStorageService : ICredentialStorageService
{
    private const string ResourceName = "YMusic.Desktop";
    private const string TokenCredentialName = "OAuthToken";
    private const string UserIdCredentialName = "CurrentUserId";

    private readonly PasswordVault _vault = new();

    public string? GetToken()
    {
        try
        {
            var credential = _vault.Retrieve(ResourceName, TokenCredentialName);
            credential.RetrievePassword();
            return credential.Password;
        }
        catch
        {
            return null;
        }
    }

    public void SaveToken(string token, string? userId = null)
    {
        RemoveToken();

        var tokenCredential = new PasswordCredential(ResourceName, TokenCredentialName, token);
        _vault.Add(tokenCredential);

        if (!string.IsNullOrWhiteSpace(userId))
        {
            SaveUserId(userId);
        }
    }

    public void RemoveToken()
    {
        try
        {
            var credential = _vault.Retrieve(ResourceName, TokenCredentialName);
            _vault.Remove(credential);
        }
        catch
        {
            // Ignore if credential does not exist
        }

        try
        {
            var userCred = _vault.Retrieve(ResourceName, UserIdCredentialName);
            _vault.Remove(userCred);
        }
        catch
        {
            // Ignore if credential does not exist
        }
    }

    public bool HasToken()
    {
        return !string.IsNullOrWhiteSpace(GetToken());
    }

    public string? GetUserId()
    {
        try
        {
            var credential = _vault.Retrieve(ResourceName, UserIdCredentialName);
            credential.RetrievePassword();
            return credential.Password;
        }
        catch
        {
            return null;
        }
    }

    public void SaveUserId(string userId)
    {
        try
        {
            var existing = _vault.Retrieve(ResourceName, UserIdCredentialName);
            _vault.Remove(existing);
        }
        catch
        {
            // Ignore if does not exist
        }

        var cred = new PasswordCredential(ResourceName, UserIdCredentialName, userId);
        _vault.Add(cred);
    }
}
