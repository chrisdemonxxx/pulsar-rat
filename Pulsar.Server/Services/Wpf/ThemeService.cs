using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace Pulsar.Server.Services.Wpf
{
    /// <summary>
    /// Service for managing application theme (Light/Dark mode).
    /// Uses ResourceDictionaries for theme switching and broadcasts changes via events.
    /// Supports both standalone WPF Applications and WinForms-hosted WPF windows.
    /// </summary>
    public class ThemeService : INotifyPropertyChanged
    {
        public enum Theme { Light, Dark }

        private const string DarkThemeUri = "pack://application:,,,/Pulsar;component/Resources/Themes/DarkTheme.xaml";
        private const string LightThemeUri = "pack://application:,,,/Pulsar;component/Resources/Themes/LightTheme.xaml";

        private static ThemeService _instance;
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
        public event EventHandler<ThemeChangedEventArgs> ThemeChanged;

        public event PropertyChangedEventHandler PropertyChanged;

        public ThemeService()
        {
            _currentTheme = Theme.Dark;
        }

        /// <summary>
        /// Returns the pack URI for the requested theme dictionary.
        /// </summary>
        public static Uri GetThemeUri(Theme theme)
        {
            return new Uri(theme == Theme.Dark ? DarkThemeUri : LightThemeUri, UriKind.Absolute);
        }

        /// <summary>
        /// Applies the specified theme to Application.Current (if present) and notifies listeners.
        /// </summary>
        public void ApplyTheme(Theme theme)
        {
            try
            {
                if (Application.Current?.Resources != null)
                {
                    ApplyThemeTo(Application.Current.Resources, theme);
                }

                ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(theme));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to apply theme: {ex.Message}");
            }
        }

        /// <summary>
        /// Applies the specified theme into a specific ResourceDictionary (e.g., a Window's Resources),
        /// replacing any previously merged DarkTheme/LightTheme dictionary while preserving style dictionaries.
        /// </summary>
        public static void ApplyThemeTo(ResourceDictionary target, Theme theme)
        {
            if (target == null)
            {
                return;
            }

            var newDictionary = new ResourceDictionary { Source = GetThemeUri(theme) };
            ReplaceThemeDictionary(target.MergedDictionaries, newDictionary);
        }

        private static bool ReplaceThemeDictionary(IList<ResourceDictionary> merged, ResourceDictionary replacement)
        {
            for (int i = 0; i < merged.Count; i++)
            {
                var current = merged[i];
                if (IsThemeDictionary(current))
                {
                    merged[i] = replacement;
                    return true;
                }

                if (current.MergedDictionaries.Count > 0 &&
                    ReplaceThemeDictionary(current.MergedDictionaries, replacement))
                {
                    return true;
                }
            }

            merged.Insert(0, replacement);
            return true;
        }

        private static bool IsThemeDictionary(ResourceDictionary dictionary)
        {
            var source = dictionary?.Source?.OriginalString;
            if (string.IsNullOrEmpty(source))
            {
                return false;
            }

            return source.EndsWith("DarkTheme.xaml", StringComparison.OrdinalIgnoreCase)
                || source.EndsWith("LightTheme.xaml", StringComparison.OrdinalIgnoreCase);
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
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
