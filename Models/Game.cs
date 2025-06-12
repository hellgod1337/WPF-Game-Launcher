namespace Launcher
{
    public class Game
    {
        public string Name { get; set; } = "Название не найдено";
        public string? InstallPath { get; set; }
        public string? AppId { get; set; }
        public string? PosterUrl { get; set; } // Может быть локальным путем или URL
    }
}