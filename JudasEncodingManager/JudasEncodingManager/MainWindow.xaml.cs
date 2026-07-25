using System;
using System.ComponentModel;
using System.Windows;
using JudasEncodingManager.Converters;
using JudasEncodingManager.ViewModels;
using Microsoft.Web.WebView2.Core;

namespace JudasEncodingManager
{
    public partial class MainWindow : Window
    {
        private MainViewModel? _viewModel;
        private bool _localWebViewInitialized = false;
        private bool _seedboxWebViewInitialized = false;

        public MainWindow()
        {
            // Register converters as resources before InitializeComponent
            Resources.Add("BoolToColorConverter", new BoolToColorConverter());
            Resources.Add("BoolToStatusConverter", new BoolToStatusConverter());
            Resources.Add("NullToBoolConverter", new NullToBoolConverter());
            Resources.Add("BoolToVisibilityConverter", new BoolToVisibilityConverter());
            Resources.Add("UnsavedChangesColorConverter", new UnsavedChangesColorConverter());
            Resources.Add("InverseBoolConverter", new InverseBoolConverter());
            Resources.Add("StringToVisibilityConverter", new StringToVisibilityConverter());
            Resources.Add("TestModeColorConverter", new TestModeColorConverter());
            Resources.Add("ConnectionStatusColorConverter", new ConnectionStatusColorConverter());
            Resources.Add("InverseBoolToVisibilityConverter", new InverseBoolToVisibilityConverter());
            Resources.Add("MonitoringStatusColorConverter", new MonitoringStatusColorConverter());
            Resources.Add("QueueStatusColorConverter", new QueueStatusColorConverter());
            Resources.Add("LogLevelColorConverter", new LogLevelColorConverter());
            Resources.Add("EncodingStatusToVisibilityConverter", new EncodingStatusToVisibilityConverter());
            Resources.Add("BoolToSearchLabelConverter", new BoolToSearchLabelConverter());

            InitializeComponent();

            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _viewModel = DataContext as MainViewModel;
            
            if (_viewModel != null)
            {
                _viewModel.QBitRefreshRequested += OnQBitRefreshRequested;
                _viewModel.PropertyChanged += ViewModel_PropertyChanged;
                _viewModel.OpenCrunchyrollSearchRequested += OnOpenCrunchyrollSearchRequested;
            }

            // Initialize WebView2 controls
            try
            {
                await LocalQBitWebView.EnsureCoreWebView2Async();
                _localWebViewInitialized = true;
                
                await SeedboxQBitWebView.EnsureCoreWebView2Async();
                _seedboxWebViewInitialized = true;

                // Set up navigation event handlers
                LocalQBitWebView.NavigationCompleted += LocalWebView_NavigationCompleted;
                LocalQBitWebView.NavigationStarting += WebView_NavigationStarting;
                SeedboxQBitWebView.NavigationCompleted += SeedboxWebView_NavigationCompleted;
                SeedboxQBitWebView.NavigationStarting += WebView_NavigationStarting;

                // Configure WebView2 settings for better qBittorrent compatibility
                ConfigureWebView(LocalQBitWebView);
                ConfigureWebView(SeedboxQBitWebView);

                // Navigate to initial URLs
                NavigateWebViews();
            }
            catch (Exception ex)
            {
                _viewModel?.SetQBitConnectionStatus("WebView2 Error");
                MessageBox.Show(
                    $"WebView2 initialization failed: {ex.Message}\n\nMake sure WebView2 Runtime is installed.\nDownload from: https://developer.microsoft.com/microsoft-edge/webview2/",
                    "WebView2 Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.IsLocalQBitSelected) ||
                e.PropertyName == nameof(MainViewModel.IsSeedboxQBitSelected) ||
                e.PropertyName == nameof(MainViewModel.LocalQBitUrl) ||
                e.PropertyName == nameof(MainViewModel.SeedboxQBitUrl))
            {
                NavigateWebViews();
            }
        }

        private void NavigateWebViews()
        {
            if (_viewModel == null) return;

            try
            {
                // Navigate local WebView
                if (_localWebViewInitialized && !string.IsNullOrWhiteSpace(_viewModel.LocalQBitUrl))
                {
                    var localUrl = _viewModel.LocalQBitUrl;
                    if (Uri.TryCreate(localUrl, UriKind.Absolute, out var localUri))
                    {
                        if (LocalQBitWebView.Source?.ToString() != localUri.ToString())
                        {
                            LocalQBitWebView.Source = localUri;
                        }
                    }
                }

                // Navigate seedbox WebView
                if (_seedboxWebViewInitialized && !string.IsNullOrWhiteSpace(_viewModel.SeedboxQBitUrl))
                {
                    var seedboxUrl = _viewModel.SeedboxQBitUrl;
                    if (Uri.TryCreate(seedboxUrl, UriKind.Absolute, out var seedboxUri))
                    {
                        if (SeedboxQBitWebView.Source?.ToString() != seedboxUri.ToString())
                        {
                            SeedboxQBitWebView.Source = seedboxUri;
                        }
                    }
                }

                // Update status based on current selection
                _viewModel.UpdateQBitConnectionStatus();
            }
            catch (Exception ex)
            {
                _viewModel?.SetQBitConnectionStatus("Navigation Error");
                System.Diagnostics.Debug.WriteLine($"WebView navigation error: {ex.Message}");
            }
        }

        private void ConfigureWebView(Microsoft.Web.WebView2.Wpf.WebView2 webView)
        {
            if (webView.CoreWebView2 != null)
            {
                // Allow all certificates (for self-signed certs on local/seedbox)
                webView.CoreWebView2.Settings.IsWebMessageEnabled = true;
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            }
        }

        private void WebView_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            _viewModel?.SetQBitConnectionStatus("Connecting");
        }

        private void LocalWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (_viewModel?.IsLocalQBitSelected == true)
            {
                UpdateConnectionStatus(e);
            }
        }

        private void SeedboxWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (_viewModel?.IsSeedboxQBitSelected == true)
            {
                UpdateConnectionStatus(e);
            }
        }

        private void UpdateConnectionStatus(CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                _viewModel?.SetQBitConnectionStatus("Connected");
            }
            else
            {
                _viewModel?.SetQBitConnectionStatus("Disconnected");
            }
        }

        private void OnQBitRefreshRequested(object? sender, EventArgs e)
        {
            try
            {
                if (_viewModel?.IsLocalQBitSelected == true && _localWebViewInitialized)
                {
                    LocalQBitWebView.Reload();
                }
                else if (_seedboxWebViewInitialized)
                {
                    SeedboxQBitWebView.Reload();
                }
            }
            catch (Exception)
            {
                // WebView might not be initialized yet
            }
        }

        // ── Crunchyroll sign-in: transfer PasswordBox value to VM ───────────

        private void CrunchyrollSignIn_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (_viewModel == null) return;
            // Transfer PasswordBox value (WPF PasswordBox doesn't support binding)
            _viewModel.CrunchyrollPassword = CrunchyrollPasswordBox.Password;
            // Now invoke the command, which calls AuthenticateAsync with the updated password
            if (_viewModel.CrunchyrollSignInCommand.CanExecute(null))
                _viewModel.CrunchyrollSignInCommand.Execute(null);
        }

        // ── Crunchyroll search dialog ────────────────────────────────────────

        private void OnOpenCrunchyrollSearchRequested(object? sender, EventArgs e)
        {
            if (_viewModel == null) return;

            var vm     = new CrunchyrollSearchViewModel(_viewModel.CrunchyrollApi);
            var dialog = new CrunchyrollSearchWindow(vm) { Owner = this };

            if (dialog.ShowDialog() == true && vm.SelectedSeries != null)
            {
                _viewModel.ApplyCrunchyrollSeries(vm.SelectedSeries);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.QBitRefreshRequested -= OnQBitRefreshRequested;
                _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
                _viewModel.OpenCrunchyrollSearchRequested -= OnOpenCrunchyrollSearchRequested;
            }
            
            LocalQBitWebView?.Dispose();
            SeedboxQBitWebView?.Dispose();
            
            base.OnClosed(e);
        }
    }
}
