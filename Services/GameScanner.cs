using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Launcher
{
    public class GameScanner
    {
        public List<Game> ScanAll()
        {
            var uniqueGames = new Dictionary<string, Game>(StringComparer.OrdinalIgnoreCase);
            ScanSteamGames(uniqueGames);
            ScanEpicGames(uniqueGames);
            return uniqueGames.Values.OrderBy(g => g.Name).ToList();
        }

        private void ScanSteamGames(Dictionary<string, Game> foundGames)
        {
            try
            {
                string? steamInstallPath = GetSteamInstallPath();
                if (string.IsNullOrEmpty(steamInstallPath)) return;

                string libraryCachePath = Path.Combine(steamInstallPath, "appcache", "librarycache");
                var libraryPaths = new List<string> { steamInstallPath };
                string libraryFoldersVdfPath = Path.Combine(steamInstallPath, "steamapps", "libraryfolders.vdf");

                if (File.Exists(libraryFoldersVdfPath))
                {
                    string vdfContent = File.ReadAllText(libraryFoldersVdfPath);
                    MatchCollection matches = Regex.Matches(vdfContent, @"""path""\s+""([^""]+)""");
                    foreach (Match match in matches)
                    {
                        libraryPaths.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
                    }
                }

                foreach (string libraryPath in libraryPaths.Distinct())
                {
                    string steamAppsPath = Path.Combine(libraryPath, "steamapps");
                    if (!Directory.Exists(steamAppsPath)) continue;

                    foreach (string acfFile in Directory.GetFiles(steamAppsPath, "appmanifest_*.acf"))
                    {
                        string acfContent = File.ReadAllText(acfFile);
                        Match nameMatch = Regex.Match(acfContent, @"""name""\s+""([^""]+)""");
                        Match appidMatch = Regex.Match(acfContent, @"""appid""\s+""(\d+)""");

                        if (nameMatch.Success && appidMatch.Success)
                        {
                            string gameName = nameMatch.Groups[1].Value;

                           
                            if (gameName.Contains("Steamworks") || gameName.Contains("Redist"))
                            {
                                continue;
                            }

                            var game = new Game
                            {
                                Name = gameName,
                                AppId = appidMatch.Groups[1].Value,
                                InstallPath = Path.GetDirectoryName(acfFile)
                            };

                            string posterPath = Path.Combine(libraryCachePath, $"{game.AppId}_header.jpg");
                            if (File.Exists(posterPath))
                            {
                                game.PosterUrl = posterPath;
                            }

                            if (!foundGames.ContainsKey(game.Name))
                            {
                                foundGames.Add(game.Name, game);
                            }
                        }
                    }
                }
            }
            catch (Exception) {  }
        }

        private string? GetSteamInstallPath()
        {
            string[] paths = { @"SOFTWARE\WOW6432Node\Valve\Steam", @"SOFTWARE\Valve\Steam" };
            foreach (var path in paths)
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(path);
                if (key != null) return key.GetValue("InstallPath") as string;
            }
            return null;
        }

        private void ScanEpicGames(Dictionary<string, Game> foundGames)
        {
            try
            {
                string manifestsPath = @"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests";
                if (!Directory.Exists(manifestsPath)) return;

                foreach (string filePath in Directory.GetFiles(manifestsPath, "*.item"))
                {
                    string content = File.ReadAllText(filePath);
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.TryGetProperty("DisplayName", out var nameElement) &&
                        doc.RootElement.TryGetProperty("InstallLocation", out var pathElement))
                    {
                        string? gameName = nameElement.GetString();
                        if (!string.IsNullOrEmpty(gameName) && !gameName.Equals("UEFN"))
                        {
                            var game = new Game
                            {
                                Name = gameName,
                                InstallPath = pathElement.GetString()
                            };
                            if (!foundGames.ContainsKey(game.Name))
                            {
                                foundGames.Add(game.Name, game);
                            }
                        }
                    }
                }
            }
            catch (Exception) {  }
        }
    }
}