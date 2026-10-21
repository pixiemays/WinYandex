Add-Type -AssemblyName System.Runtime.WindowsRuntime
$vault = [Windows.Security.Credentials.PasswordVault, Windows.Security.Credentials, ContentType = WindowsRuntime]::new()
try {
    $c = $vault.Retrieve('YMusic.Desktop', 'OAuthToken')
    $c.RetrievePassword()
    $token = $c.Password

    $u = $vault.Retrieve('YMusic.Desktop', 'CurrentUserId')
    $u.RetrievePassword()
    $userId = $u.Password

    $headers = @{ 'Authorization' = "OAuth $token" }

    # Test user like remove
    try {
        $remRes = Invoke-RestMethod -Uri "https://api.music.yandex.net/users/$userId/likes/tracks/remove" -Method Post -Headers $headers -Body "track-ids=111402809" -ContentType 'application/x-www-form-urlencoded'
        Write-Host "Remove like OK: $($remRes | ConvertTo-Json -Compress)"
    } catch {
        Write-Host "Remove like error: $_"
    }
} catch {
    Write-Host "Error: $_"
}
