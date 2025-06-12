using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;


namespace Launcher.Views
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<Game> Games { get; set; }
        private readonly GameScanner _scanner;

        public MainWindow()
        {
            InitializeComponent();
            Games = new ObservableCollection<Game>();
            _scanner = new GameScanner();
            GameListBox.ItemsSource = Games;
        }

        private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
        {
            await RefreshGamesAsync();
        }

        private async void RefreshGamesButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshGamesAsync();
        }

        private async Task RefreshGamesAsync()
        {
            RefreshGamesButton.IsEnabled = false;

            Games.Clear();
            DetailsPanel.Visibility = Visibility.Hidden;
            PlaceholderPanel.Visibility = Visibility.Visible;

            var foundGames = await Task.Run(() => _scanner.ScanAll());

            foreach (var game in foundGames)
            {
                Games.Add(game);
            }

            RefreshGamesButton.IsEnabled = true;
        }

        private void GameListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GameListBox.SelectedItem is Game selectedGame)
            {
                PlaceholderPanel.Visibility = Visibility.Hidden;
                DetailsPanel.Visibility = Visibility.Visible;

                GameTitleTextBlock.Text = selectedGame.Name;
                AppIdTextBlock.Text = selectedGame.AppId ?? "Не найдено";
                InstallPathTextBlock.Text = selectedGame.InstallPath ?? "Не найдено";

                if (!string.IsNullOrEmpty(selectedGame.PosterUrl))
                {
                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(selectedGame.PosterUrl);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        GamePosterImage.Source = bitmap;
                    }
                    catch
                    {
                        GamePosterImage.Source = null;
                    }
                }
                else
                {
                    GamePosterImage.Source = null;
                }
            }
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (GameListBox.SelectedItem is not Game selectedGame || string.IsNullOrEmpty(selectedGame.AppId))
            {
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo($"steam://run/{selectedGame.AppId}") { UseShellExecute = true });
                this.WindowState = WindowState.Minimized;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось запустить игру. Ошибка: {ex.Message}", "Ошибка запуска", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}