// Launcher/Services/BatchImageDownloader.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Launcher.Models;

namespace Launcher.Services
{
    public class DownloadProgressReport
    {
        public int Processed { get; set; }
        public int Total { get; set; }
    }

    public class BatchImageDownloader
    {
        private readonly ImageCacheService _imageCacheService;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(10);

        public BatchImageDownloader(ImageCacheService imageCacheService)
        {
            _imageCacheService = imageCacheService;
        }

        public async Task DownloadAllImagesAsync(IEnumerable<Game> games, IProgress<DownloadProgressReport> progress)
        {
            var gameList = games.ToList();
            var downloadActions = new List<Func<Task>>();

            foreach (var game in gameList)
            {
                if (!string.IsNullOrEmpty(game.PosterUrl))
                {
                    downloadActions.Add(() => DownloadAndAssignImagePath(game, "poster", game.PosterUrl));
                }

                foreach (var heroUrl in game.HeroUrls)
                {
                    if (!string.IsNullOrEmpty(heroUrl))
                    {
                        downloadActions.Add(() => DownloadAndAssignImagePath(game, "hero", heroUrl));
                    }
                }
            }

            if (downloadActions.Count == 0)
            {
                progress.Report(new DownloadProgressReport { Processed = 0, Total = 0 });
                return;
            }

            int totalDownloads = downloadActions.Count;
            int completedDownloads = 0;

            var downloadTasks = downloadActions.Select(async action =>
            {
                await _semaphore.WaitAsync();
                try
                {
                    await action();
                }
                finally
                {
                    _semaphore.Release();
                    int currentCount = Interlocked.Increment(ref completedDownloads);
                    progress.Report(new DownloadProgressReport { Processed = currentCount, Total = totalDownloads });
                }
            }).ToList();

            await Task.WhenAll(downloadTasks);
        }

        private async Task DownloadAndAssignImagePath(Game game, string type, string url)
        {
            try
            {
                if (string.IsNullOrEmpty(game.Name)) return;

                string? localPath = await _imageCacheService.GetLocalImagePathAsync(url, game.Name, type);
                if (string.IsNullOrEmpty(localPath)) return;

                if (type == "poster")
                {
                    game.LocalPosterPath = localPath;
                    game.CoverArtPath = localPath;
                }
                else // type == "hero"
                {
                    // Потокобезопасное добавление в список
                    lock (game.LocalHeroPaths)
                    {
                        if (!game.LocalHeroPaths.Contains(localPath))
                        {
                            game.LocalHeroPaths.Add(localPath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка при скачивании {type} для {game.Name}: {ex.Message}");
            }
        }
    }
}