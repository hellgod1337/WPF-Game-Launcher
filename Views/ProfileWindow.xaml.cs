using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace Launcher.Views
{
    public partial class ProfileWindow : Window
    {
        public ProfileWindow() => InitializeComponent();

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        // Сворачивает окно
        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        // Кнопка максимизации (оставлена пустой, т.к. ResizeMode="NoResize")
        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            // Так как размер окна не меняется, эта кнопка не выполняет действий.
            // Можно либо убрать ее из XAML, либо оставить так.
        }

        // Перетаскивание окна
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        // Новая функция: открывает ссылку на GitHub в браузере
        private void GitHub_Click(object sender, RoutedEventArgs e)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "https://github.com/hellgod1337",
                UseShellExecute = true
            };
            Process.Start(psi);
        }
    }
}