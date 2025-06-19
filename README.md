# WPF Game Launcher

A simple yet functional desktop launcher that scans for games installed on a PC from clients like Steam and Epic Games, aggregating them into a single, convenient library.


## 🚀 Features

-   Automatic game scan on application startup.
-   Manual library refresh button.
-   Support for **Steam** and **Epic Games Store**.
-   Launches Steam games directly via protocol URI.
-   Minimizes to the taskbar while a game is running.
-   Modern UI built with Material Design In XAML. 

## 🛠️ Tech Stack

-   **Platform:** .NET 8
-   **UI Framework:** WPF (Windows Presentation Foundation)
-   **UI Toolkit:** Material Design In XAML
-   **Language:** C#

## ⚙️ How to Run

### Prerequisites

-   [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
-   Visual Studio 2022 (Recommended for GUI method)

### Using Visual Studio (GUI)

1.  Clone the repository.
2.  Open the `Launcher.sln` file in Visual Studio 2022.
3.  Build the solution (Build -> Build Solution).
4.  Run the application (press F5 or the Start button).

### Using the .NET CLI (Command Line)

1.  Clone the repository:
    ```bash
    git clone [https://github.com/YourUsername/WPF-Game-Launcher.git](https://github.com/YourUsername/WPF-Game-Launcher.git)
    ```
2.  Navigate to the project directory:
    ```bash
    cd WPF-Game-Launcher
    ```
3.  Run the application:
    ```bash
    dotnet run --project Launcher/Launcher.csproj
    ```