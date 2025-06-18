using Microsoft.Win32;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Launcher.Models;

namespace Launcher.Services
{
    public class GameScanner
    {
        private readonly string _rawgApiKey = "9a0b37f8df08460aa625a3eef04a4baa"; // Ваш ключ API
        private readonly HttpClient _httpClient;
        private readonly string _cacheFilePath;
        private Dictionary<string, string> _posterCache = new();

        public GameScanner()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "WPF-Game-Launcher");

            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appFolderPath = Path.Combine(appDataPath, "WPFGameLauncher");
            Directory.CreateDirectory(appFolderPath);
            _cacheFilePath = Path.Combine(appFolderPath, "poster_cache.json");

            LoadCache();
        }

        private void LoadCache()
        {
            try
            {
                if (File.Exists(_cacheFilePath))
                {
                    string json = File.ReadAllText(_cacheFilePath);
                    _posterCache = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                                   ?? new Dictionary<string, string>();
                }
            }
            catch
            {
                _posterCache = new Dictionary<string, string>();
            }
        }

        private void SaveCache()
        {
            try
            {
                string json = JsonSerializer.Serialize(_posterCache, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_cacheFilePath, json);
            }
            catch { /* Игнорируем ошибки сохранения кэша */ }
        }

        public async Task<List<Game>> ScanAllAsync()
        {
            var uniqueGames = new Dictionary<string, Game>(StringComparer.OrdinalIgnoreCase);

            // Сканирование игр из разных источников
            ScanSteamGames(uniqueGames);
            ScanEpicGames(uniqueGames);
            ScanUbisoftGames(uniqueGames);
            ScanXboxGames(uniqueGames);
            ScanGaijinGames(uniqueGames);
            ScanBattleNetByFolders(uniqueGames);

            // Асинхронная загрузка метаданных (включая изображения) для всех найденных игр
            var tasks = uniqueGames.Values.Select(FetchGameMetadataAsync).ToList();
            await Task.WhenAll(tasks);

            SaveCache(); // Сохраняем кэш после всех операций
            return uniqueGames.Values.OrderBy(g => g.Name).ToList();
        }

        private async Task FetchGameMetadataAsync(Game game)
        {
            // Создаем уникальный ключ для кэша, чтобы избежать коллизий
            string cacheKey = !string.IsNullOrEmpty(game.AppId) && game.Source == "Steam"
                ? $"steam_{game.AppId}"
                : $"rawg_{game.Name}";

            // Проверяем, есть ли изображение в кэше
            if (_posterCache.TryGetValue(cacheKey, out var cachedUrl) && !string.IsNullOrEmpty(cachedUrl))
            {
                game.PosterUrl = cachedUrl;
                game.CoverArtPath = cachedUrl;
                return;
            }

            string? imageUrl = null;

            // Для игр Steam используем их официальные CDN для получения изображений
            if (game.Source == "Steam" && !string.IsNullOrEmpty(game.AppId))
            {
                // Предпочтительный URL - вертикальный постер
                string steamPosterUrl = $"https://cdn.akamai.steamstatic.com/steam/apps/{game.AppId}/library_600x900.jpg";

                try
                {
                    // Проверяем доступность постера через HEAD-запрос, чтобы не качать всё изображение
                    using var request = new HttpRequestMessage(HttpMethod.Head, steamPosterUrl);
                    using var response = await _httpClient.SendAsync(request);

                    if (response.IsSuccessStatusCode)
                    {
                        imageUrl = steamPosterUrl;
                    }
                    else
                    {
                        // Если постер недоступен, пробуем получить заголовок (горизонтальное изображение)
                        imageUrl = $"https://cdn.akamai.steamstatic.com/steam/apps/{game.AppId}/header.jpg";
                    }
                }
                catch
                {
                    // В случае сетевой ошибки, ищем через API как запасной вариант
                    imageUrl = await GetPosterFromApi(game.Name);
                }
            }
            else
            {
                // Для всех остальных игр (Epic, Ubisoft и т.д.) ищем через RAWG API
                imageUrl = await GetPosterFromApi(game.Name);
            }

            // Если URL получен, присваиваем его свойствам игры и сохраняем в кэш
            if (!string.IsNullOrEmpty(imageUrl))
            {
                game.PosterUrl = imageUrl;
                game.CoverArtPath = imageUrl;
                _posterCache[cacheKey] = imageUrl;
            }
        }


        private async Task<string?> GetPosterFromApi(string gameName)
        {
            try
            {
                // Очистка названия игры от лишних символов для более точного поиска
                string cleanedName = Regex.Replace(
                        gameName,
                        @"™|®|©|:.*Edition|:.*of the Year.*",
                        "",
                        RegexOptions.IgnoreCase)
                    .Trim();

                string searchUrl = $"https://api.rawg.io/api/games?key={_rawgApiKey}&search={Uri.EscapeDataString(cleanedName)}&page_size=1";
                HttpResponseMessage response = await _httpClient.GetAsync(searchUrl);

                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(jsonResponse);
                    if (doc.RootElement.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
                    {
                        var firstResult = results[0];
                        if (firstResult.TryGetProperty("background_image", out var imageUrlElement))
                        {
                            return imageUrlElement.GetString();
                        }
                    }
                }
            }
            catch { /* Игнорируем ошибки API */ }
            return null;
        }

        private void ScanSteamGames(Dictionary<string, Game> foundGames)
        {
            try
            {
                string? steamInstallPath = GetSteamInstallPath();
                if (string.IsNullOrEmpty(steamInstallPath)) return;

                var libraryPaths = new List<string> { steamInstallPath };

                string libraryFoldersVdfPath = Path.Combine(steamInstallPath, "steamapps", "libraryfolders.vdf");
                if (File.Exists(libraryFoldersVdfPath))
                {
                    string content = File.ReadAllText(libraryFoldersVdfPath);
                    foreach (Match m in Regex.Matches(content, @"\""path\""\s+\""(.*?)\"""))
                    {
                        string path = m.Groups[1].Value.Replace(@"\\", @"\");
                        if (Directory.Exists(path)) libraryPaths.Add(path);
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
                                continue;

                            var game = new Game
                            {
                                Name = gameName,
                                AppId = appidMatch.Groups[1].Value,
                                InstallPath = Path.GetDirectoryName(acfFile),
                                Source = "Steam"
                            };
                            if (!foundGames.ContainsKey(game.Name))
                                foundGames.Add(game.Name, game);
                        }
                    }
                }
            }
            catch { }
        }

        private string? GetSteamInstallPath()
        {
            string[] paths =
            {
                @"SOFTWARE\WOW6432Node\Valve\Steam",
                @"SOFTWARE\Valve\Steam"
            };
            foreach (var path in paths)
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(path);
                if (key != null) return key.GetValue("InstallPath") as string;
            }
            return null;
        }

        private void ScanGaijinGames(Dictionary<string, Game> foundGames)
        {
            try
            {
                // Поиск в реестре HKEY_LOCAL_MACHINE
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Gaijin"))
                {
                    if (key != null)
                    {
                        foreach (string subKeyName in key.GetSubKeyNames())
                        {
                            using (var gameKey = key.OpenSubKey(subKeyName))
                            {
                                if (gameKey != null)
                                {
                                    string installPath = gameKey.GetValue("InstallPath") as string ??
                                                         gameKey.GetValue("game_path") as string ??
                                                         gameKey.GetValue("Path") as string;

                                    if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
                                    {
                                        string gameName = GetGaijinGameName(subKeyName, installPath);
                                        if (!string.IsNullOrEmpty(gameName))
                                        {
                                            var game = new Game
                                            {
                                                Name = gameName,
                                                InstallPath = installPath,
                                                Source = "Gaijin Launcher"
                                            };
                                            if (!foundGames.ContainsKey(game.Name))
                                                foundGames.Add(game.Name, game);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                // Поиск в реестре HKEY_CURRENT_USER
                using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Gaijin"))
                {
                    if (key != null)
                    {
                        foreach (string subKeyName in key.GetSubKeyNames())
                        {
                            using (var gameKey = key.OpenSubKey(subKeyName))
                            {
                                if (gameKey != null)
                                {
                                    string installPath = gameKey.GetValue("InstallPath") as string ??
                                                         gameKey.GetValue("game_path") as string ??
                                                         gameKey.GetValue("Path") as string;

                                    if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
                                    {
                                        string gameName = GetGaijinGameName(subKeyName, installPath);
                                        if (!string.IsNullOrEmpty(gameName))
                                        {
                                            var game = new Game
                                            {
                                                Name = gameName,
                                                InstallPath = installPath,
                                                Source = "Gaijin Launcher"
                                            };
                                            if (!foundGames.ContainsKey(game.Name))
                                                foundGames.Add(game.Name, game);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                // Поиск через общий реестр uninstall
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                {
                    if (key != null)
                    {
                        foreach (string subKeyName in key.GetSubKeyNames())
                        {
                            using (var subKey = key.OpenSubKey(subKeyName))
                            {
                                if (subKey != null)
                                {
                                    string displayName = subKey.GetValue("DisplayName") as string;
                                    string publisher = subKey.GetValue("Publisher") as string;
                                    string installLocation = subKey.GetValue("InstallLocation") as string;

                                    if (!string.IsNullOrEmpty(displayName) && !string.IsNullOrEmpty(installLocation) &&
                                        (publisher?.Contains("Gaijin") == true || displayName.Contains("War Thunder") ||
                                         displayName.Contains("Crossout") || displayName.Contains("Star Conflict")) &&
                                        Directory.Exists(installLocation))
                                    {
                                        var game = new Game
                                        {
                                            Name = displayName,
                                            InstallPath = installLocation,
                                            Source = "Gaijin Launcher"
                                        };
                                        if (!foundGames.ContainsKey(game.Name))
                                            foundGames.Add(game.Name, game);
                                    }
                                }
                            }
                        }
                    }
                }

                // Поиск в стандартных путях установки
                string[] gaijinPaths = {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "War Thunder"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "War Thunder"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Crossout"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Crossout"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Star Conflict"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Star Conflict"),
            @"C:\Games\War Thunder",
            @"D:\Games\War Thunder",
            @"E:\Games\War Thunder"
        };

                foreach (string path in gaijinPaths)
                {
                    if (Directory.Exists(path))
                    {
                        // Проверяем наличие лаунчера или исполняемого файла
                        var exeFiles = Directory.GetFiles(path, "*.exe", SearchOption.TopDirectoryOnly)
                            .Where(f => Path.GetFileName(f).ToLower().Contains("launcher") ||
                                        Path.GetFileName(f).ToLower().Contains("aces") ||
                                        Path.GetFileName(f).ToLower().Contains("crossout") ||
                                        Path.GetFileName(f).ToLower().Contains("star"));

                        if (exeFiles.Any())
                        {
                            string gameName = Path.GetFileName(path);
                            var game = new Game
                            {
                                Name = gameName,
                                InstallPath = path,
                                Source = "Gaijin Launcher"
                            };
                            if (!foundGames.ContainsKey(game.Name))
                                foundGames.Add(game.Name, game);
                        }
                    }
                }

                // Поиск в AppData
                string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gaijin");
                if (Directory.Exists(appDataPath))
                {
                    foreach (string gameDir in Directory.GetDirectories(appDataPath))
                    {
                        string gameName = Path.GetFileName(gameDir);
                        // Ищем конфигурационные файлы или другие признаки установленной игры
                        if (Directory.GetFiles(gameDir, "config.blk", SearchOption.AllDirectories).Any() ||
                            Directory.GetFiles(gameDir, "*.cfg", SearchOption.AllDirectories).Any())
                        {
                            // Пытаемся найти путь к игре из конфигурационных файлов
                            var configFiles = Directory.GetFiles(gameDir, "config.blk", SearchOption.AllDirectories);
                            foreach (string configFile in configFiles)
                            {
                                try
                                {
                                    string configContent = File.ReadAllText(configFile);
                                    // Простой парсинг для поиска пути установки
                                    var pathMatch = Regex.Match(configContent, @"installPath\s*=\s*""([^""]+)""");
                                    if (pathMatch.Success)
                                    {
                                        string installPath = pathMatch.Groups[1].Value;
                                        if (Directory.Exists(installPath))
                                        {
                                            var game = new Game
                                            {
                                                Name = gameName,
                                                InstallPath = installPath,
                                                Source = "Gaijin Launcher"
                                            };
                                            if (!foundGames.ContainsKey(game.Name))
                                                foundGames.Add(game.Name, game);
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private string GetGaijinGameName(string registryKeyName, string installPath)
        {
            // Определяем название игры по ключу реестра или пути
            string keyLower = registryKeyName.ToLower();
            string pathLower = installPath.ToLower();

            if (keyLower.Contains("warthunder") || pathLower.Contains("war thunder"))
                return "War Thunder";
            else if (keyLower.Contains("crossout") || pathLower.Contains("crossout"))
                return "Crossout";
            else if (keyLower.Contains("starconflict") || pathLower.Contains("star conflict"))
                return "Star Conflict";
            else if (keyLower.Contains("enlisted") || pathLower.Contains("enlisted"))
                return "Enlisted";

            // Если не удалось определить, возвращаем имя ключа
            return registryKeyName;
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
                                InstallPath = pathElement.GetString(),
                                Source = "Epic Games"
                            };
                            if (!foundGames.ContainsKey(game.Name))
                                foundGames.Add(game.Name, game);
                        }
                    }
                }
            }
            catch { }
        }

        private void ScanBattleNetByFolders(Dictionary<string, Game> foundGames)
        {
            try
            {
                // Большой словарь игр Battle.net.
                var battleNetGames = new Dictionary<string, (string FolderName, string ExecutableName)>
        {
            // --- World of Warcraft ---
            { "World of Warcraft", ("World of Warcraft", @"_retail_\Wow.exe") },
            { "World of Warcraft Classic", ("World of Warcraft", @"_classic_\WowClassic.exe") },
            { "WoW Classic Era", ("World of Warcraft", @"_classic_era_\WowClassic.exe") },

            // --- Diablo ---
            { "Diablo IV", ("Diablo IV", "Diablo IV Launcher.exe") },
            { "Diablo III", ("Diablo III", @"x64\Diablo III64.exe") },
            { "Diablo II: Resurrected", ("Diablo II Resurrected", "D2R.exe") },

            // --- Overwatch ---
            { "Overwatch 2", ("Overwatch", @"_retail_\Overwatch.exe") },

            // --- StarCraft ---
            { "StarCraft II", ("StarCraft II", "StarCraft II.exe") },
            { "StarCraft Remastered", ("StarCraft", @"x86_64\StarCraft.exe") },
            
            // --- Warcraft III ---
            { "Warcraft III: Reforged", ("Warcraft III", @"_retail_\Warcraft III.exe") },
            
            // --- Другие игры ---
            { "Hearthstone", ("Hearthstone", "Hearthstone.exe") },
            { "Heroes of the Storm", ("Heroes of the Storm", @"Support64\HeroesOfTheStorm_x64.exe") },
            
            // --- Call of Duty (Обычно ставится в одну папку) ---
            { "Call of Duty", ("Call of Duty", "cod.exe") }
        };

                // Стандартные пути для поиска
                string[] basePaths =
                {
                   Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                   Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                   };

                foreach (string basePath in basePaths)
                {
                    if (!Directory.Exists(basePath)) continue;

                    foreach (var knownGame in battleNetGames)
                    {
                        string gameName = knownGame.Key;

                        // Пропускаем, если уже нашли эту игру
                        if (foundGames.ContainsKey(gameName)) continue;

                        string gameFolder = knownGame.Value.FolderName;
                        string gameExe = knownGame.Value.ExecutableName;

                        string installPath = Path.Combine(basePath, gameFolder);
                        string exePath = Path.Combine(installPath, gameExe);

                        if (File.Exists(exePath))
                        {
                            var game = new Game
                            {
                                Name = gameName,
                                InstallPath = installPath,
                                Source = "Battle.net"
                            };
                            foundGames.Add(game.Name, game);
                        }
                    }
                }
            }
            catch { }
        }
        private void ScanUbisoftGames(Dictionary<string, Game> foundGames)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Ubisoft\Launcher\Installs");
                if (key != null)
                {
                    foreach (string installId in key.GetSubKeyNames())
                    {
                        using var gameKey = key.OpenSubKey(installId);
                        if (gameKey != null)
                        {
                            string? installDir = gameKey.GetValue("InstallDir") as string;
                            if (!string.IsNullOrEmpty(installDir) && Directory.Exists(installDir))
                            {
                                string gameName = Path.GetFileName(installDir);
                                var game = new Game
                                {
                                    Name = gameName,
                                    InstallPath = installDir,
                                    Source = "Ubisoft Connect"
                                };
                                if (!foundGames.ContainsKey(game.Name))
                                    foundGames.Add(game.Name, game);
                            }
                        }
                    }
                }

                string[] ubisoftPaths =
                {
                   Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ubisoft"),
                   Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Ubisoft")
               };

                foreach (string ubisoftPath in ubisoftPaths)
                {
                    if (Directory.Exists(ubisoftPath))
                    {
                        foreach (string gameDir in Directory.GetDirectories(ubisoftPath))
                        {
                            string gameName = Path.GetFileName(gameDir);
                            if (!gameName.Contains("Ubisoft") && !gameName.Contains("Connect"))
                            {
                                var game = new Game
                                {
                                    Name = gameName,
                                    InstallPath = gameDir,
                                    Source = "Ubisoft Connect"
                                };
                                if (!foundGames.ContainsKey(game.Name))
                                    foundGames.Add(game.Name, game);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private void ScanXboxGames(Dictionary<string, Game> foundGames)
        {
            try
            {
                // Список папок, которые нужно исключить (не являются играми)
                string[] excludedFolders = {
            "GameSave", "wgs", ".GamingRoot", "Temp", "Cache",
            "Logs", "Settings", "Config", "Data"
        };

                // Поиск в C:\XboxGames
                string xboxGamesPath = @"C:\XboxGames";
                if (Directory.Exists(xboxGamesPath))
                {
                    foreach (string gameDir in Directory.GetDirectories(xboxGamesPath))
                    {
                        string gameName = Path.GetFileName(gameDir);

                        // Исключаем системные папки
                        if (excludedFolders.Any(folder => gameName.Equals(folder, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        // Проверяем наличие исполняемых файлов
                        var exeFiles = Directory.GetFiles(gameDir, "*.exe", SearchOption.AllDirectories);
                        if (exeFiles.Length > 0)
                        {
                            var game = new Game
                            {
                                Name = gameName,
                                InstallPath = gameDir,
                                Source = "Xbox Game Pass"
                            };
                            if (!foundGames.ContainsKey(game.Name))
                                foundGames.Add(game.Name, game);
                        }
                    }
                }

                // Поиск в WindowsApps с улучшенной фильтрацией
                string windowsAppsPath = @"C:\Program Files\WindowsApps";
                if (Directory.Exists(windowsAppsPath))
                {
                    try
                    {
                        foreach (string appDir in Directory.GetDirectories(windowsAppsPath))
                        {
                            string dirName = Path.GetFileName(appDir);

                            // Более точная фильтрация Xbox игр
                            if ((dirName.Contains("Microsoft") || dirName.Contains("XboxGamePass")) &&
                                !dirName.Contains("VCLibs") &&
                                !dirName.Contains("Framework") &&
                                !dirName.Contains("Runtime") &&
                                !dirName.Contains("Extension") &&
                                !dirName.Contains("Store") &&
                                !excludedFolders.Any(folder => dirName.Contains(folder)))
                            {
                                // Проверяем наличие исполняемых файлов
                                var exeFiles = Directory.GetFiles(appDir, "*.exe", SearchOption.TopDirectoryOnly);
                                if (exeFiles.Length > 0)
                                {
                                    string gameName = Regex.Replace(dirName, @"_[\d\.]+_.*", "");
                                    gameName = gameName.Replace("Microsoft.", "");

                                    var game = new Game
                                    {
                                        Name = gameName,
                                        InstallPath = appDir,
                                        Source = "Xbox Game Pass"
                                    };
                                    if (!foundGames.ContainsKey(game.Name))
                                        foundGames.Add(game.Name, game);
                                }
                            }
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Игнорируем ошибки доступа
                    }
                }

                // Дополнительный поиск в других возможных локациях Xbox
                string[] additionalXboxPaths = {
            @"D:\XboxGames",
            @"E:\XboxGames",
            @"C:\Games"
        };

                foreach (string path in additionalXboxPaths)
                {
                    if (Directory.Exists(path))
                    {
                        foreach (string gameDir in Directory.GetDirectories(path))
                        {
                            string gameName = Path.GetFileName(gameDir);

                            if (!excludedFolders.Any(folder => gameName.Equals(folder, StringComparison.OrdinalIgnoreCase)))
                            {
                                var exeFiles = Directory.GetFiles(gameDir, "*.exe", SearchOption.AllDirectories);
                                if (exeFiles.Length > 0)
                                {
                                    var game = new Game
                                    {
                                        Name = gameName,
                                        InstallPath = gameDir,
                                        Source = "Xbox Game Pass"
                                    };
                                    if (!foundGames.ContainsKey(game.Name))
                                        foundGames.Add(game.Name, game);
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }
    }
}