Add-Type -AssemblyName System.Runtime.WindowsRuntime
$vault = [Windows.Security.Credentials.PasswordVault, Windows.Security.Credentials, ContentType = WindowsRuntime]::new()
try {
    $c = $vault.Retrieve('YMusic.Desktop', 'OAuthToken')
    $c.RetrievePassword()
    $token = $c.Password

    $u = $vault.Retrieve('YMusic.Desktop', 'CurrentUserId')
    $u.RetrievePassword()
    $userId = $u.Password

    $headers = @{ 
        'Authorization' = "OAuth $token"
        'Accept' = 'application/json'
    }
    $sampleTrackId = "122635484"

    # Test remove
    $remRes = Invoke-RestMethod -Method Post -Uri "https://api.music.yandex.net/users/$userId/likes/tracks/remove" -Headers $headers -Body "track-ids=$sampleTrackId" -ContentType "application/x-www-form-urlencoded"
    Write-Host "remove result: $($remRes | ConvertTo-Json -Compress)"

    # Test add back
    $addRes = Invoke-RestMethod -Method Post -Uri "https://api.music.yandex.net/users/$userId/likes/tracks/add-multiple" -Headers $headers -Body "track-ids=$sampleTrackId" -ContentType "application/x-www-form-urlencoded"
    Write-Host "add-multiple result: $($addRes | ConvertTo-Json -Compress)"
} catch {
    Write-Host "Error: $_"
}
