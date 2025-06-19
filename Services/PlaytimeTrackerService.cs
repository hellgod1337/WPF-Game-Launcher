using System.IO;
using System.Text.Json;

namespace Launcher.Services
{
    public class PlaytimeTrackerService
    {
        private readonly string _filePath;
        private Dictionary<string, DateTime> _playtimes;

        public PlaytimeTrackerService()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appFolderPath = Path.Combine(appDataPath, "WPFGameLauncher");
            Directory.CreateDirectory(appFolderPath);
            _filePath = Path.Combine(appFolderPath, "playtimes.json");

            LoadPlaytimes();
        }

        private void LoadPlaytimes()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    string json = File.ReadAllText(_filePath);
                    _playtimes = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(json) ?? new Dictionary<string, DateTime>();
                }
                else
                {
                    _playtimes = new Dictionary<string, DateTime>();
                }
            }
            catch
            {
                _playtimes = new Dictionary<string, DateTime>();
            }
        }

        private void SavePlaytimes()
        {
            try
            {
                string json = JsonSerializer.Serialize(_playtimes, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка сохранения времени игры: {ex.Message}");
            }
        }

        /// <summary>
        /// Получает время последнего запуска для указанной игры.
        /// </summary>
        public DateTime? GetLastPlaytime(string gameName)
        {
            if (string.IsNullOrEmpty(gameName)) return null;

            return _playtimes.TryGetValue(gameName, out DateTime lastPlayed) ? lastPlayed : null;
        }

        /// <summary>
        /// Обновляет время последнего запуска для игры на текущее и сохраняет в файл.
        /// </summary>
        public void UpdatePlaytime(string gameName)
        {
            if (string.IsNullOrEmpty(gameName)) return;

            _playtimes[gameName] = DateTime.Now;
            SavePlaytimes();
        }
    }
}