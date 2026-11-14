using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Clipboard = System.Windows.Clipboard;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using WpfMenuItem = System.Windows.Controls.MenuItem;

namespace AdvancedFamilyManager
{
    // Converter to keep the height proportional to the width
    public class CardHeightConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double width)
            {
                // Original ratio is 175/145 = 1.20689655
                return width * 1.20689655;
            }
            return 175.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class AppConfig
    {
        public string? LibraryPath { get; set; }
        public string? SelectedCategory { get; set; }
        public string? SelectedSubfolder { get; set; }
        public bool SearchGlobal { get; set; }
        public bool OverwriteParameterValues { get; set; } = true;
        public List<string> Favorites { get; set; } = new();
        public double WindowWidth { get; set; } = 1120;
        public double WindowHeight { get; set; } = 680;
        public double? WindowLeft { get; set; }
        public double? WindowTop { get; set; }
        public double SidebarWidth { get; set; } = 250;
        public double CardSize { get; set; } = 145;
    }

    public class CategoryItem
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
        public bool IsFavorites { get; set; }
        public override string ToString() => IsFavorites ? $"⭐ Favorites ({Count})" : $"{Name} ({Count})";
    }

    public class SubfolderItem
    {
        public string RelativePath { get; set; } = string.Empty;
        public string DisplayText { get; set; } = string.Empty;
        public int Count { get; set; }
        public override string ToString() => DisplayText;
    }

    public partial class ManagerWindow : Window
    {
        private readonly string _configPath;
        private string _rootFolder = string.Empty;
        private bool _isInitializing = true;
        private bool _isClosing = false;

        private CancellationTokenSource? _thumbCts;
        private readonly HashSet<string> _favorites = new(StringComparer.OrdinalIgnoreCase);

        private FileSystemWatcher? _watcher;
        private DispatcherTimer? _watcherDebounce;

        private List<FamilyItem> _activeBaseList = new();

        public List<string> SelectedFamilyPaths { get; } = new();
        public bool PlaceAfterLoad { get; private set; }
        public bool OverwriteParameterValues { get; private set; } = true;

        public ManagerWindow()
        {
            InitializeComponent();

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string configDir = Path.Combine(appData, "AdvancedFamilyManager");
            Directory.CreateDirectory(configDir);
            _configPath = Path.Combine(configDir, "config.json");

            PopulateSmartPathMenu();
            LoadSavedConfig();
            _isInitializing = false;
        }

        // ---------- CONFIG ----------

        private void LoadSavedConfig()
        {
            if (!File.Exists(_configPath))
            {
                if (ChkOverwriteParams != null) ChkOverwriteParams.IsChecked = true;
                return;
            }

            try
            {
                string json = File.ReadAllText(_configPath);
                AppConfig? cfg = JsonSerializer.Deserialize<AppConfig>(json);
                if (cfg == null) return;

                if (cfg.Favorites != null)
                    foreach (var fav in cfg.Favorites)
                        if (!string.IsNullOrEmpty(fav))
                            _favorites.Add(fav);

                if (ChkOverwriteParams != null) ChkOverwriteParams.IsChecked = cfg.OverwriteParameterValues;
                ApplyWindowGeometry(cfg);

                if (CardSizeSlider != null && cfg.CardSize >= 110 && cfg.CardSize <= 250)
                {
                    CardSizeSlider.Value = cfg.CardSize;
                }

                if (!string.IsNullOrEmpty(cfg.LibraryPath) && Directory.Exists(cfg.LibraryPath))
                {
                    string savedPath = cfg.LibraryPath!;
                    TxtLibraryPath.Text = savedPath;
                    SetLibraryRoot(savedPath, cfg.SelectedCategory, cfg.SelectedSubfolder);
                }

                if (ChkGlobalSearch != null) ChkGlobalSearch.IsChecked = cfg.SearchGlobal;
            }
            catch { }
        }

        private void ApplyWindowGeometry(AppConfig cfg)
        {
            if (cfg.WindowWidth > 400 && cfg.WindowWidth < 5000) Width = cfg.WindowWidth;
            if (cfg.WindowHeight > 300 && cfg.WindowHeight < 5000) Height = cfg.WindowHeight;

            if (cfg.WindowLeft.HasValue && cfg.WindowTop.HasValue &&
                !double.IsNaN(cfg.WindowLeft.Value) && !double.IsNaN(cfg.WindowTop.Value))
            {
                var virtualScreen = new Rect(
                    SystemParameters.VirtualScreenLeft,
                    SystemParameters.VirtualScreenTop,
                    SystemParameters.VirtualScreenWidth,
                    SystemParameters.VirtualScreenHeight);
                var windowRect = new Rect(cfg.WindowLeft.Value, cfg.WindowTop.Value, Width, Height);
                if (virtualScreen.IntersectsWith(windowRect))
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = cfg.WindowLeft.Value;
                    Top = cfg.WindowTop.Value;
                }
            }

            if (ColSidebar != null && cfg.SidebarWidth >= 160 && cfg.SidebarWidth <= 2000)
                ColSidebar.Width = new GridLength(cfg.SidebarWidth);
        }

        private void SaveConfig()
        {
            if (_isInitializing) return;

            var cfg = new AppConfig
            {
                LibraryPath = TxtLibraryPath.Text.Trim(),
                SelectedCategory = (LstCategories.SelectedItem as CategoryItem)?.Name,
                SelectedSubfolder = (LstSubfolders.SelectedItem as SubfolderItem)?.RelativePath,
                SearchGlobal = ChkGlobalSearch.IsChecked ?? false,
                OverwriteParameterValues = ChkOverwriteParams.IsChecked ?? true,
                Favorites = _favorites.ToList(),
                WindowWidth = double.IsNaN(Width) ? 1120 : Width,
                WindowHeight = double.IsNaN(Height) ? 680 : Height,
                WindowLeft = double.IsNaN(Left) ? null : Left,
                WindowTop = double.IsNaN(Top) ? null : Top,
                SidebarWidth = ColSidebar != null && ColSidebar.Width.IsAbsolute ? ColSidebar.Width.Value : 250,
                CardSize = CardSizeSlider != null ? CardSizeSlider.Value : 145
            };

            try
            {
                File.WriteAllText(_configPath, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        private void ManagerWindow_Closing(object? sender, CancelEventArgs e)
        {
            _isClosing = true;
            SaveConfig();
            DisposeWatcher();
            _thumbCts?.Cancel();
        }

        private void CardSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            SaveConfig();
        }

        // ---------- SMART PATH MENU ----------

        private void PopulateSmartPathMenu()
        {
            if (MenuLibraryPaths == null) return;

            var paths = new List<string>();

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string cacheFile = Path.Combine(appData, "AdvancedFamilyManager", "revit_places.cache");
            if (File.Exists(cacheFile))
            {
                try
                {
                    foreach (string line in File.ReadAllLines(cacheFile))
                        if (Directory.Exists(line) && !paths.Contains(line))
                            paths.Add(line);
                }
                catch { }
            }

            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            foreach (string year in new[] { "2026", "2025", "2024" })
            {
                string path = Path.Combine(programData, "Autodesk", $"RVT {year}", "Libraries", "English");
                if (Directory.Exists(path) && !paths.Contains(path))
                    paths.Add(path);
            }

            if (File.Exists(_configPath))
            {
                try
                {
                    string json = File.ReadAllText(_configPath);
                    AppConfig? cfg = JsonSerializer.Deserialize<AppConfig>(json);
                    string? cfgPath = cfg?.LibraryPath;
                    if (!string.IsNullOrEmpty(cfgPath) && Directory.Exists(cfgPath) && !paths.Contains(cfgPath!))
                        paths.Insert(0, cfgPath!);
                }
                catch { }
            }

            MenuLibraryPaths.Items.Clear();
            if (paths.Count == 0)
            {
                MenuLibraryPaths.Items.Add(new WpfMenuItem { Header = "(No Revit library places found)", IsEnabled = false });
            }
            else
            {
                foreach (string p in paths)
                {
                    var item = new WpfMenuItem { Header = p };
                    item.Click += (s, e) =>
                    {
                        TxtLibraryPath.Text = p;
                        SetLibraryRoot(p, null, null);
                        SaveConfig();
                    };
                    MenuLibraryPaths.Items.Add(item);
                }
            }
        }

        private void BtnShowPaths_Click(object sender, RoutedEventArgs e)
        {
            PopulateSmartPathMenu();
            if (MenuLibraryPaths != null)
            {
                MenuLibraryPaths.PlacementTarget = BtnShowPaths;
                MenuLibraryPaths.Placement = PlacementMode.Bottom;
                MenuLibraryPaths.IsOpen = true;
            }
        }

        // ---------- LIBRARY ROOT / CATEGORIES ----------

        private void BtnSelectFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select Root Revit Families Directory"
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                TxtLibraryPath.Text = dialog.SelectedPath;
                SetLibraryRoot(dialog.SelectedPath, null, null);
                SaveConfig();
            }
        }

        private void TxtLibraryPath_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) ApplyTypedLibraryPath();
        }

        private void TxtLibraryPath_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            ApplyTypedLibraryPath();
        }

        private void ApplyTypedLibraryPath()
        {
            if (_isInitializing) return;
            string typedPath = TxtLibraryPath.Text.Trim();
            if (Directory.Exists(typedPath) && typedPath != _rootFolder)
            {
                SetLibraryRoot(typedPath, null, null);
                SaveConfig();
            }
        }

        private void SetLibraryRoot(string rootPath, string? restoreCategory, string? restoreSubfolder)
        {
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath)) return;
            _rootFolder = rootPath;
            SetupWatcher(rootPath);

            var categories = new List<CategoryItem>();

            int favCount = _favorites.Count(File.Exists);
            categories.Add(new CategoryItem { Name = "Favorites", Count = favCount, IsFavorites = true });

            try
            {
                var dirs = Directory.GetDirectories(rootPath)
                                    .Select(Path.GetFileName)
                                    .Where(n => !string.IsNullOrEmpty(n))
                                    .Cast<string>()
                                    .OrderBy(n => n);

                foreach (var name in dirs)
                {
                    int count = 0;
                    try { count = Directory.EnumerateFiles(Path.Combine(rootPath, name), "*.rfa", SearchOption.AllDirectories).Count(); }
                    catch { }
                    categories.Add(new CategoryItem { Name = name, Count = count });
                }
            }
            catch { }

            LstCategories.ItemsSource = categories;

            if (categories.Count == 0) return;

            CategoryItem? restore = null;
            if (!string.IsNullOrEmpty(restoreCategory))
                restore = categories.FirstOrDefault(c => !c.IsFavorites && c.Name == restoreCategory);

            if (restore != null)
            {
                LstCategories.SelectedItem = restore;
                LoadSubfoldersForCategory(restore.Name, restoreSubfolder);
            }
            else
            {
                var firstReal = categories.FirstOrDefault(c => !c.IsFavorites);
                LstCategories.SelectedItem = firstReal ?? categories[0];
            }
        }

        private void LoadSubfoldersForCategory(string categoryName, string? restoreSubfolder)
        {
            if (string.IsNullOrEmpty(categoryName)) return;
            string catPath = Path.Combine(_rootFolder, categoryName);

            var subDirs = new List<SubfolderItem>
            {
                new SubfolderItem { RelativePath = "", DisplayText = "All Subfolders (All Files)", Count = 0 }
            };

            if (Directory.Exists(catPath))
            {
                try
                {
                    string[] allDirs = Directory.GetDirectories(catPath, "*", SearchOption.AllDirectories);
                    foreach (string dir in allDirs)
                    {
                        string relPath = GetRelativePath(catPath, dir);
                        if (string.IsNullOrEmpty(relPath)) continue;

                        int count = 0;
                        try { count = Directory.EnumerateFiles(dir, "*.rfa", SearchOption.AllDirectories).Count(); }
                        catch { }

                        subDirs.Add(new SubfolderItem
                        {
                            RelativePath = relPath,
                            DisplayText = $"{relPath} ({count})",
                            Count = count
                        });
                    }
                }
                catch { }
            }

            var ordered = subDirs.Skip(1).OrderBy(s => s.RelativePath).ToList();
            ordered.Insert(0, subDirs[0]);

            LstSubfolders.ItemsSource = ordered;

            if (!string.IsNullOrEmpty(restoreSubfolder))
            {
                var match = ordered.FirstOrDefault(s => s.RelativePath == restoreSubfolder);
                if (match != null)
                {
                    LstSubfolders.SelectedItem = match;
                    return;
                }
            }
            LstSubfolders.SelectedIndex = 0;
        }

        private static string GetRelativePath(string relativeTo, string path)
        {
            if (!relativeTo.EndsWith(Path.DirectorySeparatorChar.ToString()))
                relativeTo += Path.DirectorySeparatorChar;
            Uri baseUri = new Uri(relativeTo);
            Uri fullUri = new Uri(path);
            Uri relativeUri = baseUri.MakeRelativeUri(fullUri);
            return Uri.UnescapeDataString(relativeUri.ToString().Replace('/', Path.DirectorySeparatorChar));
        }

        private void LstCategories_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstCategories.SelectedItem == null || string.IsNullOrEmpty(_rootFolder)) return;
            if (LstCategories.SelectedItem is not CategoryItem cat) return;

            if (cat.IsFavorites)
                LstSubfolders.ItemsSource = new List<SubfolderItem>();
            else
                LoadSubfoldersForCategory(cat.Name, null);

            UpdateActiveFolderDisplay();
            SaveConfig();
        }

        private void LstSubfolders_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateActiveFolderDisplay();
            SaveConfig();
        }

        private void UpdateActiveFolderDisplay()
        {
            if (LstCategories.SelectedItem == null || string.IsNullOrEmpty(_rootFolder)) return;

            if (LstCategories.SelectedItem is CategoryItem cat && cat.IsFavorites)
            {
                ShowFavorites();
                return;
            }

            string selectedCat = (LstCategories.SelectedItem as CategoryItem)?.Name ?? "";
            if (string.IsNullOrEmpty(selectedCat)) return;

            string targetPath = Path.Combine(_rootFolder, selectedCat);
            if (LstSubfolders.SelectedItem is SubfolderItem sub && !string.IsNullOrEmpty(sub.RelativePath))
                targetPath = Path.Combine(targetPath, sub.RelativePath);

            LoadFamiliesForPath(targetPath);
        }

        // ---------- FAMILY LIST ----------

        private void LoadFamiliesForPath(string folderPath)
        {
            var items = new List<FamilyItem>();
            if (!Directory.Exists(folderPath))
            {
                _activeBaseList = items;
                SetDisplay(items, "");
                return;
            }

            string subCatName = Path.GetFileName(folderPath) ?? "";
            string[] files;
            try { files = Directory.GetFiles(folderPath, "*.rfa", SearchOption.AllDirectories); }
            catch
            {
                _activeBaseList = items;
                SetDisplay(items, "");
                return;
            }

            foreach (string file in files)
            {
                string? parentDir = Path.GetDirectoryName(file);
                items.Add(new FamilyItem
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    FullPath = file,
                    Category = parentDir != null ? (Path.GetFileName(parentDir) ?? "") : "",
                    SubCategory = subCatName,
                    IsFavorite = _favorites.Contains(file)
                });
            }

            _activeBaseList = items;
            SetDisplay(items, "");
        }

        private void ShowFavorites()
        {
            var items = new List<FamilyItem>();
            foreach (var path in _favorites)
            {
                if (!File.Exists(path)) continue;
                string? parentDir = Path.GetDirectoryName(path);
                items.Add(new FamilyItem
                {
                    Name = Path.GetFileNameWithoutExtension(path),
                    FullPath = path,
                    Category = parentDir != null ? (Path.GetFileName(Path.GetDirectoryName(parentDir)) ?? "") : "",
                    SubCategory = parentDir != null ? (Path.GetFileName(parentDir) ?? "") : "",
                    IsFavorite = true
                });
            }

            items = items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
            _activeBaseList = items;
            SetDisplay(items, " (Favorites)");
        }

        private void SetDisplay(List<FamilyItem> list, string statusSuffix)
        {
            LstFamilies.ItemsSource = list;
            TxtStatusCount.Text = $"{list.Count} Families Found{statusSuffix}";
            LoadThumbnailsAsync(list);
        }

        private async void LoadThumbnailsAsync(List<FamilyItem> items)
        {
            _thumbCts?.Cancel();
            _thumbCts?.Dispose();
            _thumbCts = new CancellationTokenSource();
            var token = _thumbCts.Token;

            try
            {
                foreach (var item in items)
                {
                    if (token.IsCancellationRequested) return;
                    if (item.IsThumbnailLoaded) continue;

                    var thumb = await Task.Run(() => ThumbnailHelper.GetThumbnail(item.FullPath), token);

                    if (token.IsCancellationRequested) return;

                    item.Thumbnail = thumb;
                    item.IsThumbnailLoaded = true;
                }
            }
            catch (OperationCanceledException) { }
            catch { }
        }

        // ---------- FILE SYSTEM WATCHER ----------

        private void SetupWatcher(string rootPath)
        {
            DisposeWatcher();
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath)) return;

            _watcherDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            _watcherDebounce.Tick += (s, e) =>
            {
                _watcherDebounce?.Stop();
                if (!_isClosing) RefreshCurrentView();
            };

            try
            {
                _watcher = new FileSystemWatcher(rootPath)
                {
                    IncludeSubdirectories = true,
                    Filter = "*.rfa",
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName,
                    EnableRaisingEvents = true
                };

                void Handler(object s, FileSystemEventArgs e)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_isClosing) return;
                        _watcherDebounce?.Stop();
                        _watcherDebounce?.Start();
                    }));
                }

                _watcher.Created += Handler;
                _watcher.Deleted += Handler;
                _watcher.Changed += Handler;
                _watcher.Renamed += (s, e) => Handler(s, new FileSystemEventArgs(WatcherChangeTypes.Renamed, e.FullPath, ""));
                _watcher.Error += (s, e) =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_isClosing) return;
                        _watcherDebounce?.Stop();
                        _watcherDebounce?.Start();
                    }));
                };
            }
            catch { _watcher = null; }
        }

        private void DisposeWatcher()
        {
            _watcherDebounce?.Stop();
            _watcher?.Dispose();
            _watcher = null;
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e) => RefreshCurrentView();

        private void RefreshCurrentView()
        {
            if (string.IsNullOrEmpty(_rootFolder)) return;

            string? currentCat = (LstCategories.SelectedItem as CategoryItem)?.Name;
            string? currentSub = (LstSubfolders.SelectedItem as SubfolderItem)?.RelativePath;

            SetLibraryRoot(_rootFolder, currentCat, currentSub);
        }

        // ---------- SEARCH ----------

        private void ChkGlobalSearch_Changed(object sender, RoutedEventArgs e) => SaveConfig();
        private void ChkOverwriteParams_Changed(object sender, RoutedEventArgs e) => SaveConfig();

        private void TxtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) PerformSearch();
        }

        private void BtnSearch_Click(object sender, RoutedEventArgs e) => PerformSearch();

        private void PerformSearch()
        {
            string query = TxtSearch.Text.Trim();
            bool global = ChkGlobalSearch.IsChecked ?? false;

            if (string.IsNullOrEmpty(query))
            {
                UpdateActiveFolderDisplay();
                SaveConfig();
                return;
            }

            if (global && !string.IsNullOrEmpty(_rootFolder))
            {
                var globalItems = new List<FamilyItem>();
                try
                {
                    var files = Directory.GetFiles(_rootFolder, "*.rfa", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        string name = Path.GetFileNameWithoutExtension(file);
                        if (name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;

                        string? parentDir = Path.GetDirectoryName(file);
                        globalItems.Add(new FamilyItem
                        {
                            Name = name,
                            FullPath = file,
                            Category = parentDir != null ? (Path.GetFileName(Path.GetDirectoryName(parentDir)) ?? "") : "",
                            SubCategory = parentDir != null ? (Path.GetFileName(parentDir) ?? "") : "",
                            IsFavorite = _favorites.Contains(file)
                        });
                    }
                }
                catch { }
                SetDisplay(globalItems, " (Global Search)");
            }
            else
            {
                var filtered = _activeBaseList
                    .Where(f => f.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
                SetDisplay(filtered, "");
            }

            SaveConfig();
        }

        // ---------- LOAD / PLACE ----------

        private void BtnLoadOnly_Click(object sender, RoutedEventArgs e) => ConfirmSelection(place: false);
        private void BtnLoadAndPlace_Click(object sender, RoutedEventArgs e) => ConfirmSelection(place: true);

        private void LstFamilies_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (LstFamilies.SelectedItem is FamilyItem item)
                ConfirmSingleSelection(item, place: true);
        }

        private void LstFamilies_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                ConfirmSelection(place: ctrl);
                e.Handled = true;
            }
        }

        private void ConfirmSelection(bool place)
        {
            var selected = LstFamilies.SelectedItems.OfType<FamilyItem>().ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Please select at least one family first.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SelectedFamilyPaths.Clear();
            foreach (var item in selected) SelectedFamilyPaths.Add(item.FullPath);

            PlaceAfterLoad = place;
            OverwriteParameterValues = ChkOverwriteParams.IsChecked ?? true;
            DialogResult = true;
            Close();
        }

        private void ConfirmSingleSelection(FamilyItem item, bool place)
        {
            SelectedFamilyPaths.Clear();
            SelectedFamilyPaths.Add(item.FullPath);
            PlaceAfterLoad = place;
            OverwriteParameterValues = ChkOverwriteParams.IsChecked ?? true;
            DialogResult = true;
            Close();
        }

        // ---------- CONTEXT MENU ----------

        private void CtxLoadOnly_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is FamilyItem item)
                ConfirmSingleSelection(item, place: false);
        }

        private void CtxLoadAndPlace_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is FamilyItem item)
                ConfirmSingleSelection(item, place: true);
        }

        private void CtxOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is FamilyItem item)
            {
                try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{item.FullPath}\""); }
                catch { }
            }
        }

        private void CtxCopyPath_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is FamilyItem item)
            {
                try { Clipboard.SetText(item.FullPath); } catch { }
            }
        }

        private void CtxCopyName_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is FamilyItem item)
            {
                try { Clipboard.SetText(item.Name); } catch { }
            }
        }

        private void CtxToggleFavorite_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is FamilyItem item)
                ToggleFavorite(item);
        }

        private void CtxCategoryAddFav_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is CategoryItem cat)
            {
                if (cat.IsFavorites || string.IsNullOrEmpty(_rootFolder)) return;
                string path = Path.Combine(_rootFolder, cat.Name);
                AddAllToFavorites(path);
            }
        }

        private void CtxSubfolderAddFav_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is SubfolderItem sub)
            {
                if (LstCategories.SelectedItem is not CategoryItem cat || cat.IsFavorites || string.IsNullOrEmpty(_rootFolder)) return;
                string path = Path.Combine(_rootFolder, cat.Name);
                if (!string.IsNullOrEmpty(sub.RelativePath))
                    path = Path.Combine(path, sub.RelativePath);
                
                AddAllToFavorites(path);
            }
        }

        private void AddAllToFavorites(string folderPath)
        {
            if (!Directory.Exists(folderPath)) return;
            try
            {
                var files = Directory.GetFiles(folderPath, "*.rfa", SearchOption.AllDirectories);
                bool changed = false;
                foreach (var file in files)
                {
                    if (_favorites.Add(file)) changed = true;
                }

                if (changed)
                {
                    SaveConfig();
                    RefreshCurrentView();
                }
            }
            catch { }
        }

        private void ToggleFavorite(FamilyItem item)
        {
            if (_favorites.Contains(item.FullPath))
            {
                _favorites.Remove(item.FullPath);
                item.IsFavorite = false;
            }
            else
            {
                _favorites.Add(item.FullPath);
                item.IsFavorite = true;
            }

            UpdateFavoritesCount();
            SaveConfig();
        }

        private void UpdateFavoritesCount()
        {
            if (LstCategories.ItemsSource is List<CategoryItem> cats)
            {
                var favCat = cats.FirstOrDefault(c => c.IsFavorites);
                if (favCat != null)
                {
                    favCat.Count = _favorites.Count(File.Exists);
                    LstCategories.Items.Refresh();
                }
            }
        }
    }
}