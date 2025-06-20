// Launcher/Views/MainWindow.xaml.cs

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
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
        private readonly BatchImageDownloader _batchDownloader;
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
            _batchDownloader = new BatchImageDownloader(_imageCacheService);

            _gamesView = CollectionViewSource.GetDefaultView(Games);
            _gamesView.Filter = FilterGames;
            GameListBox.ItemsSource = _gamesView;

            PrevBackgroundButton.Visibility = Visibility.Collapsed;
            NextBackgroundButton.Visibility = Visibility.Collapsed;

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

        private void GameListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

            if (GameListBox.SelectedItem is not Game selectedGame)
            {
                PrevBackgroundButton.Visibility = Visibility.Collapsed;
                NextBackgroundButton.Visibility = Visibility.Collapsed;
                // Убираем фон, если ничего не выбрано
                ChangeBackgroundImage(null);
                return;
            }

            DetailsPanel.BeginAnimation(OpacityProperty, null);
            if (DetailsPanel.Opacity > 0) DetailsPanel.Opacity = 0;

            GameTitleTextBlock.Text = selectedGame.Name ?? "Название не найдено";
            InstallPathTextBlock.Text = selectedGame.InstallPath ?? "Не найден";
            SourceTextBlock.Text = selectedGame.Source ?? "Неизвестно";
            LastPlayedTextBlock.Text = FormatLastPlayed(selectedGame.LastPlayed);

            bool isValidSteamId = !string.IsNullOrEmpty(selectedGame.AppId) && selectedGame.AppId.All(char.IsDigit);
            SteamIdPanel.Visibility = selectedGame.Source == "Steam" && isValidSteamId ? Visibility.Visible : Visibility.Collapsed;
            if (isValidSteamId) AppIdTextBlock.Text = selectedGame.AppId;

            GamePosterImage.Source = CreateBitmapFromFile(selectedGame.LocalPosterPath);

            var backgroundPaths = selectedGame.LocalHeroPaths;
            BitmapImage? backgroundBitmap = null;

            if (backgroundPaths != null && backgroundPaths.Any())
            {

                if (selectedGame.SelectedBackgroundIndex >= backgroundPaths.Count)
                {
                    selectedGame.SelectedBackgroundIndex = 0;
                }
                backgroundBitmap = CreateBitmapFromFile(backgroundPaths[selectedGame.SelectedBackgroundIndex]);
            }
            else
            {
                backgroundBitmap = CreateBitmapFromFile(selectedGame.LocalPosterPath);
            }
            ChangeBackgroundImage(backgroundBitmap);

            bool hasMultipleBackgrounds = backgroundPaths != null && backgroundPaths.Count > 1;
            PrevBackgroundButton.Visibility = hasMultipleBackgrounds ? Visibility.Visible : Visibility.Collapsed;
            NextBackgroundButton.Visibility = hasMultipleBackgrounds ? Visibility.Visible : Visibility.Collapsed;

            if (PlaceholderPanel.Visibility == Visibility.Visible)
            {
                PlaceholderPanel.Visibility = Visibility.Hidden;
                DetailsPanel.Visibility = Visibility.Visible;
            }
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400));
            DetailsPanel.BeginAnimation(OpacityProperty, fadeIn);
        }

        private void PrevBackgroundButton_Click(object sender, RoutedEventArgs e)
        {
            if (GameListBox.SelectedItem is not Game selectedGame) return;
            var backgrounds = selectedGame.LocalHeroPaths;
            if (backgrounds == null || backgrounds.Count <= 1) return;

            // --- ИЗМЕНЕНИЕ ---
            selectedGame.SelectedBackgroundIndex--;
            if (selectedGame.SelectedBackgroundIndex < 0)
            {
                selectedGame.SelectedBackgroundIndex = backgrounds.Count - 1;
            }
            ChangeBackgroundImage(CreateBitmapFromFile(backgrounds[selectedGame.SelectedBackgroundIndex]));
        }

        private void NextBackgroundButton_Click(object sender, RoutedEventArgs e)
        {
            if (GameListBox.SelectedItem is not Game selectedGame) return;
            var backgrounds = selectedGame.LocalHeroPaths;
            if (backgrounds == null || backgrounds.Count <= 1) return;

            // --- ИЗМЕНЕНИЕ ---
            selectedGame.SelectedBackgroundIndex++;
            if (selectedGame.SelectedBackgroundIndex >= backgrounds.Count)
            {
                selectedGame.SelectedBackgroundIndex = 0;
            }
            ChangeBackgroundImage(CreateBitmapFromFile(backgrounds[selectedGame.SelectedBackgroundIndex]));
        }

        // --- ФИНАЛЬНАЯ ВЕРСИЯ МЕТОДА СМЕНЫ ФОНА С АНИМАЦИЕЙ ---
        private void ChangeBackgroundImage(BitmapImage? newBackground)
        {
            // Останавливаем любую текущую анимацию, чтобы избежать конфликтов
            BackgroundImage_FadeIn.BeginAnimation(OpacityProperty, null);
            BackgroundImage_FadeIn.Opacity = 0;

            // Если нового фона нет, просто убираем старый
            if (newBackground == null)
            {
                BackgroundImage.Source = null;
                return;
            }

            // Если пытаемся установить тот же фон, что уже стоит, ничего не делаем
            if (BackgroundImage.Source is BitmapImage oldImage && oldImage.UriSource == newBackground.UriSource)
            {
                return;
            }

            // Помещаем новое изображение в "невидимый" слой для анимации
            BackgroundImage_FadeIn.Source = newBackground;

            // Создаем саму анимацию "проявления"
            var fadeInAnimation = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.4));

            // Определяем, что должно произойти ПОСЛЕ завершения анимации
            fadeInAnimation.Completed += (s, e) =>
            {
                // 1. Устанавливаем основному фону новое изображение
                BackgroundImage.Source = newBackground;
                // 2. Сбрасываем анимированный слой в исходное состояние
                BackgroundImage_FadeIn.BeginAnimation(OpacityProperty, null);
                BackgroundImage_FadeIn.Opacity = 0;
                BackgroundImage_FadeIn.Source = null;
            };

            // Запускаем анимацию
            BackgroundImage_FadeIn.BeginAnimation(OpacityProperty, fadeInAnimation);
        }

        #region Остальные методы (без изменений)

        private async void RefreshGamesButton_Click(object sender, RoutedEventArgs e) { await RefreshGamesAsync(); }

        private async Task RefreshGamesAsync()
        {
            SetUIEnabled(false);
            LoadingStatusText.Text = "Поиск установленных игр...";
            LoadingProgressBar.IsIndeterminate = true;
            LoadingOverlay.Visibility = Visibility.Visible;

            Games.Clear();
            DetailsPanel.Visibility = Visibility.Hidden;
            PlaceholderPanel.Visibility = Visibility.Visible;
            ChangeBackgroundImage(null);

            var foundGames = await Task.Run(() => _scanner.ScanAllAsync());

            if (foundGames.Any())
            {
                LoadingStatusText.Text = "Загрузка обложек...";
                LoadingProgressBar.IsIndeterminate = false;

                var progress = new Progress<DownloadProgressReport>(report =>
                {
                    if (report.Total > 0)
                    {
                        LoadingStatusText.Text = $"Загрузка изображений... ({report.Processed} / {report.Total})";
                        LoadingProgressBar.Maximum = report.Total;
                        LoadingProgressBar.Value = report.Processed;
                    }
                });
                await _batchDownloader.DownloadAllImagesAsync(foundGames, progress);
            }

            foreach (var game in foundGames)
            {
                if (game.Name != null)
                {
                    game.LastPlayed = _playtimeTracker.GetLastPlaytime(game.Name);
                }
                Games.Add(game);
            }
            UpdateGameCount();

            LoadingOverlay.Visibility = Visibility.Collapsed;
            SetUIEnabled(true);
        }

        private void SetUIEnabled(bool isEnabled)
        {
            RefreshGamesButton.IsEnabled = isEnabled;
            ToggleViewButton.IsEnabled = isEnabled;
            SearchTextBox.IsEnabled = isEnabled;
            FilterComboBox.IsEnabled = isEnabled;
            GameListBox.IsEnabled = isEnabled;
            if (isEnabled) SearchTextBox.Text = string.Empty;
        }

        private async void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (GameListBox.SelectedItem is not Game selectedGame) return;

            if (selectedGame.Source == "Steam" && !string.IsNullOrEmpty(selectedGame.AppId))
            {
                Process.Start(new ProcessStartInfo($"steam://run/{selectedGame.AppId}") { UseShellExecute = true });
                this.WindowState = WindowState.Minimized;
                return;
            }

            if (string.IsNullOrEmpty(selectedGame.ExecutablePath))
            {
                MessageBox.Show("Исполняемый файл для этой игры не найден.", "Ошибка запуска");
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

        private BitmapImage? CreateBitmapFromFile(string? localPath)
        {
            if (string.IsNullOrEmpty(localPath) || !File.Exists(localPath)) return null;
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(localPath);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка создания BitmapImage из {localPath}: {ex.Message}");
                return null;
            }
        }

        private bool FilterGames(object item)
        {
            if (item is not Game game) return false;
            string gameName = game.Name ?? "";
            bool platformMatch = true;
            if (FilterComboBox.SelectedItem is ComboBoxItem { Content: string filterPlatform } && filterPlatform != "Все платформы")
            {
                platformMatch = game.Source != null && string.Equals(game.Source, filterPlatform, StringComparison.OrdinalIgnoreCase);
            }
            bool searchMatch = string.IsNullOrEmpty(SearchTextBox.Text) || gameName.Contains(SearchTextBox.Text, StringComparison.OrdinalIgnoreCase);
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

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e) { _gamesView?.Refresh(); UpdateGameCount(); }
        private void FilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { _gamesView?.Refresh(); UpdateGameCount(); }

        private void ToggleViewButton_Click(object? sender, RoutedEventArgs? e)
        {
            isGridView = !isGridView;
            if (ToggleViewButton.Content is not PackIcon viewIcon) return;
            if (isGridView)
            {
                GameListBox.ItemsPanel = (ItemsPanelTemplate)FindResource("GridViewPanelTemplate");
                GameListBox.ItemTemplate = (DataTemplate)FindResource("GridViewItemTemplate");
                viewIcon.Kind = PackIconKind.ViewList;
            }
            else
            {
                GameListBox.ItemsPanel = (ItemsPanelTemplate)FindResource("ListViewPanelTemplate");
                GameListBox.ItemTemplate = (DataTemplate)FindResource("ListViewItemTemplate");
                viewIcon.Kind = PackIconKind.ViewGrid;
            }
        }

        private string FormatLastPlayed(DateTime? lastPlayedDate)
        {
            if (lastPlayedDate == null) return "Еще не отслеживается";
            DateTime date = lastPlayedDate.Value;
            DateTime today = DateTime.Today;
            if (date.Date == today) return $"Сегодня в {date:HH:mm}";
            if (date.Date == today.AddDays(-1)) return $"Вчера в {date:HH:mm}";
            if ((today - date.Date).TotalDays < 7)
            {
                var culture = new System.Globalization.CultureInfo("ru-RU");
                return $"{culture.TextInfo.ToTitleCase(culture.DateTimeFormat.GetDayName(date.DayOfWeek))} в {date:HH:mm}";
            }
            return date.ToString("dd.MM.yyyy в HH:mm");
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) DragMove(); }
        private void Minimize_Click(object sender, RoutedEventArgs e) { WindowState = WindowState.Minimized; }
        private void Close_Click(object sender, RoutedEventArgs e) { Close(); }
        private void Profile_Click(object sender, RoutedEventArgs e) { new ProfileWindow().Show(); }
        #endregion
    }
}