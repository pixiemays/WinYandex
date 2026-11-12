#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.Services.Implementations;

public sealed class UpdateService : IUpdateService
{
    private const string RepoOwner = "pixiemays";
    private const string RepoName = "WinYandex";
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    static UpdateService()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WinYandex-App/1.0");
        HttpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public static string AppVersion
    {
        get
        {
            var assembly = Assembly.GetExecutingAssembly();
            var infoVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(infoVersion))
            {
                int plusIdx = infoVersion.IndexOf('+');
                return plusIdx > 0 ? infoVersion.Substring(0, plusIdx) : infoVersion;
            }
            var ver = assembly.GetName().Version;
            if (ver == null) return "1.0.0";
            return ver.Revision > 0
                ? $"{ver.Major}.{ver.Minor}.{ver.Build}.{ver.Revision}"
                : $"{ver.Major}.{ver.Minor}.{ver.Build}";
        }
    }

    public string CurrentVersion => AppVersion;

    public async Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        string currentVer = CurrentVersion;
        var info = new UpdateInfo
        {
            CurrentVersion = currentVer,
            IsUpdateAvailable = false
        };

        try
        {
            string url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
            using var response = await HttpClient.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return info;
            }

            string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
            string cleanTag = tagName.TrimStart('v', 'V');
            info.LatestVersion = cleanTag;

            if (root.TryGetProperty("name", out var nameProp))
            {
                info.ReleaseName = nameProp.GetString() ?? tagName;
            }

            if (root.TryGetProperty("body", out var bodyProp))
            {
                info.ReleaseNotes = bodyProp.GetString() ?? "";
            }

            bool isGenericChangelog = string.IsNullOrWhiteSpace(info.ReleaseNotes) ||
                                     info.ReleaseNotes.Contains("Full Changelog", StringComparison.OrdinalIgnoreCase) ||
                                     info.ReleaseNotes.Trim().StartsWith("**Full Changelog**", StringComparison.OrdinalIgnoreCase);

            if (isGenericChangelog)
            {
                // 1. Try fetching CHANGELOG.md from GitHub raw repository
                try
                {
                    string changelogUrl = $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/main/CHANGELOG.md";
                    using var clResponse = await HttpClient.GetAsync(changelogUrl, ct).ConfigureAwait(false);
                    if (clResponse.IsSuccessStatusCode)
                    {
                        string clContent = await clResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        string parsedNotes = ExtractReleaseNotesFromChangelog(clContent, cleanTag);
                        if (!string.IsNullOrWhiteSpace(parsedNotes))
                        {
                            info.ReleaseNotes = parsedNotes;
                            isGenericChangelog = false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[UpdateService] Failed to load CHANGELOG.md from GitHub: {ex.Message}");
                }

                // 2. Fallback to local CHANGELOG.md if available
                if (isGenericChangelog)
                {
                    try
                    {
                        string localChangelog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CHANGELOG.md");
                        if (File.Exists(localChangelog))
                        {
                            string localContent = File.ReadAllText(localChangelog);
                            string parsedNotes = ExtractReleaseNotesFromChangelog(localContent, cleanTag);
                            if (!string.IsNullOrWhiteSpace(parsedNotes))
                            {
                                info.ReleaseNotes = parsedNotes;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[UpdateService] Failed to load local CHANGELOG.md: {ex.Message}");
                    }
                }
            }

            // 3. Load Russian changelog (from raw CHANGELOG_ru.md on GitHub or local CHANGELOG_ru.md)
            try
            {
                string changelogRuUrl = $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/main/CHANGELOG_ru.md";
                using var clRuResponse = await HttpClient.GetAsync(changelogRuUrl, ct).ConfigureAwait(false);
                if (clRuResponse.IsSuccessStatusCode)
                {
                    string clRuContent = await clRuResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    string parsedNotesRu = ExtractReleaseNotesFromChangelog(clRuContent, cleanTag);
                    if (!string.IsNullOrWhiteSpace(parsedNotesRu))
                    {
                        info.ReleaseNotesRu = parsedNotesRu;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateService] Failed to load CHANGELOG_ru.md from GitHub: {ex.Message}");
            }

            if (string.IsNullOrWhiteSpace(info.ReleaseNotesRu))
            {
                try
                {
                    string localChangelogRu = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CHANGELOG_ru.md");
                    if (File.Exists(localChangelogRu))
                    {
                        string localRuContent = File.ReadAllText(localChangelogRu);
                        string parsedNotesRu = ExtractReleaseNotesFromChangelog(localRuContent, cleanTag);
                        if (!string.IsNullOrWhiteSpace(parsedNotesRu))
                        {
                            info.ReleaseNotesRu = parsedNotesRu;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[UpdateService] Failed to load local CHANGELOG_ru.md: {ex.Message}");
                }
            }

            if (root.TryGetProperty("html_url", out var htmlProp))
            {
                info.HtmlUrl = htmlProp.GetString() ?? info.HtmlUrl;
            }

            if (root.TryGetProperty("published_at", out var pubProp) && pubProp.TryGetDateTime(out var pubDate))
            {
                info.PublishedAt = pubDate;
            }

            // Find best asset (prefer .exe installer, fallback to .zip) and locate checksum assets
            if (root.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
            {
                JsonElement? bestAsset = null;
                string? checksumUrl = null;

                foreach (var asset in assetsProp.EnumerateArray())
                {
                    string name = asset.TryGetProperty("name", out var np) ? np.GetString() ?? "" : "";
                    string? assetDownloadUrl = asset.TryGetProperty("browser_download_url", out var du) ? du.GetString() : null;

                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        bestAsset = asset;
                    }
                    else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && bestAsset == null)
                    {
                        bestAsset = asset;
                    }
                    else if (string.Equals(name, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(name, "checksums.txt", StringComparison.OrdinalIgnoreCase) ||
                             name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase))
                    {
                        checksumUrl = assetDownloadUrl;
                    }
                }

                if (bestAsset.HasValue)
                {
                    var a = bestAsset.Value;
                    info.AssetName = a.TryGetProperty("name", out var an) ? an.GetString() : null;
                    info.DownloadUrl = a.TryGetProperty("browser_download_url", out var du) ? du.GetString() : null;
                    info.FileSizeBytes = a.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
                }

                info.ChecksumAssetUrl = checksumUrl;
            }

            // Attempt to extract expected SHA-256 hash from release body if available
            if (!string.IsNullOrWhiteSpace(info.AssetName))
            {
                info.ExpectedSha256 = ExtractHashFromText(info.ReleaseNotes, info.AssetName);
            }

            info.IsUpdateAvailable = IsNewerVersion(cleanTag, currentVer);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateService] CheckForUpdates error: {ex.Message}");
        }

        return info;
    }

    public async Task<string> DownloadUpdateAsync(
        UpdateInfo update,
        IProgress<(double Percentage, long DownloadedBytes, long TotalBytes)>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(update.DownloadUrl))
        {
            throw new InvalidOperationException("No download URL available for update.");
        }

        string updatesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YMusic", "Updates");
        Directory.CreateDirectory(updatesDir);

        // Clean up any old installer files before starting a new download
        try
        {
            foreach (var existingFile in Directory.EnumerateFiles(updatesDir))
            {
                try { File.Delete(existingFile); } catch { }
            }
        }
        catch { }

        string fileName = update.AssetName ?? $"WinYandex-v{update.LatestVersion}-Setup.exe";
        string filePath = Path.Combine(updatesDir, fileName);

        // 1. Try to obtain expected SHA-256 hash from release checksum asset
        if (string.IsNullOrWhiteSpace(update.ExpectedSha256) && !string.IsNullOrWhiteSpace(update.ChecksumAssetUrl))
        {
            try
            {
                string checksums = await HttpClient.GetStringAsync(update.ChecksumAssetUrl, ct).ConfigureAwait(false);
                update.ExpectedSha256 = FindHashInChecksums(checksums, fileName);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateService] Failed to fetch checksum asset: {ex.Message}");
            }
        }

        // 2. Fallback: Check repository dist/SHA256SUMS.txt if still null
        if (string.IsNullOrWhiteSpace(update.ExpectedSha256))
        {
            try
            {
                string fallbackUrl = $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/main/dist/SHA256SUMS.txt";
                using var fbResp = await HttpClient.GetAsync(fallbackUrl, ct).ConfigureAwait(false);
                if (fbResp.IsSuccessStatusCode)
                {
                    string checksums = await fbResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    update.ExpectedSha256 = FindHashInChecksums(checksums, fileName);
                }
            }
            catch { }
        }

        // 3. Download update payload
        using var response = await HttpClient.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long totalBytes = response.Content.Headers.ContentLength ?? update.FileSizeBytes;

        using (var contentStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
        {
            byte[] buffer = new byte[81920];
            long downloaded = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, ct).ConfigureAwait(false);
                downloaded += bytesRead;

                if (totalBytes > 0 && progress != null)
                {
                    double pct = Math.Clamp((double)downloaded / totalBytes * 100.0, 0.0, 100.0);
                    progress.Report((pct, downloaded, totalBytes));
                }
            }

            await fileStream.FlushAsync(ct).ConfigureAwait(false);
        }

        // 4. Perform strict integrity and digital signature verification before returning file
        await VerifyUpdateIntegrityAsync(filePath, update.ExpectedSha256, fileName, ct).ConfigureAwait(false);

        return filePath;
    }

    public static async Task VerifyUpdateIntegrityAsync(
        string filePath,
        string? expectedSha256,
        string fileName,
        CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Update file was not found for integrity verification.", filePath);
        }

        // 1. Calculate actual SHA-256
        string actualSha256;
        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
        {
            byte[] hashBytes = await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false);
            actualSha256 = Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        // 2. Check Windows Authenticode digital signature
        var signature = VerifyAuthenticodeSignature(filePath);

        // If digital signature exists but is tampered/corrupt, fail immediately
        if (signature.IsSigned && !signature.IsValid)
        {
            TryDeleteFile(filePath);
            throw new System.Security.SecurityException(
                $"Update integrity verification failed: Executable has an invalid or tampered digital signature ({signature.ErrorMessage}).");
        }

        bool isSha256Verified = false;
        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            string cleanExpected = expectedSha256.Trim().ToLowerInvariant();
            if (!string.Equals(actualSha256, cleanExpected, StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteFile(filePath);
                throw new System.Security.SecurityException(
                    $"Update integrity check failed: SHA-256 hash mismatch for '{fileName}'.\n" +
                    $"Expected: {cleanExpected}\n" +
                    $"Actual:   {actualSha256}\n" +
                    $"The downloaded update package may have been corrupted or tampered with.");
            }

            isSha256Verified = true;
            Debug.WriteLine($"[UpdateService] SHA-256 integrity verified successfully: {actualSha256}");
        }

        bool isSignatureVerified = signature.IsSigned && signature.IsValid;
        if (isSignatureVerified)
        {
            Debug.WriteLine($"[UpdateService] Digital signature verified successfully for '{fileName}'.");
        }

        // Require at least one verified security mechanism: valid digital signature OR verified SHA-256 checksum
        if (!isSha256Verified && !isSignatureVerified)
        {
            TryDeleteFile(filePath);
            throw new System.Security.SecurityException(
                $"Update verification failed: The update package '{fileName}' has neither a verified SHA-256 checksum " +
                $"nor a valid digital signature. Execution aborted to protect system integrity.");
        }
    }

    public void InstallAndRestart(string installerFilePath)
    {
        if (!File.Exists(installerFilePath))
        {
            throw new FileNotFoundException("Installer file was not found", installerFilePath);
        }

        try
        {
            if (installerFilePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                // Final safety check before executing external installer
                var sig = VerifyAuthenticodeSignature(installerFilePath);
                if (sig.IsSigned && !sig.IsValid)
                {
                    TryDeleteFile(installerFilePath);
                    throw new System.Security.SecurityException(
                        $"Cannot launch installer: Invalid or tampered digital signature ({sig.ErrorMessage}).");
                }

                // Run Inno Setup installer
                Process.Start(new ProcessStartInfo
                {
                    FileName = installerFilePath,
                    UseShellExecute = true
                });

                // Spawn a detached delayed background command to delete the installer after installation
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c timeout /t 90 /nobreak >nul & del /f /q \"{installerFilePath}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    });
                }
                catch { }
            }
            else
            {
                // Portable ZIP - reveal in explorer
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{installerFilePath}\"",
                    UseShellExecute = true
                });
            }

            // Clean shutdown of current app to allow overwriting files
            if (Application.Current != null)
            {
                Application.Current.Exit();
            }
            else
            {
                Environment.Exit(0);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateService] InstallAndRestart failed: {ex.Message}");
            throw;
        }
    }

    #region Digital Signature & Hash Helpers

    public sealed class SignatureVerificationResult
    {
        public bool IsSigned { get; init; }
        public bool IsValid { get; init; }
        public string? ErrorMessage { get; init; }

        public static SignatureVerificationResult NotSigned() =>
            new() { IsSigned = false, IsValid = false, ErrorMessage = "File is not digitally signed." };

        public static SignatureVerificationResult Valid() =>
            new() { IsSigned = true, IsValid = true };

        public static SignatureVerificationResult Invalid(string message) =>
            new() { IsSigned = true, IsValid = false, ErrorMessage = message };
    }

    private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOCATION_CHECK_NONE = 0x00000010;
    private const uint WTD_STATEACTION_IGNORE = 0;
    private const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x00001000;

    private const int ERROR_SUCCESS = 0;
    private const int TRUST_E_NOSIGNATURE = unchecked((int)0x800B0100);
    private const int TRUST_E_BAD_DIGEST = unchecked((int)0x80096010);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(
        [In] IntPtr hwnd,
        [In] [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID,
        [In] IntPtr pWVTData);

    public static SignatureVerificationResult VerifyAuthenticodeSignature(string filePath)
    {
        try
        {
            var fileInfo = new WINTRUST_FILE_INFO
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
                pcwszFilePath = filePath,
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero
            };

            IntPtr pFileInfo = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
            try
            {
                Marshal.StructureToPtr(fileInfo, pFileInfo, false);

                var trustData = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                    pPolicyCallbackData = IntPtr.Zero,
                    pSIPClientData = IntPtr.Zero,
                    dwUIChoice = WTD_UI_NONE,
                    fdwRevocationChecks = WTD_REVOCATION_CHECK_NONE,
                    dwUnionChoice = WTD_CHOICE_FILE,
                    pFile = pFileInfo,
                    dwStateAction = WTD_STATEACTION_IGNORE,
                    hWVTStateData = IntPtr.Zero,
                    pwszURLReference = null,
                    dwProvFlags = WTD_CACHE_ONLY_URL_RETRIEVAL,
                    dwUIContext = 0,
                    pSignatureSettings = IntPtr.Zero
                };

                IntPtr pTrustData = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_DATA>());
                try
                {
                    Marshal.StructureToPtr(trustData, pTrustData, false);

                    int result = WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, pTrustData);
                    if (result == ERROR_SUCCESS)
                    {
                        return SignatureVerificationResult.Valid();
                    }
                    if (result == TRUST_E_NOSIGNATURE)
                    {
                        return SignatureVerificationResult.NotSigned();
                    }
                    if (result == TRUST_E_BAD_DIGEST)
                    {
                        return SignatureVerificationResult.Invalid("Digest mismatch: file has been modified after signing (TRUST_E_BAD_DIGEST).");
                    }
                    return SignatureVerificationResult.Invalid($"Signature trust check failed with error code 0x{result:X8}.");
                }
                finally
                {
                    Marshal.FreeHGlobal(pTrustData);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pFileInfo);
            }
        }
        catch (Exception ex)
        {
            return SignatureVerificationResult.Invalid($"Exception verifying signature: {ex.Message}");
        }
    }

    public static string? FindHashInChecksums(string checksumsContent, string targetFileName)
    {
        if (string.IsNullOrWhiteSpace(checksumsContent)) return null;

        using var reader = new StringReader(checksumsContent);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;

            var match = Regex.Match(line, @"^([a-fA-F0-9]{64})\s+[*]?(.+)$");
            if (match.Success)
            {
                string hash = match.Groups[1].Value;
                string file = Path.GetFileName(match.Groups[2].Value.Trim());
                if (string.Equals(file, targetFileName, StringComparison.OrdinalIgnoreCase))
                {
                    return hash.ToLowerInvariant();
                }
            }
        }
        return null;
    }

    public static string? ExtractHashFromText(string text, string targetFileName)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        string escapedName = Regex.Escape(targetFileName);
        var match1 = Regex.Match(text, $@"(?i){escapedName}[:\s]+([a-f0-9]{{64}})");
        if (match1.Success) return match1.Groups[1].Value.ToLowerInvariant();

        var match2 = Regex.Match(text, $@"(?i)([a-f0-9]{{64}})\s+[*]?{escapedName}");
        if (match2.Success) return match2.Groups[1].Value.ToLowerInvariant();

        var match3 = Regex.Match(text, @"(?i)sha-?256\s*[:=]\s*([a-f0-9]{64})");
        if (match3.Success) return match3.Groups[1].Value.ToLowerInvariant();

        return null;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch { }
    }

    #endregion

    private static bool IsNewerVersion(string latestStr, string currentStr)
    {
        if (string.IsNullOrWhiteSpace(latestStr)) return false;

        try
        {
            var latestVer = ParseSmartVersion(latestStr);
            var currentVer = ParseSmartVersion(currentStr);
            return latestVer > currentVer;
        }
        catch
        {
            return !string.Equals(latestStr, currentStr, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static Version ParseSmartVersion(string ver)
    {
        string clean = ver.Trim().TrimStart('v', 'V');
        int dashIdx = clean.IndexOf('-');
        if (dashIdx > 0) clean = clean.Substring(0, dashIdx);

        var parts = clean.Split('.');
        int major = parts.Length > 0 && int.TryParse(parts[0], out int maj) ? maj : 0;
        int minor = parts.Length > 1 && int.TryParse(parts[1], out int min) ? min : 0;
        int patch = 0;
        int sub = 0;

        if (parts.Length > 2 && int.TryParse(parts[2], out int p))
        {
            if (major == 0 && minor == 9 && p >= 50 && p <= 99)
            {
                patch = p / 10;
                sub = p % 10;
                if (parts.Length > 3 && int.TryParse(parts[3], out int s))
                {
                    sub = sub * 10 + s;
                }
            }
            else
            {
                patch = p;
                if (parts.Length > 3 && int.TryParse(parts[3], out int s))
                {
                    sub = s;
                }
            }
        }

        return new Version(major, minor, patch, sub);
    }

    public static string ExtractReleaseNotesFromChangelog(string changelog, string version)
    {
        if (string.IsNullOrWhiteSpace(changelog) || string.IsNullOrWhiteSpace(version))
        {
            return string.Empty;
        }

        string cleanVersion = version.TrimStart('v', 'V');
        string pattern = @"(?ms)^##\s*\[?" + Regex.Escape(cleanVersion) + @"\]?[^\r\n]*\r?\n(.*?)(?=\r?\n---\s*(\r?\n|\Z)|\r?\n##\s*\[|\Z)";
        var match = Regex.Match(changelog, pattern);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        return string.Empty;
    }

    public static void CleanOldUpdates()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(5000);
                string updatesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YMusic", "Updates");
                if (Directory.Exists(updatesDir))
                {
                    foreach (var file in Directory.EnumerateFiles(updatesDir))
                    {
                        try
                        {
                            File.Delete(file);
                            Debug.WriteLine($"[UpdateService] Cleaned up old update file: {file}");
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateService] CleanOldUpdates error: {ex.Message}");
            }
        });
    }
}
