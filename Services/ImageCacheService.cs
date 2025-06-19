// Файл: Services/ImageCacheService.cs
using System;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Launcher.Services
{
    public class ImageCacheService
    {
        private readonly HttpClient _httpClient;
        private readonly string _cacheDirectory;

        public ImageCacheService()
        {
            _httpClient = new HttpClient();
            // Путь к папке кэша: %APPDATA%/WPFGameLauncher/ImageCache
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _cacheDirectory = Path.Combine(appDataPath, "WPFGameLauncher", "ImageCache");

            // Создаем папку, если ее нет
            Directory.CreateDirectory(_cacheDirectory);
        }

        /// <summary>
        /// Получает локальный путь к изображению. Если его нет в кэше, скачивает его.
        /// </summary>
        /// <param name="imageUrl">URL изображения для скачивания.</param>
        /// <param name="gameName">Имя игры для создания уникального имени файла.</param>
        /// <param name="imageType">Тип изображения ("poster" или "hero").</param>
        /// <returns>Локальный путь к файлу или null, если скачивание не удалось.</returns>
        public async Task<string?> GetLocalImagePathAsync(string? imageUrl, string gameName, string imageType)
        {
            if (string.IsNullOrEmpty(imageUrl) || string.IsNullOrEmpty(gameName))
            {
                return null;
            }

            try
            {
                // Создаем безопасное и уникальное имя файла
                string safeFileName = SanitizeFileName($"{gameName}-{imageType}{Path.GetExtension(imageUrl)}");
                string localFilePath = Path.Combine(_cacheDirectory, safeFileName);

                // Если файл уже есть в кэше, просто возвращаем путь к нему
                if (File.Exists(localFilePath))
                {
                    return localFilePath;
                }

                // Если файла нет, скачиваем его
                byte[] imageBytes = await _httpClient.GetByteArrayAsync(imageUrl);
                await File.WriteAllBytesAsync(localFilePath, imageBytes);

                return localFilePath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка кэширования изображения {imageUrl}: {ex.Message}");
                return null;
            }
        }

        private string SanitizeFileName(string fileName)
        {
            // Убираем недопустимые символы из имени файла
            string invalidChars = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
            Regex r = new Regex(string.Format("[{0}]", Regex.Escape(invalidChars)));
            return r.Replace(fileName, "");
        }
    }
}