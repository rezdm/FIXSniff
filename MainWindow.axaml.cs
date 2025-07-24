using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using FIXSniff.ViewModels;
using FIXSniff.Models;
// ReSharper disable InvertIf

namespace FIXSniff;

public partial class MainWindow : Window {
    public MainWindow() {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
    }

    protected override void OnLoaded(RoutedEventArgs e) {
        base.OnLoaded(e);
        
        // Subscribe to DataGrid selection changes
        var tabControl = this.FindControl<TabControl>("TabsControl");
        if (tabControl != null) {
            tabControl.SelectionChanged += OnTabSelectionChanged;
        }
    }

    private void OnTabSelectionChanged(object? sender, SelectionChangedEventArgs e) {
        // Handle tab changes if needed
    }

    // Handle DataGrid selection in code-behind since Avalonia's DataGrid binding can be tricky
    private void DataGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e) {
        switch (sender) {
            case DataGrid { SelectedItem: FixFieldInfo selectedField }: {
                if (DataContext is MainWindowViewModel viewModel) {
                    viewModel.UpdateSelectedFieldDescription(selectedField.Description);
                }

                break;
            }
        }
    }

    // Theme toggle button handler
    private void ToggleTheme_Click(object? sender, RoutedEventArgs e) {
        var current = App.Current.RequestedThemeVariant;
        if (current == ThemeVariant.Dark) {
            SetLightTheme();
        } else {
            SetDarkTheme();
        }
    }

    // Paste button handler
    private async void PasteButton_Click(object? sender, RoutedEventArgs e) {
        try {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null) {
                var text = await clipboard.GetTextAsync();
                if (!string.IsNullOrEmpty(text)) {
                    // Ensure we're on the UI thread and update both ways
                    await Dispatcher.UIThread.InvokeAsync(() => {
                        if (DataContext is MainWindowViewModel viewModel) {
                            viewModel.InputText = text;
                        }
                        
                        // Direct TextBox update as backup
                        var textBox = this.FindControl<TextBox>("InputTextBox");
                        if (textBox != null) {
                            textBox.Text = text;
                        }
                    });
                }
            }
        } catch (Exception ex) {
            System.Console.WriteLine($"Clipboard access failed: {ex.Message}");
        }
    }

    // Methods to switch themes programmatically
    private static void SetDarkTheme() {
        App.Current.RequestedThemeVariant = ThemeVariant.Dark;
    }

    private static void SetLightTheme() {
        App.Current.RequestedThemeVariant = ThemeVariant.Light;
    }

    private static void SetSystemTheme() {
        App.Current.RequestedThemeVariant = ThemeVariant.Default;
    }
}