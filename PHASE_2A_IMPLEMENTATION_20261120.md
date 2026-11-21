# Phase 2a: WPF Foundation Implementation Plan
## Pulsar RAT GUI Modernization - Foundation Layer
**Date**: 2026-11-20  
**Duration**: ~4-6 hours  
**Objective**: Create WPF base infrastructure and theme system  

---

## Overview

Phase 2a establishes the foundation for WPF modernization by creating:

1. **Folder Structure** - Organized WPF views, viewmodels, resources, and services
2. **Base Classes** - PulsarWindow and PulsarDialog XAML base classes
3. **Theme System** - Unified light/dark mode with ResourceDictionaries
4. **Services** - ThemeService for runtime theme switching

After Phase 2a, the application will have a complete WPF base layer that can be built and tested independently of the existing WinForms code.

---

## Task 1: Create Folder Structure

### Directory Layout

```powershell
# Create folders
mkdir C:\Users\chris\projects\development\pulsar-rat\Pulsar.Server\Views
mkdir C:\Users\chris\projects\development\pulsar-rat\Pulsar.Server\Views\Dialogs
mkdir C:\Users\chris\projects\development\pulsar-rat\Pulsar.Server\Views\Panels
mkdir C:\Users\chris\projects\development\pulsar-rat\Pulsar.Server\Views\Popups
mkdir C:\Users\chris\projects\development\pulsar-rat\Pulsar.Server\ViewModels
mkdir C:\Users\chris\projects\development\pulsar-rat\Pulsar.Server\ViewModels\Dialogs
mkdir C:\Users\chris\projects\development\pulsar-rat\Pulsar.Server\ViewModels\Panels
mkdir C:\Users\chris\projects\development\pulsar-rat\Pulsar.Server\Resources
mkdir C:\Users\chris\projects\development\pulsar-rat\Pulsar.Server\Resources\Themes
mkdir C:\Users\chris\projects\development\pulsar-rat\Pulsar.Server\Resources\Styles
mkdir C:\Users\chris\projects\development\pulsar-rat\Pulsar.Server\Resources\Converters
mkdir C:\Users\chris\projects\development\pulsar-rat\Pulsar.Server\Services\Wpf
mkdir C:\Users\chris\projects\development\pulsar-rat\Pulsar.Server\Behaviors
```

### Folder Purposes

| Folder | Purpose |
|--------|---------|
| `/Views/` | All XAML windows and user controls |
| `/Views/Dialogs/` | Modal dialog windows |
| `/Views/Panels/` | Tab panels and content areas |
| `/Views/Popups/` | Floating windows and popup windows |
| `/ViewModels/` | ViewModel classes for MVVM |
| `/ViewModels/Dialogs/` | Dialog-specific ViewModels |
| `/ViewModels/Panels/` | Panel-specific ViewModels |
| `/Resources/Themes/` | Light/Dark theme ResourceDictionaries |
| `/Resources/Styles/` | Control-specific styling (Buttons, Windows, etc.) |
| `/Resources/Converters/` | Value converters for XAML binding |
| `/Services/Wpf/` | WPF-specific services (ThemeService, DialogService, etc.) |
| `/Behaviors/` | Attached behaviors for MVVM-friendly event handling |

**Effort**: ~15 minutes  
**Command**:
```powershell
$basePath = "C:\Users\chris\projects\development\pulsar-rat\Pulsar.Server"
@(
    "Views", "Views\Dialogs", "Views\Panels", "Views\Popups",
    "ViewModels", "ViewModels\Dialogs", "ViewModels\Panels",
    "Resources", "Resources\Themes", "Resources\Styles", "Resources\Converters",
    "Services\Wpf",
    "Behaviors"
) | ForEach-Object { 
    $path = Join-Path $basePath $_
    if (-not (Test-Path $path)) { New-Item -ItemType Directory -Path $path -Force | Out-Null }
}
Write-Host "✅ Folder structure created"
```

---

## Task 2: Create Base Classes

### 2a. PulsarWindow.xaml (Base Window)

**Purpose**: Base class for all windows in the application. Applies default styling and theme support.

**File**: `Pulsar.Server/Views/PulsarWindow.xaml`

```xaml
<Window x:Class="Pulsar.Server.Views.PulsarWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Style="{DynamicResource PulsarWindowStyle}"
        Background="{DynamicResource WindowBackgroundBrush}"
        Foreground="{DynamicResource WindowForegroundBrush}"
        FontSize="11"
        WindowStartupLocation="CenterOwner"
        SnapsToDevicePixels="True">
    <!-- Content rendered here by derived classes -->
</Window>
```

**File**: `Pulsar.Server/Views/PulsarWindow.xaml.cs`

```csharp
using System.Windows;

namespace Pulsar.Server.Views
{
    /// <summary>
    /// Base window class for all Pulsar windows.
    /// Provides automatic theme support and default styling.
    /// </summary>
    public partial class PulsarWindow : Window
    {
        public PulsarWindow()
        {
            InitializeComponent();
        }
    }
}
```

**Usage in derived classes**:
```xaml
<!-- MainWindow.xaml -->
<local:PulsarWindow x:Class="Pulsar.Server.Views.MainWindow"
                    xmlns:local="clr-namespace:Pulsar.Server.Views"
                    Title="Pulsar - Operator Console"
                    Width="1200" Height="800">
    <Grid>
        <!-- Content here -->
    </Grid>
</local:PulsarWindow>
```

### 2b. PulsarDialog.xaml (Base Dialog)

**Purpose**: Base class for modal dialogs. Provides consistent styling, button layout, and dialog service integration.

**File**: `Pulsar.Server/Views/PulsarDialog.xaml`

```xaml
<Window x:Class="Pulsar.Server.Views.PulsarDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Style="{DynamicResource PulsarDialogStyle}"
        Background="{DynamicResource WindowBackgroundBrush}"
        Foreground="{DynamicResource WindowForegroundBrush}"
        FontSize="11"
        WindowStartupLocation="CenterOwner"
        ShowInTaskbar="False"
        SnapsToDevicePixels="True">
    <!-- Content rendered here by derived classes -->
</Window>
```

**File**: `Pulsar.Server/Views/PulsarDialog.xaml.cs`

```csharp
using System.Windows;

namespace Pulsar.Server.Views
{
    /// <summary>
    /// Base dialog class for all Pulsar modal dialogs.
    /// Provides automatic theme support, consistent styling, and dialog results.
    /// </summary>
    public partial class PulsarDialog : Window
    {
        public PulsarDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Sets the dialog result to True (OK/Accept).
        /// </summary>
        protected void AcceptDialog()
        {
            DialogResult = true;
            Close();
        }

        /// <summary>
        /// Sets the dialog result to False (Cancel/Reject).
        /// </summary>
        protected void CancelDialog()
        {
            DialogResult = false;
            Close();
        }
    }
}
```

**Usage in derived classes**:
```xaml
<!-- SettingsDialog.xaml -->
<local:PulsarDialog x:Class="Pulsar.Server.Views.Dialogs.SettingsDialog"
                    xmlns:local="clr-namespace:Pulsar.Server.Views"
                    Title="Settings"
                    Width="600" Height="400">
    <Grid>
        <StackPanel VerticalAlignment="Bottom" HorizontalAlignment="Right" Orientation="Horizontal">
            <Button Content="OK" Click="OK_Click" />
            <Button Content="Cancel" Click="Cancel_Click" />
        </StackPanel>
    </Grid>
</local:PulsarDialog>
```

**Effort**: ~20 minutes  
**Files Created**: 4 (2 XAML + 2 CS)  
**Complexity**: Low

---

## Task 3: Create Theme System

### 3a. ThemeService.cs (Theme Management)

**File**: `Pulsar.Server/Services/Wpf/ThemeService.cs`

```csharp
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
                Application.Current.Resources.MergedDictionaries.Clear();
                Application.Current.Resources.MergedDictionaries.Add(resourceDictionary);

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
```

**Usage**:
```csharp
// In MainWindowViewModel or any ViewModel
var themeService = ThemeService.Instance;
themeService.CurrentTheme = ThemeService.Theme.Light;  // Switch to light theme

// Listen for theme changes
themeService.ThemeChanged += (s, e) =>
{
    Debug.WriteLine($"Theme changed to: {e.NewTheme}");
};
```

**Effort**: ~25 minutes  
**Files Created**: 1 CS file  
**Complexity**: Low

### 3b. DarkTheme.xaml (Dark Color Scheme)

**File**: `Pulsar.Server/Resources/Themes/DarkTheme.xaml`

```xaml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- Background and Foreground Colors -->
    <Color x:Key="PrimaryBackgroundColor">#1E1E1E</Color>
    <Color x:Key="PrimaryForegroundColor">#FFFFFF</Color>
    <Color x:Key="SecondaryBackgroundColor">#2D2D2D</Color>
    <Color x:Key="SecondaryForegroundColor">#E0E0E0</Color>
    <Color x:Key="AccentColor">#007ACC</Color>
    <Color x:Key="AccentDarkColor">#005A9E</Color>

    <!-- Brushes (derived from colors) -->
    <SolidColorBrush x:Key="WindowBackgroundBrush" Color="{StaticResource PrimaryBackgroundColor}" />
    <SolidColorBrush x:Key="WindowForegroundBrush" Color="{StaticResource PrimaryForegroundColor}" />
    <SolidColorBrush x:Key="PanelBackgroundBrush" Color="{StaticResource SecondaryBackgroundColor}" />
    <SolidColorBrush x:Key="PanelForegroundBrush" Color="{StaticResource SecondaryForegroundColor}" />
    <SolidColorBrush x:Key="AccentBrush" Color="{StaticResource AccentColor}" />
    <SolidColorBrush x:Key="AccentDarkBrush" Color="{StaticResource AccentDarkColor}" />

    <!-- Border and Separator Colors -->
    <SolidColorBrush x:Key="BorderBrush" Color="#3E3E42" />
    <SolidColorBrush x:Key="SeparatorBrush" Color="#424245" />

    <!-- Control-Specific Colors -->
    <SolidColorBrush x:Key="ButtonBackgroundBrush" Color="#0E639C" />
    <SolidColorBrush x:Key="ButtonHoverBackgroundBrush" Color="#1177BB" />
    <SolidColorBrush x:Key="ButtonPressedBackgroundBrush" Color="#007ACC" />
    <SolidColorBrush x:Key="ButtonDisabledBackgroundBrush" Color="#2D2D30" />
    <SolidColorBrush x:Key="ButtonDisabledForegroundBrush" Color="#808080" />

    <!-- TextBox and Input Colors -->
    <SolidColorBrush x:Key="TextBoxBackgroundBrush" Color="#3C3C3C" />
    <SolidColorBrush x:Key="TextBoxBorderBrush" Color="#464646" />
    <SolidColorBrush x:Key="TextBoxForegroundBrush" Color="#CCCCCC" />

    <!-- Selection and Highlight Colors -->
    <SolidColorBrush x:Key="SelectionBackgroundBrush" Color="#094771" />
    <SolidColorBrush x:Key="SelectionForegroundBrush" Color="#FFFFFF" />
    <SolidColorBrush x:Key="InactiveSelectionBackgroundBrush" Color="#2D2D30" />

    <!-- Message and Status Colors -->
    <SolidColorBrush x:Key="SuccessBrush" Color="#00AA00" />
    <SolidColorBrush x:Key="WarningBrush" Color="#FFAA00" />
    <SolidColorBrush x:Key="ErrorBrush" Color="#FF5555" />
    <SolidColorBrush x:Key="InfoBrush" Color="#00CCFF" />

</ResourceDictionary>
```

**Effort**: ~15 minutes  
**Files Created**: 1 XAML file  
**Complexity**: Low

### 3c. LightTheme.xaml (Light Color Scheme)

**File**: `Pulsar.Server/Resources/Themes/LightTheme.xaml`

```xaml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- Background and Foreground Colors -->
    <Color x:Key="PrimaryBackgroundColor">#FFFFFF</Color>
    <Color x:Key="PrimaryForegroundColor">#000000</Color>
    <Color x:Key="SecondaryBackgroundColor">#F3F3F3</Color>
    <Color x:Key="SecondaryForegroundColor">#333333</Color>
    <Color x:Key="AccentColor">#0066CC</Color>
    <Color x:Key="AccentDarkColor">#0052A3</Color>

    <!-- Brushes (derived from colors) -->
    <SolidColorBrush x:Key="WindowBackgroundBrush" Color="{StaticResource PrimaryBackgroundColor}" />
    <SolidColorBrush x:Key="WindowForegroundBrush" Color="{StaticResource PrimaryForegroundColor}" />
    <SolidColorBrush x:Key="PanelBackgroundBrush" Color="{StaticResource SecondaryBackgroundColor}" />
    <SolidColorBrush x:Key="PanelForegroundBrush" Color="{StaticResource SecondaryForegroundColor}" />
    <SolidColorBrush x:Key="AccentBrush" Color="{StaticResource AccentColor}" />
    <SolidColorBrush x:Key="AccentDarkBrush" Color="{StaticResource AccentDarkColor}" />

    <!-- Border and Separator Colors -->
    <SolidColorBrush x:Key="BorderBrush" Color="#D0D0D0" />
    <SolidColorBrush x:Key="SeparatorBrush" Color="#E0E0E0" />

    <!-- Control-Specific Colors -->
    <SolidColorBrush x:Key="ButtonBackgroundBrush" Color="#0066CC" />
    <SolidColorBrush x:Key="ButtonHoverBackgroundBrush" Color="#0052A3" />
    <SolidColorBrush x:Key="ButtonPressedBackgroundBrush" Color="#003D7A" />
    <SolidColorBrush x:Key="ButtonDisabledBackgroundBrush" Color="#E0E0E0" />
    <SolidColorBrush x:Key="ButtonDisabledForegroundBrush" Color="#808080" />

    <!-- TextBox and Input Colors -->
    <SolidColorBrush x:Key="TextBoxBackgroundBrush" Color="#FFFFFF" />
    <SolidColorBrush x:Key="TextBoxBorderBrush" Color="#CCCCCC" />
    <SolidColorBrush x:Key="TextBoxForegroundBrush" Color="#000000" />

    <!-- Selection and Highlight Colors -->
    <SolidColorBrush x:Key="SelectionBackgroundBrush" Color="#0066CC" />
    <SolidColorBrush x:Key="SelectionForegroundBrush" Color="#FFFFFF" />
    <SolidColorBrush x:Key="InactiveSelectionBackgroundBrush" Color="#E0E0E0" />

    <!-- Message and Status Colors -->
    <SolidColorBrush x:Key="SuccessBrush" Color="#008000" />
    <SolidColorBrush x:Key="WarningBrush" Color="#FF8800" />
    <SolidColorBrush x:Key="ErrorBrush" Color="#CC0000" />
    <SolidColorBrush x:Key="InfoBrush" Color="#0088CC" />

</ResourceDictionary>
```

**Effort**: ~15 minutes  
**Files Created**: 1 XAML file  
**Complexity**: Low

---

## Task 4: Create Default Styles

### 4a. Generic.xaml (Merged Resource Dictionary)

**File**: `Pulsar.Server/Resources/Styles/Generic.xaml`

This file will be referenced by App.xaml to merge all styles and resource dictionaries.

```xaml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- Merge all style libraries -->
    <ResourceDictionary.MergedDictionaries>
        <ResourceDictionary Source="pack://application:,,,/Resources/Styles/Windows.xaml" />
        <ResourceDictionary Source="pack://application:,,,/Resources/Styles/Buttons.xaml" />
        <ResourceDictionary Source="pack://application:,,,/Resources/Styles/TextBoxes.xaml" />
        <ResourceDictionary Source="pack://application:,,,/Resources/Styles/DataGrids.xaml" />
        <ResourceDictionary Source="pack://application:,,,/Resources/Themes/DarkTheme.xaml" />
    </ResourceDictionary.MergedDictionaries>

</ResourceDictionary>
```

### 4b. Windows.xaml (Window Styles)

**File**: `Pulsar.Server/Resources/Styles/Windows.xaml`

```xaml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- PulsarWindow Style -->
    <Style x:Key="PulsarWindowStyle" TargetType="{x:Type Window}">
        <Setter Property="Background" Value="{DynamicResource WindowBackgroundBrush}" />
        <Setter Property="Foreground" Value="{DynamicResource WindowForegroundBrush}" />
        <Setter Property="FontFamily" Value="Segoe UI" />
        <Setter Property="FontSize" Value="11" />
        <Setter Property="UseLayoutRounding" Value="True" />
        <Setter Property="TextOptions.TextRenderingMode" Value="Auto" />
    </Style>

    <!-- PulsarDialog Style (inherits from PulsarWindowStyle) -->
    <Style x:Key="PulsarDialogStyle" TargetType="{x:Type Window}" BasedOn="{StaticResource PulsarWindowStyle}">
        <Setter Property="ResizeMode" Value="CanResizeWithGrip" />
        <Setter Property="ShowInTaskbar" Value="False" />
        <Setter Property="WindowStyle" Value="SingleBorderWindow" />
    </Style>

</ResourceDictionary>
```

### 4c. Buttons.xaml (Button Styles)

**File**: `Pulsar.Server/Resources/Styles/Buttons.xaml`

```xaml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- Standard Button Style -->
    <Style TargetType="{x:Type Button}">
        <Setter Property="Background" Value="{DynamicResource ButtonBackgroundBrush}" />
        <Setter Property="Foreground" Value="{DynamicResource PrimaryForegroundColor}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}" />
        <Setter Property="Padding" Value="10,6" />
        <Setter Property="FontSize" Value="11" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="{x:Type Button}">
                    <Border Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}"
                            CornerRadius="2">
                        <ContentPresenter HorizontalAlignment="Center"
                                        VerticalAlignment="Center"
                                        TextBlock.Foreground="{TemplateBinding Foreground}"
                                        Margin="{TemplateBinding Padding}" />
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
        <Style.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
                <Setter Property="Background" Value="{DynamicResource ButtonHoverBackgroundBrush}" />
            </Trigger>
            <Trigger Property="IsPressed" Value="True">
                <Setter Property="Background" Value="{DynamicResource ButtonPressedBackgroundBrush}" />
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
                <Setter Property="Background" Value="{DynamicResource ButtonDisabledBackgroundBrush}" />
                <Setter Property="Foreground" Value="{DynamicResource ButtonDisabledForegroundBrush}" />
            </Trigger>
        </Style.Triggers>
    </Style>

</ResourceDictionary>
```

### 4d. TextBoxes.xaml, DataGrids.xaml (Stub Files)

Create placeholder files for future styling:

```xaml
<!-- TextBoxes.xaml -->
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- TextBox and related control styles -->
</ResourceDictionary>
```

```xaml
<!-- DataGrids.xaml -->
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- DataGrid and related control styles -->
</ResourceDictionary>
```

**Effort**: ~25 minutes  
**Files Created**: 4 XAML files  
**Complexity**: Low

---

## Task 5: Update App.xaml

Merge the new resource dictionaries into the application's App.xaml:

**File**: `Pulsar.Server/App.xaml`

**Current**:
```xaml
<Application x:Class="Pulsar.Server.PulsarApplication"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
         <!-- Existing resources -->
    </Application.Resources>
</Application>
```

**Updated**:
```xaml
<Application x:Class="Pulsar.Server.PulsarApplication"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <!-- Load merged styles and themes -->
                <ResourceDictionary Source="pack://application:,,,/Resources/Styles/Generic.xaml" />
            </ResourceDictionary.MergedDictionaries>
            
            <!-- Existing resources -->
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

**Effort**: ~5 minutes  
**Files Modified**: 1 (App.xaml)  
**Complexity**: Low

---

## Task 6: Create Startup Code

Modify `PulsarApplication` to initialize the theme service:

**File**: `Pulsar.Server/PulsarApplication.cs`

**Add at beginning of OnStartup**:
```csharp
// Initialize theme service (before any windows created)
var themeService = Services.Wpf.ThemeService.Instance;
// Theme defaults to Dark from constructor
```

**Example**:
```csharp
protected override void OnStartup(StartupEventArgs e)
{
    // Initialize theme service
    var themeService = Services.Wpf.ThemeService.Instance;
    
    // Load user theme preference from settings
    var savedTheme = Properties.Settings.Default.ApplicationTheme ?? "Dark";
    themeService.CurrentTheme = savedTheme == "Dark" 
        ? Services.Wpf.ThemeService.Theme.Dark 
        : Services.Wpf.ThemeService.Theme.Light;

    base.OnStartup(e);
    
    // ... rest of startup
}
```

**Effort**: ~10 minutes  
**Files Modified**: 1 (PulsarApplication.cs)  
**Complexity**: Low

---

## Task 7: Build and Test Foundation

### 7a. Build Solution

```powershell
cd C:\Users\chris\projects\development\pulsar-rat
dotnet clean Pulsar.sln
dotnet restore Pulsar.sln
dotnet build Pulsar.Server/Pulsar.Server.csproj -c Release 2>&1 | Tee-Object -FilePath "phase2a-build.log"
```

**Expected Output**:
```
✅ Pulsar.Server -> bin\Release\net9.0-windows\Pulsar.dll
Build succeeded with 0 errors and X warnings.
```

### 7b. Run Unit Tests

```powershell
# Common tests (unchanged, should still pass)
dotnet test Pulsar.Common.Tests/Pulsar.Common.Tests.csproj -c Release -f net48 --verbosity normal
dotnet test Pulsar.Common.Tests/Pulsar.Common.Tests.csproj -c Release -f net9.0-windows --verbosity normal
```

**Expected**:
```
✅ 273/273 passed
Test Run Successful.
```

### 7c. Launch Application (Minimal Test)

```powershell
# Launch without connecting to any clients (startup only)
& "C:\Users\chris\projects\development\pulsar-rat\bin\Release\net9.0-windows\Pulsar.exe"
```

**Visual Checklist**:
- [ ] Window appears and doesn't crash
- [ ] Dark theme is applied (default)
- [ ] All text is readable
- [ ] No XAML parse errors in Output

**Effort**: ~20 minutes (mostly build time)  
**Risk**: Low (new code doesn't touch existing)

---

## Verification Checklist

After completing Phase 2a, verify:

```
FOLDER STRUCTURE
[ ] Views/ exists
[ ] Views/Dialogs/ exists
[ ] Views/Panels/ exists
[ ] Views/Popups/ exists
[ ] ViewModels/ exists
[ ] ViewModels/Dialogs/ exists
[ ] ViewModels/Panels/ exists
[ ] Resources/Themes/ exists
[ ] Resources/Styles/ exists
[ ] Resources/Converters/ exists
[ ] Services/Wpf/ exists
[ ] Behaviors/ exists

BASE CLASSES
[ ] PulsarWindow.xaml + .xaml.cs exist
[ ] PulsarDialog.xaml + .xaml.cs exist
[ ] Both compile without errors

THEME SYSTEM
[ ] ThemeService.cs exists
[ ] ThemeService.Instance works
[ ] ThemeService.CurrentTheme settable
[ ] DarkTheme.xaml exists with all colors
[ ] LightTheme.xaml exists with all colors
[ ] ThemeChanged event fires

STYLES
[ ] Generic.xaml exists and merges 4 style files
[ ] Windows.xaml contains PulsarWindowStyle and PulsarDialogStyle
[ ] Buttons.xaml has button styling
[ ] TextBoxes.xaml exists (stub)
[ ] DataGrids.xaml exists (stub)

APP.XAML
[ ] App.xaml references Generic.xaml in MergedDictionaries
[ ] No parse errors after App.xaml change

BUILD
[ ] dotnet build succeeds
[ ] 0 compilation errors
[ ] All projects build successfully
[ ] Pulsar.exe created in Release folder

UNIT TESTS
[ ] Common.Tests (net48): 122/122 pass
[ ] Common.Tests (net9.0-windows): 151/151 pass
[ ] Total: 273/273 pass

RUNTIME
[ ] Application launches without crash
[ ] Dark theme applied visually
[ ] Text is readable with current theme
[ ] No XAML parse errors
[ ] Window is resizable and responsive
```

**Success Criteria**:
✅ All checkboxes above: ✓  
✅ Build output shows "Build succeeded"  
✅ All 273 unit tests pass  
✅ Application launches with dark theme  

---

## Troubleshooting

### Issue: XAML Parse Error "Cannot find type 'PulsarWindow'"

**Cause**: Namespace not registered in XAML.

**Fix**:
```xaml
<Window xmlns:local="clr-namespace:Pulsar.Server.Views"
        x:Class="Pulsar.Server.Views.MyWindow">
    <!-- Must use local: prefix for PulsarWindow -->
</Window>
```

### Issue: Theme Resources Not Found at Runtime

**Cause**: ResourceDictionary URI incorrect.

**Fix**: Check URI format:
```xaml
<!-- Correct -->
<ResourceDictionary Source="pack://application:,,,/Resources/Themes/DarkTheme.xaml" />

<!-- Wrong (missing "pack://application:,,,/") -->
<ResourceDictionary Source="/Resources/Themes/DarkTheme.xaml" />
```

### Issue: Build Error "ResourceDictionary in .xaml"

**Cause**: Missing xmlns declarations.

**Fix**: All theme XAML files need:
```xaml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
```

### Issue: Window Doesn't Apply PulsarWindow Style

**Cause**: PulsarWindow not properly deriving in XAML.

**Fix**: Derived XAML must use full namespace:
```xaml
<local:PulsarWindow x:Class="Pulsar.Server.Views.MainWindow"
                    xmlns:local="clr-namespace:Pulsar.Server.Views"
                    ...>
```

---

## Success Output Example

After Phase 2a is complete:

```
✅ Phase 2a: WPF Foundation
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📁 Folder Structure Created
   ✓ Views/ (4 subdirectories)
   ✓ ViewModels/ (3 subdirectories)
   ✓ Resources/ (3 subdirectories)
   ✓ Services/Wpf/
   ✓ Behaviors/

📐 Base Classes Created
   ✓ PulsarWindow.xaml + CS
   ✓ PulsarDialog.xaml + CS

🎨 Theme System Created
   ✓ ThemeService.cs
   ✓ DarkTheme.xaml (20 colors)
   ✓ LightTheme.xaml (20 colors)

🎯 Styles Created
   ✓ Generic.xaml (merged dictionary)
   ✓ Windows.xaml (2 styles)
   ✓ Buttons.xaml (button styling)
   ✓ TextBoxes.xaml (stub)
   ✓ DataGrids.xaml (stub)

🔧 Updated App.xaml
   ✓ Resources/Styles/Generic.xaml merged

✅ Build Status
   Project: Pulsar.Server
   Configuration: Release
   Target: net9.0-windows
   Result: SUCCESS (0 errors, 15 warnings)

✅ Test Status
   Framework: net48           122/122 ✓
   Framework: net9.0-windows  151/151 ✓
   Total:                     273/273 ✓

✅ Runtime Status
   Application Launch: SUCCESS
   Theme Application: Dark (applied)
   Window Rendering: OK
   No XAML Errors: Confirmed

📈 Metrics
   Files Created:    15 (3 CS, 10 XAML, 2 config)
   Build Time:       1.77 seconds
   Test Duration:    1.5 seconds
   Total Phase Time: ~4.5 hours

🎯 Next Step: Phase 2b - Critical Dialog Migration
   Target: 4 dialogs (Settings, Builder, Connections, HVNC)
   Start Date: (Ready for execution)
```

---

## Files Summary

| File | Type | Size | Purpose |
|------|------|------|---------|
| PulsarWindow.xaml | XAML | 400B | Base window class |
| PulsarWindow.xaml.cs | CS | 200B | Window codebehind |
| PulsarDialog.xaml | XAML | 500B | Base dialog class |
| PulsarDialog.xaml.cs | CS | 600B | Dialog codebehind + AcceptDialog/CancelDialog |
| ThemeService.cs | CS | 2.5KB | Theme management service |
| DarkTheme.xaml | XAML | 2KB | Dark theme colors |
| LightTheme.xaml | XAML | 2KB | Light theme colors |
| Generic.xaml | XAML | 500B | Merged resource dictionary |
| Windows.xaml | XAML | 400B | Window styles |
| Buttons.xaml | XAML | 1.5KB | Button styles |
| TextBoxes.xaml | XAML | 200B | Stub |
| DataGrids.xaml | XAML | 200B | Stub |
| App.xaml | XAML | (modified) | Added resource dictionary reference |
| PulsarApplication.cs | CS | (modified) | Added theme service initialization |

**Total New Code**: ~12.5 KB  
**Total Files Created**: 13  
**Total Files Modified**: 2  

---

## Next Steps After Phase 2a

Once Phase 2a is complete and verified:

1. **Commit to Git**
   ```powershell
   git add -A
   git commit -m "Phase 2a: WPF foundation layer - base classes, theme system, default styles"
   ```

2. **Create Phase 2b Branch**
   ```powershell
   git checkout -b phase-2b-critical-dialogs
   ```

3. **Proceed to Phase 2b: Critical Dialog Migration**
   - FrmSettings → SettingsDialog.xaml
   - FrmBuilder → ClientBuilderDialog.xaml
   - FrmConnections → ConnectionsDialog.xaml
   - FrmHVNC → HVNCWindow.xaml

---

**End of Phase 2a Implementation Plan**
