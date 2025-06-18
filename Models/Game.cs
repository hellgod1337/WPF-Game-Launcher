namespace Launcher.Models
{
    public class Game
    {
        public string? Name { get; set; }
        public string? InstallPath { get; set; }
        public string? ExecutablePath { get; set; }
        public string? Source { get; set; } // Например, "Steam", "Epic Games"
        public string? AppId { get; set; } // Steam App ID
        public string? PosterUrl { get; set; } // URL постера для фона
        public string? CoverArtPath { get; set; } // Локальный путь к обложке для GridView

    }
}