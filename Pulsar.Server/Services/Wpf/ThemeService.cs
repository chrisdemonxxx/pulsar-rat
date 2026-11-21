using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace Pulsar.Server.Services.Wpf
{
    /// <summary>
    /// Service for managing application theme (Light/Dark mode).
    /// Uses ResourceDictionaries for theme switching and broadcasts changes via events.
    /// </summary>
    public class ThemeService : INotifyPropertyChanged
    {
        public enum Theme { Light, Dark }

        private static ThemeService? _instance;
        public static ThemeService Instance => _instance ??= new ThemeService();

        private Theme _currentTheme = Theme.Dark;

        public Theme CurrentTheme
        {
            get => _currentTheme;
            set
            {
                if (_currentTheme != value)
                {
                    _currentTheme = value;
                    ApplyTheme(value);
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Fired when theme changes at runtime.
        /// </summary>
        public event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ThemeService()
        {
            // Load theme from settings or default to Dark
            CurrentTheme = Theme.Dark;
        }

        /// <summary>
        /// Applies the specified theme to the application.
        /// </summary>
        public void ApplyTheme(Theme theme)
        {
            try
            {
                var themeFile = theme == Theme.Dark
                    ? "pack://application:,,,/Resources/Themes/DarkTheme.xaml"
                    : "pack://application:,,,/Resources/Themes/LightTheme.xaml";

                var resourceDictionary = new ResourceDictionary { Source = new Uri(themeFile) };

                // Replace existing merged dictionary
                if (Application.Current.Resources.MergedDictionaries.Count > 0)
                {
                    Application.Current.Resources.MergedDictionaries.RemoveAt(0);
                }
                Application.Current.Resources.MergedDictionaries.Insert(0, resourceDictionary);

                // Fire theme changed event
                ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(theme));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to apply theme: {ex.Message}");
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Event arguments for theme change events.
    /// </summary>
    public class ThemeChangedEventArgs : EventArgs
    {
        public ThemeService.Theme NewTheme { get; }

        public ThemeChangedEventArgs(ThemeService.Theme newTheme)
        {
            NewTheme = newTheme;
        }
    }
}
