using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Launcher.Models;
using Launcher.Services;
using MaterialDesignThemes.Wpf;

namespace Launcher.Views
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<Game> Games { get; set; }
        private readonly GameScanner _scanner;
        private readonly ImageCacheService _imageCacheService;
        private ICollectionView? _gamesView;
        private readonly PlaytimeTrackerService _playtimeTracker;
        private bool isGridView = false;


        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minimumWorkingSetSize, IntPtr maximumWorkingSetSize);

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            Games = new ObservableCollection<Game>();
            _scanner = new GameScanner();
            _imageCacheService = new ImageCacheService();
            _playtimeTracker = new PlaytimeTrackerService();

            _gamesView = CollectionViewSource.GetDefaultView(Games);
            _gamesView.Filter = FilterGames;
            GameListBox.ItemsSource = _gamesView;

            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CornerRadius = new CornerRadius(25),
                GlassFrameThickness = new Thickness(0)
            });
        }

        private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
        {
            await RefreshGamesAsync();
            if (FilterComboBox.Items.Count > 0)
            {
                FilterComboBox.SelectedIndex = 0;
            }
        }

        private async void RefreshGamesButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshGamesAsync();
        }

        private async Task RefreshGamesAsync()
        {
            RefreshGamesButton.IsEnabled = false;
            ToggleViewButton.IsEnabled = false;
            SearchTextBox.IsEnabled = false;
            FilterComboBox.IsEnabled = false;
            SearchTextBox.Text = string.Empty;

            Games.Clear();
            DetailsPanel.Visibility = Visibility.Hidden;
            PlaceholderPanel.Visibility = Visibility.Visible;

            // Сбрасываем фон
            ChangeBackgroundImage(null);

            var foundGames = await Task.Run(() => _scanner.ScanAllAsync());
            foreach (var game in foundGames)
            {
                // Присваиваем значение из нашего нового сервиса
                if (game.Name != null)
                {
                    game.LastPlayed = _playtimeTracker.GetLastPlaytime(game.Name);
                }
                Games.Add(game);
            }

            UpdateGameCount();

            RefreshGamesButton.IsEnabled = true;
            ToggleViewButton.IsEnabled = true;
            SearchTextBox.IsEnabled = true;
            FilterComboBox.IsEnabled = true;
        }

        private bool FilterGames(object item)
        {
            if (item is not Game game) return false;

            bool platformMatch = true;
            if (FilterComboBox.SelectedItem is ComboBoxItem selectedPlatformItem)
            {
                string filterPlatform = selectedPlatformItem.Content.ToString();
                if (filterPlatform != "Все платформы")
                {
                    platformMatch = game.Source != null &&
                                    string.Equals(game.Source, filterPlatform, StringComparison.OrdinalIgnoreCase);
                }
            }

            bool searchMatch = string.IsNullOrEmpty(SearchTextBox.Text) ||
                               (game.Name != null && game.Name.Contains(SearchTextBox.Text, StringComparison.OrdinalIgnoreCase));

            return platformMatch && searchMatch;
        }

        private void UpdateGameCount()
        {
            int count = _gamesView?.Cast<object>().Count() ?? 0;
            string text = count.ToString();
            int lastDigit = count % 10;
            int lastTwoDigits = count % 100;
            if (lastTwoDigits >= 11 && lastTwoDigits <= 19) { GameCountTextBlock.Text = text + " игр"; }
            else if (lastDigit == 1) { GameCountTextBlock.Text = text + " игра"; }
            else if (lastDigit >= 2 && lastDigit <= 4) { GameCountTextBlock.Text = text + " игры"; }
            else { GameCountTextBlock.Text = text + " игр"; }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _gamesView?.Refresh();
            UpdateGameCount();
        }

        private void FilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _gamesView?.Refresh();
            UpdateGameCount();
        }

        private void ToggleViewButton_Click(object? sender, RoutedEventArgs? e)
        {
            isGridView = !isGridView;
            var viewIcon = (PackIcon)ToggleViewButton.Content;

            if (isGridView)
            {
                GameListBox.ItemsPanel = (ItemsPanelTemplate)FindResource("GridViewPanelTemplate");
                GameListBox.ItemTemplate = (DataTemplate)FindResource("GridViewItemTemplate");
                ScrollViewer.SetHorizontalScrollBarVisibility(GameListBox, ScrollBarVisibility.Disabled);
                viewIcon.Kind = PackIconKind.ViewList;
            }
            else
            {
                GameListBox.ItemsPanel = (ItemsPanelTemplate)FindResource("ListViewPanelTemplate");
                GameListBox.ItemTemplate = (DataTemplate)FindResource("ListViewItemTemplate");
                viewIcon.Kind = PackIconKind.ViewGrid;
            }
        }

        // 1. ВСТАВЬТЕ ОБНОВЛЕННУЮ ВЕРСИЮ ЭТОГО МЕТОДА

        private async void GameListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GameListBox.SelectedItem is not Game selectedGame) return;

            // --- Обновление текстовой информации ---
            DetailsPanel.BeginAnimation(UIElement.OpacityProperty, null);
            if (DetailsPanel.Opacity > 0) { DetailsPanel.Opacity = 0; }

            GameTitleTextBlock.Text = selectedGame.Name;
            InstallPathTextBlock.Text = selectedGame.InstallPath ?? "Не найден";
            SourceTextBlock.Text = selectedGame.Source ?? "Неизвестно";

            // Используем новый метод для форматирования даты
            LastPlayedTextBlock.Text = FormatLastPlayed(selectedGame.LastPlayed);

            bool isValidSteamId = !string.IsNullOrEmpty(selectedGame.AppId) && selectedGame.AppId.All(char.IsDigit);
            SteamIdPanel.Visibility = selectedGame.Source == "Steam" && isValidSteamId ? Visibility.Visible : Visibility.Collapsed;
            if (isValidSteamId) AppIdTextBlock.Text = selectedGame.AppId;


            // --- Асинхронная логика загрузки изображений из кэша ---

            selectedGame.LocalHeroPath = await _imageCacheService.GetLocalImagePathAsync(selectedGame.HeroUrl, selectedGame.Name, "hero");
            selectedGame.LocalPosterPath = await _imageCacheService.GetLocalImagePathAsync(selectedGame.PosterUrl, selectedGame.Name, "poster");

            if (!string.IsNullOrEmpty(selectedGame.LocalPosterPath))
            {
                GamePosterImage.Source = CreateBitmapFromFile(selectedGame.LocalPosterPath);
            }
            else
            {
                GamePosterImage.Source = null;
            }

            string backgroundPath = selectedGame.LocalHeroPath ?? selectedGame.LocalPosterPath;
            if (!string.IsNullOrEmpty(backgroundPath))
            {
                BackgroundImage.Source = CreateBitmapFromFile(backgroundPath);
            }
            else
            {
                BackgroundImage.Source = null;
            }

            // --- Отображение панели ---
            if (PlaceholderPanel.Visibility == Visibility.Visible)
            {
                PlaceholderPanel.Visibility = Visibility.Hidden;
                DetailsPanel.Visibility = Visibility.Visible;
            }
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(600));
            DetailsPanel.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }


        // 2. ВСТАВЬТЕ ЭТОТ НОВЫЙ ВСПОМОГАТЕЛЬНЫЙ МЕТОД (можно в самый конец файла, перед последней '}')

        private string FormatLastPlayed(DateTime? lastPlayedDate)
        {
            if (lastPlayedDate == null)
            {
                return "Еще не отслеживается";
            }

            DateTime date = lastPlayedDate.Value;
            DateTime today = DateTime.Today;
            DateTime yesterday = today.AddDays(-1);

            if (date.Date == today)
            {
                return $"Сегодня в {date:HH:mm}";
            }
            if (date.Date == yesterday)
            {
                return $"Вчера в {date:HH:mm}";
            }

            // Для дат в пределах текущей недели
            if ((today - date.Date).TotalDays < 7)
            {
                // Выводим день недели (например, "Во вторник в 14:20")
                var culture = new System.Globalization.CultureInfo("ru-RU");
                string dayOfWeek = culture.TextInfo.ToTitleCase(culture.DateTimeFormat.GetDayName(date.DayOfWeek));
                return $"{dayOfWeek} в {date:HH:mm}";
            }

            return date.ToString("dd.MM.yyyy в HH:mm");
        }

        // Вставьте этот новый вспомогательный метод под методом GameListBox_SelectionChanged
        private BitmapImage? CreateBitmapFromFile(string localPath)
        {
            if (string.IsNullOrEmpty(localPath) || !File.Exists(localPath))
            {
                return null;
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(localPath);
                bitmap.CacheOption = BitmapCacheOption.OnLoad; // Важно для освобождения файла
                bitmap.EndInit();
                bitmap.Freeze(); // Важно для производительности в UI
                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка создания BitmapImage из {localPath}: {ex.Message}");
                return null;
            }
        }

        private void ChangeBackgroundImage(BitmapImage? newBackground)
        {
            if (newBackground == null || BackgroundImage_FadeIn == null || BackgroundImage == null)
            {
                BackgroundImage.Source = null; // Просто очищаем фон
                return;
            }

            BackgroundImage_FadeIn.Source = newBackground;
            BackgroundImage_FadeIn.Opacity = 0;

            var fadeInAnimation = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.5), FillBehavior.Stop);

            fadeInAnimation.Completed += (s, e) =>
            {
                BackgroundImage.Source = newBackground;
                BackgroundImage_FadeIn.Opacity = 0;
                BackgroundImage_FadeIn.Source = null;
            };

            BackgroundImage_FadeIn.BeginAnimation(UIElement.OpacityProperty, fadeInAnimation);
        }

        private async void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (GameListBox.SelectedItem is not Game selectedGame) return;



            if (selectedGame.Source == "Steam" && !string.IsNullOrEmpty(selectedGame.AppId))
            {
                Process.Start(new ProcessStartInfo($"steam://run/{selectedGame.AppId}") { UseShellExecute = true });
                this.WindowState = WindowState.Minimized;
                // Не ждем завершения Steam игр, т.к. процесс может быть другим
                return;
            }

            var startInfo = new ProcessStartInfo(selectedGame.ExecutablePath)
            {
                WorkingDirectory = Path.GetDirectoryName(selectedGame.ExecutablePath) ?? string.Empty,
                UseShellExecute = true
            };

            Process? gameProcess = Process.Start(startInfo);

            if (gameProcess == null) return;

            var currentProcess = Process.GetCurrentProcess();
            var originalPriority = currentProcess.PriorityClass;
            try
            {
                this.Hide();
                currentProcess.PriorityClass = ProcessPriorityClass.BelowNormal;
                GC.Collect();
                GC.WaitForPendingFinalizers();
                SetProcessWorkingSetSize(currentProcess.Handle, (IntPtr)(-1), (IntPtr)(-1));

                await gameProcess.WaitForExitAsync();
            }
            finally
            {
                currentProcess.PriorityClass = originalPriority;
                this.Show();
                this.WindowState = WindowState.Normal;
                this.Activate();
            }
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) this.DragMove(); }
        private void Minimize_Click(object sender, RoutedEventArgs e) { this.WindowState = WindowState.Minimized; }
        private void Close_Click(object sender, RoutedEventArgs e) { this.Close(); }
        private void Profile_Click(object sender, RoutedEventArgs e){ ProfileWindow profileWindow = new ProfileWindow(); profileWindow.Show(); }

    }
}