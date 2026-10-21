Add-Type -AssemblyName System.Runtime.WindowsRuntime
$asTaskGeneric = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.IsGenericMethod } | Select-Object -First 1

$vault = [Windows.Security.Credentials.PasswordVault, Windows.Security.Credentials, ContentType = WindowsRuntime]::new()
try {
    $c = $vault.Retrieve('YMusic.Desktop', 'OAuthToken')
    $c.RetrievePassword()
    $token = $c.Password
    Write-Host "Got token: $($token.Substring(0, 5))..."
    $headers = @{ 'Authorization' = "OAuth $token" }
    $res = Invoke-RestMethod -Uri 'https://api.music.yandex.net/rotor/station/user:onyourwave/tracks' -Headers $headers
    $first = $res.result.sequence[0]
    Write-Host "Type: $($first.type)"
    Write-Host "Track is null? $($null -eq $first.track)"
    if ($first.track) {
        Write-Host "Track title: $($first.track.title)"
        Write-Host "Track id: $($first.track.id)"
        Write-Host "Track artists: $($first.track.artists | ConvertTo-Json -Compress)"
        Write-Host "Track coverUri: $($first.track.coverUri)"
        Write-Host "Sequence count: $($res.result.sequence.Count)"
    } else {
        Write-Host "First item raw: $($first | ConvertTo-Json -Depth 4)"
    }
} catch {
    Write-Host "Error: $_"
}
