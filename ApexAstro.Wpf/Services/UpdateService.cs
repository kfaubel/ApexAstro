using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Reflection;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;

namespace ApexAstro.Wpf.Services
{
    /// <summary>
    /// Service for checking for application updates from GitHub releases
    /// </summary>
    public class UpdateService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _repoOwner;
        private readonly string _repoName;
        private readonly string _currentVersion;

        public UpdateService(string repoOwner, string repoName)
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "ApexAstro-UpdateChecker");
            _repoOwner = repoOwner;
            _repoName = repoName;
            
            // Get current version from assembly
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            _currentVersion = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "2.0.0";
        }

        /// <summary>
        /// Check if a newer version is available on GitHub releases
        /// </summary>
        /// <returns>UpdateInfo with details about available update, or null if no update</returns>
        public async Task<UpdateInfo?> CheckForUpdatesAsync()
        {
            try
            {
                // GitHub API endpoint for latest release
                string apiUrl = $"https://api.github.com/repos/{_repoOwner}/{_repoName}/releases/latest";
                
                string response = await _httpClient.GetStringAsync(apiUrl);
                var releaseInfo = JsonSerializer.Deserialize<GitHubRelease>(response);
                
                if (releaseInfo?.tag_name != null)
                {
                    // Remove 'v' prefix if present (e.g., "v2.1.0" -> "2.1.0")
                    string latestVersionString = releaseInfo.tag_name.TrimStart('v');
                    
                    if (IsNewerVersion(latestVersionString, _currentVersion))
                    {
                        return new UpdateInfo
                        {
                            LatestVersion = latestVersionString,
                            CurrentVersion = _currentVersion,
                            ReleaseNotes = ResolveReleaseNotes(releaseInfo.body),
                            DownloadUrl = releaseInfo.html_url ?? "",
                            ReleaseName = releaseInfo.name ?? $"Version {latestVersionString}",
                            PublishedDate = releaseInfo.published_at
                        };
                    }
                }
                
                return null; // No update available
            }
            catch (Exception ex)
            {
                // Log error but don't crash the app
                System.Diagnostics.Debug.WriteLine($"Update check failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Compare version strings to determine if update is available
        /// </summary>
        private bool IsNewerVersion(string latestVersion, string currentVersion)
        {
            try
            {
                var latest = new Version(latestVersion);
                var current = new Version(currentVersion);
                return latest > current;
            }
            catch
            {
                // If version parsing fails, assume no update to be safe
                return false;
            }
        }

        /// <summary>
        /// Open the GitHub releases page in default browser
        /// </summary>
        public void OpenReleasePage(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to open browser: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }

        private string ResolveReleaseNotes(string? githubReleaseNotes)
        {
            if (!string.IsNullOrWhiteSpace(githubReleaseNotes))
            {
                return githubReleaseNotes.Trim();
            }

            var localReleaseNotes = TryReadLocalReleaseNotes();
            if (!string.IsNullOrWhiteSpace(localReleaseNotes))
            {
                return localReleaseNotes;
            }

            return "No release notes available for this release. Open the release page for details.";
        }

        private static string? TryReadLocalReleaseNotes()
        {
            var candidateFiles = new[]
            {
                "RELEASE_NOTES.md",
                "RELEASE_NOTES.txt",
                "RELEASE_NOTES"
            };

            var visitedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var directory in EnumerateCandidateDirectories())
            {
                if (string.IsNullOrWhiteSpace(directory) || !visitedDirectories.Add(directory))
                {
                    continue;
                }

                foreach (var fileName in candidateFiles)
                {
                    try
                    {
                        var path = Path.Combine(directory, fileName);
                        if (!File.Exists(path))
                        {
                            continue;
                        }

                        var content = File.ReadAllText(path).Trim();
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            return content;
                        }
                    }
                    catch
                    {
                        // Ignore file access errors and continue trying other paths.
                    }
                }
            }

            return null;
        }

        private static IEnumerable<string> EnumerateCandidateDirectories()
        {
            var directories = new List<string>();

            void AddDirectoryAndParents(string? startDirectory, int maxLevels)
            {
                if (string.IsNullOrWhiteSpace(startDirectory))
                {
                    return;
                }

                var current = startDirectory;
                for (int i = 0; i <= maxLevels && !string.IsNullOrWhiteSpace(current); i++)
                {
                    directories.Add(current);

                    try
                    {
                        current = Directory.GetParent(current)?.FullName;
                    }
                    catch
                    {
                        break;
                    }
                }
            }

            AddDirectoryAndParents(AppContext.BaseDirectory, 4);

            try
            {
                AddDirectoryAndParents(Directory.GetCurrentDirectory(), 4);
            }
            catch
            {
                // Ignore inability to read the current directory.
            }

            return directories;
        }
    }

    /// <summary>
    /// Information about an available update
    /// </summary>
    public class UpdateInfo
    {
        public string LatestVersion { get; set; } = "";
        public string CurrentVersion { get; set; } = "";
        public string ReleaseNotes { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string ReleaseName { get; set; } = "";
        public DateTime? PublishedDate { get; set; }
    }

    /// <summary>
    /// GitHub API response model for releases
    /// </summary>
    internal class GitHubRelease
    {
        public string? tag_name { get; set; }
        public string? name { get; set; }
        public string? body { get; set; }
        public string? html_url { get; set; }
        public DateTime published_at { get; set; }
        public bool prerelease { get; set; }
        public bool draft { get; set; }
    }
}