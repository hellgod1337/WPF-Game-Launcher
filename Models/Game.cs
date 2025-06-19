namespace Launcher.Models
{
    public class Game
    {
        public string? Name { get; set; }
        public string? InstallPath { get; set; }
        public string? ExecutablePath { get; set; }
        public string? Source { get; set; } // Например, "Steam", "Epic Games"
        public string? AppId { get; set; } // Steam App ID

        // URL-адреса, получаемые от сканера
        public string? PosterUrl { get; set; }
        public string? HeroUrl { get; set; }

        // НОВЫЕ СВОЙСТВА: Локальные пути к кэшированным файлам
        public string? LocalPosterPath { get; set; }
        public string? LocalHeroPath { get; set; }

        // Это свойство можно оставить для совместимости или для локального пути
        public string? CoverArtPath { get; set; }

        // НОВОЕ СВОЙСТВО
        public DateTime? LastPlayed { get; set; }

    }
}