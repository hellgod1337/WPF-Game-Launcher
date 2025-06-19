using Microsoft.Data.SqlClient;
using System.Windows;
using System.Windows.Input;

namespace Launcher.Views
{
    public partial class LoginWindow : Window
    {
        private readonly string _connectionString = 
            "Server=DESKTOP-4SR2T2G\\MSSQLSERVER01;" +
            "Database=Launcher;" +
            "Integrated Security=True;" +
            "TrustServerCertificate=True;";

        private bool _isRegisterMode = false;

        public LoginWindow()
        {
            InitializeComponent();
        }

        // Метод, который вызывается при загрузке окна
        private async void LoginWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Блокируем форму, пока идет проверка
            mainPanel.IsEnabled = false;
            statusTextBlock.Text = "Подключение к базе данных...";

            bool isConnected = await CheckDatabaseConnectionAsync();

            // Разблокируем форму и убираем сообщение
            mainPanel.IsEnabled = true;
            statusTextBlock.Text = "";

            if (!isConnected)
            {
                MessageBox.Show("Не удалось подключиться к базе данных. Пожалуйста, проверьте строку подключения в коде и убедитесь, что SQL Server запущен.", "Ошибка подключения", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Асинхронная проверка соединения с БД
        private async Task<bool> CheckDatabaseConnectionAsync()
        {
            try
            {
                await using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    return true; // Соединение успешно
                }
            }
            catch (Exception)
            {
                Close();
                return false; // Произошла ошибка
                
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) { this.WindowState = WindowState.Minimized; }
        private void Close_Click(object sender, RoutedEventArgs e) { this.Close(); }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) this.DragMove(); }

        // Переключение между входом и регистрацией
        private void ToggleView_Click(object sender, RoutedEventArgs e)
        {
            _isRegisterMode = !_isRegisterMode;

            if (_isRegisterMode)
            {
                TitleTextBlock.Text = "РЕГИСТРАЦИЯ";
                LoginButton.Visibility = Visibility.Collapsed;
                RegisterButton.Visibility = Visibility.Visible;
                PromptRun.Text = "Уже есть аккаунт? ";
                ToggleRun.Text = "Войти";
            }
            else
            {
                TitleTextBlock.Text = "ВХОД";
                LoginButton.Visibility = Visibility.Visible;
                RegisterButton.Visibility = Visibility.Collapsed;
                PromptRun.Text = "Нет аккаунта? ";
                ToggleRun.Text = "Зарегистрироваться";
            }
        }

        // Логика кнопки "Войти"
        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            string username = UsernameTextBox.Text;
            string password = PasswordBox.Password;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("Пожалуйста, введите имя пользователя и пароль.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string storedHash = "";
            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                try
                {
                    connection.Open();
                    string query = "SELECT PasswordHash FROM Users WHERE Username = @Username";
                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@Username", username);
                        var result = command.ExecuteScalar();
                        if (result != null)
                        {
                            storedHash = result.ToString();
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка работы с базой данных: {ex.Message}", "Критическая ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            if (!string.IsNullOrEmpty(storedHash) && BCrypt.Net.BCrypt.Verify(password, storedHash))
            {
                // При успешном входе открываем главное окно
                MainWindow mainWindow = new MainWindow();
                Application.Current.MainWindow = mainWindow;
                mainWindow.Show();
                this.Close();
            }
            else
            {
                MessageBox.Show("Неверное имя пользователя или пароль.", "Ошибка входа", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Логика кнопки "Регистрация"
        private void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            string username = UsernameTextBox.Text;
            string password = PasswordBox.Password;

            if (string.IsNullOrWhiteSpace(username) || password.Length < 6)
            {
                MessageBox.Show("Имя пользователя не может быть пустым, а пароль должен содержать минимум 6 символов.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string passwordHash = BCrypt.Net.BCrypt.HashPassword(password);

            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                try
                {
                    connection.Open();
                    // Проверка, существует ли пользователь
                    string checkUserQuery = "SELECT COUNT(1) FROM Users WHERE Username = @Username";
                    using (SqlCommand checkUserCmd = new SqlCommand(checkUserQuery, connection))
                    {
                        checkUserCmd.Parameters.AddWithValue("@Username", username);
                        int userExists = (int)checkUserCmd.ExecuteScalar();
                        if (userExists > 0)
                        {
                            MessageBox.Show("Пользователь с таким именем уже существует.", "Ошибка регистрации", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                    }

                    // Добавление нового пользователя
                    string insertQuery = "INSERT INTO Users (Username, PasswordHash, RoleId) VALUES (@Username, @PasswordHash, 2)";
                    using (SqlCommand command = new SqlCommand(insertQuery, connection))
                    {
                        command.Parameters.AddWithValue("@Username", username);
                        command.Parameters.AddWithValue("@PasswordHash", passwordHash);

                        int result = command.ExecuteNonQuery();

                        if (result > 0)
                        {
                            MessageBox.Show("Регистрация прошла успешно! Теперь вы можете войти.", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                            ToggleView_Click(sender, e);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка работы с базой данных: {ex.Message}", "Критическая ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
