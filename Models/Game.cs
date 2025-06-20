namespace Launcher.Models
{
    public class Game
    {
        public string? Name { get; set; }
        public string? InstallPath { get; set; }
        public string? ExecutablePath { get; set; }
        public string? Source { get; set; }
        public string? AppId { get; set; }
        public DateTime? LastPlayed { get; set; }

        // URL постера (остается один)
        public string? PosterUrl { get; set; }

        // Локальный путь к постеру (остается один)
        public string? LocalPosterPath { get; set; }

        // URL-ы для фонов теперь в виде списка
        public List<string> HeroUrls { get; set; } = new List<string>();

        // Локальные пути к фонам тоже в виде списка
        public List<string> LocalHeroPaths { get; set; } = new List<string>();

        // Свойство для совместимости (используется для постера в GridView)
        public string? CoverArtPath { get; set; }

        // --- ДОБАВЛЯЕМ НОВОЕ СВОЙСТВО ---
        // Оно будет хранить индекс текущего фона для этой конкретной игры.
        // Инициализируем его нулем по умолчанию.
        public int SelectedBackgroundIndex { get; set; } = 0;
    }
}