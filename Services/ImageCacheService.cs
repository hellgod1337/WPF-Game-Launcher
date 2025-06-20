using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Launcher.Services
{
    public class ImageCacheService
    {
        private readonly HttpClient _httpClient;
        private readonly string _cacheDirectory;
        private readonly Dictionary<string, Task<string?>> _pendingDownloads;

        public ImageCacheService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "WPF-Game-Launcher/1.0");

            // Создаем папку кэша
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _cacheDirectory = Path.Combine(appDataPath, "WPFGameLauncher", "ImageCache");
            Directory.CreateDirectory(_cacheDirectory);

            _pendingDownloads = new Dictionary<string, Task<string?>>();

            Debug.WriteLine($"=== ImageCacheService инициализирован ===");
            Debug.WriteLine($"Папка кэша: {_cacheDirectory}");
        }

        /// <summary>
        /// Получает локальный путь к изображению, загружая его при необходимости
        /// </summary>
        public async Task<string?> GetLocalImagePathAsync(string? imageUrl, string? gameName, string imageType)
        {
            if (string.IsNullOrEmpty(imageUrl))
            {
                Debug.WriteLine($"URL изображения пустой для игры '{gameName}', тип: {imageType}");
                return null;
            }

            try
            {
                Debug.WriteLine($"=== Обработка изображения для '{gameName}' ===");
                Debug.WriteLine($"URL: {imageUrl}");
                Debug.WriteLine($"Тип: {imageType}");

                // Создаем безопасное имя файла
                string fileName = GenerateFileName(imageUrl, gameName, imageType);
                string localPath = Path.Combine(_cacheDirectory, fileName);

                Debug.WriteLine($"Локальный путь: {localPath}");

                // Проверяем, существует ли файл локально
                if (File.Exists(localPath))
                {
                    Debug.WriteLine($"Файл найден в кэше: {localPath}");
                    return localPath;
                }

                Debug.WriteLine($"Файл не найден в кэше, начинаем загрузку...");

                // Избегаем дублирующихся загрузок
                if (_pendingDownloads.ContainsKey(localPath))
                {
                    Debug.WriteLine($"Загрузка уже в процессе, ожидаем...");
                    return await _pendingDownloads[localPath];
                }

                // Запускаем загрузку
                var downloadTask = DownloadImageAsync(imageUrl, localPath);
                _pendingDownloads[localPath] = downloadTask;

                try
                {
                    var result = await downloadTask;
                    Debug.WriteLine($"Загрузка завершена: {(result != null ? "УСПЕШНО" : "ОШИБКА")}");
                    return result;
                }
                finally
                {
                    _pendingDownloads.Remove(localPath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ОШИБКА в GetLocalImagePathAsync: {ex.Message}");
                return null;
            }
        }

        private async Task<string?> DownloadImageAsync(string imageUrl, string localPath)
        {
            try
            {
                Debug.WriteLine($"Начинаем загрузку с {imageUrl}");

                using var response = await _httpClient.GetAsync(imageUrl);

                Debug.WriteLine($"HTTP статус: {response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"ОШИБКА HTTP: {response.StatusCode} - {response.ReasonPhrase}");
                    return null;
                }

                var contentType = response.Content.Headers.ContentType?.MediaType;
                Debug.WriteLine($"Content-Type: {contentType}");

                // Проверяем, что это действительно изображение
                if (contentType != null && !contentType.StartsWith("image/"))
                {
                    Debug.WriteLine($"ПРЕДУПРЕЖДЕНИЕ: Получен не-изображение контент: {contentType}");
                }

                using var imageStream = await response.Content.ReadAsStreamAsync();

                // Создаем директорию, если её нет
                var directory = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using var fileStream = new FileStream(localPath, FileMode.Create, FileAccess.Write);
                await imageStream.CopyToAsync(fileStream);
                await fileStream.FlushAsync();

                var fileInfo = new FileInfo(localPath);
                Debug.WriteLine($"Файл сохранен: {localPath}");
                Debug.WriteLine($"Размер файла: {fileInfo.Length} байт");

                // Проверяем, что файл действительно сохранился
                if (fileInfo.Length > 0)
                {
                    Debug.WriteLine($"УСПЕХ: Изображение загружено и сохранено");
                    return localPath;
                }
                else
                {
                    Debug.WriteLine($"ОШИБКА: Файл сохранился с размером 0 байт");
                    File.Delete(localPath);
                    return null;
                }
            }
            catch (HttpRequestException httpEx)
            {
                Debug.WriteLine($"ОШИБКА HTTP запроса: {httpEx.Message}");
                return null;
            }
            catch (IOException ioEx)
            {
                Debug.WriteLine($"ОШИБКА ввода/вывода: {ioEx.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"НЕОЖИДАННАЯ ОШИБКА: {ex.Message}");
                return null;
            }
        }

        private string GenerateFileName(string imageUrl, string? gameName, string imageType)
        {
            // Создаем уникальный хэш URL для предотвращения конфликтов имен файлов
            using var sha1 = SHA1.Create();
            var urlHash = sha1.ComputeHash(Encoding.UTF8.GetBytes(imageUrl));
            var hashString = Convert.ToHexString(urlHash)[..8]; // Первые 8 символов

            // Безопасное имя игры (убираем недопустимые символы)
            string safeGameName = gameName ?? "unknown";
            safeGameName = string.Join("_", safeGameName.Split(Path.GetInvalidFileNameChars()));
            if (safeGameName.Length > 50) safeGameName = safeGameName[..50];

            // Определяем расширение файла по URL
            string extension = GetExtensionFromUrl(imageUrl);

            return $"{safeGameName}_{imageType}_{hashString}{extension}";
        }

        private string GetExtensionFromUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                var path = uri.AbsolutePath;
                var extension = Path.GetExtension(path);

                // Если расширение не найдено или неподдерживаемое, используем .jpg
                if (string.IsNullOrEmpty(extension) ||
                    (!extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
                     !extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) &&
                     !extension.Equals(".png", StringComparison.OrdinalIgnoreCase) &&
                     !extension.Equals(".webp", StringComparison.OrdinalIgnoreCase)))
                {
                    return ".jpg";
                }

                return extension.ToLowerInvariant();
            }
            catch
            {
                return ".jpg";
            }
        }

        /// <summary>
        /// Очищает кэш изображений старше указанного количества дней
        /// </summary>
        public void ClearOldCache(int daysOld = 30)
        {
            try
            {
                var cutoffDate = DateTime.Now.AddDays(-daysOld);
                var files = Directory.GetFiles(_cacheDirectory);

                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.LastWriteTime < cutoffDate)
                    {
                        File.Delete(file);
                        Debug.WriteLine($"Удален старый файл кэша: {file}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка при очистке кэша: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}