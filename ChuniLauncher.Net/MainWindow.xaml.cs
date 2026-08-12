using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ChuniLauncher.Net
{
    public partial class MainWindow : Window
    {
        private int _autoLaunchDelayMs = 30000;
        private bool _autoLaunchEnabled = true;
        
        private DispatcherTimer _timer;
        private int _idleTimeMs = 0;
        private bool _timerActive = true;
        private List<GameItem> _allGames = new List<GameItem>();
        private List<GameItem> _games = new List<GameItem>();
        private List<string> _categories = new List<string>();
        private int _currentCategoryIndex = 0;
        private string _baseDir;
        
        private string _sliderPort = "";
        private int _sliderSensitivity = 20;
        private SliderController? _sliderController;
        private LedController? _ledController;
        private string _backgroundImagePath = "";
        private bool[] _blockTouchStates = new bool[6];
        private string _led1Port = "";
        private string _led2Port = "";
        private double _sideBrightness = 1.0;

        public MainWindow()
        {
            InitializeComponent();
            _baseDir = AppDomain.CurrentDomain.BaseDirectory;
            
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(100);
            _timer.Tick += Timer_Tick;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Center UI elements
            TitleText.SetValue(Canvas.LeftProperty, (this.ActualWidth - TitleText.ActualWidth) / 2);
            CountdownText.SetValue(Canvas.LeftProperty, (this.ActualWidth - 1000) / 2);
            CountdownText.Width = 1000;
            CountdownText.TextAlignment = TextAlignment.Center;
            CountdownText.SetValue(Canvas.TopProperty, this.ActualHeight - 180);
            
            LoadAssets();
            LoadGames();

            if (!string.IsNullOrEmpty(_backgroundImagePath))
            {
                var absPath = GetAbsoluteAssetPath(_backgroundImagePath);
                if (absPath != null)
                {
                    BackgroundImage.Source = new BitmapImage(new Uri(absPath, UriKind.Absolute));
                }
                else
                {
                    BackgroundImage.Source = null;
                }
            }
            else
            {
                BackgroundImage.Source = null;
            }
            

            
            if (!string.IsNullOrEmpty(_sliderPort))
            {
                _sliderController = new SliderController(_sliderPort, _sliderSensitivity);
                _sliderController.OnUp += () => Dispatcher.Invoke(() => ProcessInput(Key.Up));
                _sliderController.OnDown += () => Dispatcher.Invoke(() => ProcessInput(Key.Down));
                _sliderController.OnSelect += () => Dispatcher.Invoke(() => ProcessInput(Key.Enter));
                _sliderController.OnNextCategory += () => Dispatcher.Invoke(() => ProcessInput(Key.Right));
                _sliderController.OnPrevCategory += () => Dispatcher.Invoke(() => ProcessInput(Key.Left));
                _sliderController.OnExit += () => Dispatcher.Invoke(() => Close());
                
                _sliderController.OnTouchStateChanged += (block, isTouched) => Dispatcher.Invoke(() => AnimateBlock(block, isTouched));
            }

            if (!string.IsNullOrEmpty(_led1Port) || !string.IsNullOrEmpty(_led2Port))
            {
                _ledController = new LedController(_led1Port, _led2Port, _sideBrightness);
            }
            
            UpdateCategoryDisplay();
            GamesList.SelectionChanged += GamesList_SelectionChanged;

            if (_games.Count > 0)
            {
                _timer.Start();
            }
            else
            {
                CountdownText.Text = "No games found in configuration";
            }
        }

        private void UpdateCategoryDisplay()
        {
            if (_categories.Count == 0) return;
            string currentCategory = _categories[_currentCategoryIndex];
            
            CategoryPanel.Children.Clear();
            foreach (var category in _categories)
            {
                var tb = new TextBlock
                {
                    Text = category,
                    FontSize = 24,
                    Margin = new Thickness(0, 0, 40, 0),
                    Foreground = category == currentCategory ? Brushes.White : new SolidColorBrush(Color.FromRgb(120, 120, 120))
                };
                
                if (TitleText.FontFamily != null)
                {
                    tb.FontFamily = TitleText.FontFamily;
                }
                
                tb.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    ShadowDepth = 2,
                    Direction = 330,
                    Color = Colors.Black,
                    Opacity = 0.8,
                    BlurRadius = 4
                };
                
                CategoryPanel.Children.Add(tb);
            }
            
            _games = _allGames.Where(g => (string.IsNullOrEmpty(g.Category) ? "Uncategorized" : g.Category) == currentCategory).ToList();
            GamesList.ItemsSource = _games;
            if (_games.Count > 0)
            {
                var selectedGame = _games[0];
                try {
                    var color = (Color)ColorConverter.ConvertFromString(selectedGame.ColorString ?? "#FFFFFF");
                    if (_ledController != null) _ledController.SetColor(color);
                } catch { }
                GamesList.SelectedIndex = 0;
            }
        }

        private string GetAssetPath(string fileName)
        {
            var path = Path.GetFullPath(fileName);
            if (File.Exists(path)) return path;

            path = Path.Combine(_baseDir, fileName);
            if (File.Exists(path)) return path;

            return null;
        }

        private string GetAbsoluteAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            if (Path.IsPathRooted(path)) return path;
            
            var localPath = Path.GetFullPath(path);
            if (File.Exists(localPath)) return localPath;
            
            return Path.GetFullPath(Path.Combine(_baseDir, path));
        }

        private void LoadAssets()
        {
            try
            {
                var bgPath = GetAssetPath("background.png");
                if (bgPath != null)
                {
                    BackgroundImage.Source = new BitmapImage(new Uri(bgPath));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading background: {ex.Message}");
            }
            
            try
            {
                var fontPath = GetAssetPath("font.ttf");
                if (fontPath != null)
                {
                    var families = Fonts.GetFontFamilies(fontPath);
                    if (families.Count > 0)
                    {
                        var customFont = families.First();
                        TitleText.FontFamily = customFont;
                        GamesList.FontFamily = customFont;
                        CountdownText.FontFamily = customFont;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading font: {ex.Message}");
            }
        }

        private void LoadGames()
        {
            string iniPath = null;
            var args = Environment.GetCommandLineArgs();
            if (args.Length > 1 && args[1].EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
            {
                iniPath = GetAssetPath(args[1]);
            }
            
            if (iniPath == null) iniPath = GetAssetPath("games.ini");
            if (iniPath == null) iniPath = GetAssetPath("game.ini");
            
            if (iniPath == null)
            {
                var files = System.IO.Directory.GetFiles(_baseDir, "*.ini");
                if (files.Length > 0) iniPath = files[0];
                else
                {
                    files = System.IO.Directory.GetFiles(Environment.CurrentDirectory, "*.ini");
                    if (files.Length > 0) iniPath = files[0];
                }
            }

            if (iniPath == null)
            {
                _allGames.Add(new GameItem { Name = "Error", ExecutablePath = "Missing .ini file" });
                return;
            }

            try
            {
                var lines = File.ReadAllLines(iniPath);
                string currentSection = null;
                GameItem currentGame = null;

                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(";")) continue;

                    if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                    {
                        if (currentGame != null)
                        {
                            _allGames.Add(currentGame);
                            currentGame = null;
                        }

                        currentSection = trimmed.Substring(1, trimmed.Length - 2).Trim();
                        if (!currentSection.Equals("Settings", StringComparison.OrdinalIgnoreCase))
                        {
                            currentGame = new GameItem { Name = currentSection };
                        }
                        continue;
                    }

                    var parts = trimmed.Split(new[] { '=' }, 2);
                    if (parts.Length != 2) continue;
                    
                    var key = parts[0].Trim().ToLower();
                    var value = parts[1].Trim();

                    if (currentSection != null && currentSection.Equals("Settings", StringComparison.OrdinalIgnoreCase))
                    {
                        if (key == "autoLaunch") _autoLaunchEnabled = value == "1" || value.ToLower() == "true";
                        if (key == "autoLaunchTime" && int.TryParse(value, out int time)) _autoLaunchDelayMs = time * 1000;
                        if (key == "sliderPort" || key == "sliderport") _sliderPort = value;
                        if ((key == "sliderSensitivity" || key == "sensitivity") && int.TryParse(value, out int sens)) _sliderSensitivity = sens;
                        if (key == "background") _backgroundImagePath = value;
                        if (key == "led1port") _led1Port = value;
                        if (key == "led2port") _led2Port = value;
                        if (key == "sidebrightness" && int.TryParse(value, out int sb)) _sideBrightness = Math.Min(100, Math.Max(0, sb)) / 100.0;
                    }
                    else if (currentGame != null)
                    {
                        if (key == "game") currentGame.ExecutablePath = value;
                        if (key == "logo") currentGame.LogoPath = GetAbsoluteAssetPath(value);
                        if (key == "colour") currentGame.ColorString = value;
                        if (key == "category" || key == "categeory") currentGame.Category = value;
                    }
                }

                if (currentGame != null)
                {
                    _allGames.Add(currentGame);
                }

                _categories = _allGames.Select(g => string.IsNullOrEmpty(g.Category) ? "Uncategorized" : g.Category).Distinct().ToList();
                if (_categories.Count == 0) _categories.Add("Uncategorized");
            }
            catch (Exception ex)
            {
                _allGames.Add(new GameItem { Name = "Error", ExecutablePath = ex.Message });
            }
        }

        private void GamesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_sliderController != null && _games.Count > 0)
            {
                _sliderController.IsAtTop = GamesList.SelectedIndex <= 0;
                _sliderController.IsAtBottom = GamesList.SelectedIndex >= _games.Count - 1;
                
                UpdateBoundaryOpacities();
            }

            if (_ledController != null && GamesList.SelectedIndex >= 0 && GamesList.SelectedIndex < _games.Count)
            {
                var selectedGame = _games[GamesList.SelectedIndex];
                try {
                    var color = (Color)ColorConverter.ConvertFromString(selectedGame.ColorString ?? "#FFFFFF");
                    _ledController.SetColor(color);
                } catch { }
            }
        }
        
        private void UpdateBoundaryOpacities()
        {
            if (_sliderController == null) return;
            
            if (!_blockTouchStates[0] && UI_Up != null)
            {
                double target = _sliderController.IsAtTop ? 0.2 : 1.0;
                if (UI_Up is Panel panel) {
                    foreach (UIElement child in panel.Children) {
                        child.BeginAnimation(UIElement.OpacityProperty, null);
                        child.Opacity = target;
                    }
                }
            }
            if (!_blockTouchStates[1] && UI_Down != null)
            {
                double target = _sliderController.IsAtBottom ? 0.2 : 1.0;
                if (UI_Down is Panel panel) {
                    foreach (UIElement child in panel.Children) {
                        child.BeginAnimation(UIElement.OpacityProperty, null);
                        child.Opacity = target;
                    }
                }
            }
        }

        private void AnimateBlock(int block, bool isTouched)
        {
            _blockTouchStates[block] = isTouched;
            UIElement target = null;
            double targetOpacity = isTouched ? 0.25 : 1.0;
            
            if (!isTouched && _sliderController != null)
            {
                if (block == 0 && _sliderController.IsAtTop) targetOpacity = 0.2;
                if (block == 1 && _sliderController.IsAtBottom) targetOpacity = 0.2;
            }

            switch(block)
            {
                case 0: target = UI_Up; break;
                case 1: target = UI_Down; break;
                case 2: target = UI_Select; break;
                case 3: target = UI_Prev; break;
                case 4: target = UI_Next; break;
                case 5: target = UI_Exit; break;
            }

            if (target != null && target is Panel panel)
            {
                if (isTouched)
                {
                    foreach (UIElement child in panel.Children)
                    {
                        child.BeginAnimation(UIElement.OpacityProperty, null);
                        child.Opacity = targetOpacity;
                    }
                }
                else
                {
                    var anim = new System.Windows.Media.Animation.DoubleAnimation
                    {
                        To = targetOpacity,
                        Duration = TimeSpan.FromMilliseconds(200)
                    };
                    foreach (UIElement child in panel.Children)
                    {
                        child.BeginAnimation(UIElement.OpacityProperty, anim);
                    }
                }
            }
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (!_timerActive || _games.Count == 0 || !_autoLaunchEnabled)
            {
                if (CountdownText.Text != "") CountdownText.Text = "";
                return;
            }

            _idleTimeMs += 100;

            if (_idleTimeMs >= _autoLaunchDelayMs)
            {
                _timer.Stop();
                LaunchGame(_games[0]); // Auto-launch first game
            }
            else
            {
                int secondsLeft = (int)Math.Ceiling((_autoLaunchDelayMs - _idleTimeMs) / 1000.0);
                string newText = $"Launching {_games[0].Name} in {secondsLeft} seconds";
                if (CountdownText.Text != newText)
                {
                    CountdownText.Text = newText;
                }
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_allGames.Count == 0) return;

            if (ProcessInput(e.Key))
            {
                e.Handled = true;
            }
        }

        private bool ProcessInput(Key key)
        {
            bool isHandledKey = false;

            if (key == Key.Up)
            {
                if (GamesList.SelectedIndex > 0)
                {
                    GamesList.SelectedIndex--;
                    GamesList.ScrollIntoView(GamesList.SelectedItem);
                }
                isHandledKey = true;
            }
            else if (key == Key.Down)
            {
                if (GamesList.SelectedIndex < _games.Count - 1)
                {
                    GamesList.SelectedIndex++;
                    GamesList.ScrollIntoView(GamesList.SelectedItem);
                }
                isHandledKey = true;
            }
            else if (key == Key.Left)
            {
                if (_categories.Count > 1)
                {
                    _currentCategoryIndex--;
                    if (_currentCategoryIndex < 0) _currentCategoryIndex = _categories.Count - 1;
                    UpdateCategoryDisplay();
                }
                isHandledKey = true;
            }
            else if (key == Key.Right)
            {
                if (_categories.Count > 1)
                {
                    _currentCategoryIndex++;
                    if (_currentCategoryIndex >= _categories.Count) _currentCategoryIndex = 0;
                    UpdateCategoryDisplay();
                }
                isHandledKey = true;
            }
            else if (key == Key.Enter)
            {
                isHandledKey = true;
                if (GamesList.SelectedItem is GameItem selectedGame)
                {
                    LaunchGame(selectedGame);
                }
            }
            else if (key == Key.Escape)
            {
                isHandledKey = true;
                Application.Current.Shutdown();
            }

            if (isHandledKey && _timerActive)
            {
                _timerActive = false;
                CountdownText.Text = "";
            }
            
            return isHandledKey;
        }
        
        private void LaunchGame(GameItem game)
        {
            if (string.IsNullOrEmpty(game.ExecutablePath) || game.Name == "Error") return;

            try
            {
                var startInfo = new ProcessStartInfo();
                if (game.ExecutablePath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
                {
                    startInfo.FileName = "cmd.exe";
                    startInfo.Arguments = $"/c \"{game.ExecutablePath}\"";
                    startInfo.CreateNoWindow = false;
                    startInfo.UseShellExecute = true;
                }
                else
                {
                    startInfo.FileName = game.ExecutablePath;
                    startInfo.UseShellExecute = true;
                }

                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error launching '{game.ExecutablePath}':\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            // Close launcher after launching game, matching original behavior
            Application.Current.Shutdown();
        }
    }
}
