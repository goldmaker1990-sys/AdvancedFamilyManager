using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Newtonsoft.Json;
using Clipboard = System.Windows.Clipboard;
using Color = System.Windows.Media.Color;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using WpfMenuItem = System.Windows.Controls.MenuItem;

namespace AdvancedFamilyManager
{
    public class CardHeightConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double width)
                return width * 1.20689655;
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
        public string? SelectedCategoryPath { get; set; }
        public string? SelectedSubfolder { get; set; }
        public bool SearchGlobal { get; set; }
        public bool OverwriteParameterValues { get; set; } = true;
        public List<string> Favorites { get; set; } = new();
        public double WindowWidth { get; set; } = 1120;
        public double WindowHeight { get; set; } = 680;
        public double? WindowLeft { get; set; }
        public double? WindowTop { get; set; }
        public double SidebarWidth { get; set; } = 270;
        public double CardSize { get; set; } = 145;
    }

    public class CategoryNode : INotifyPropertyChanged
    {
        private bool _isExpanded;
        private bool _isSelected;
        private int _count;

        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public bool IsFavorites { get; set; }
        public ObservableCollection<CategoryNode> Children { get; } = new();

        public int Count
        {
            get => _count;
            set
            {
                if (_count != value)
                {
                    _count = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayText));
                }
            }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public string DisplayText => IsFavorites ? $"⭐ Favorites ({Count})" : $"{Name} ({Count})";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string? prop = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    public class SubfolderItem
    {
        public string RelativePath { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string DisplayText { get; set; } = string.Empty;
        public int Count { get; set; }
        public override string ToString() => DisplayText;
    }

    public partial class ManagerWindow : Window
    {
        private readonly string _configPath;
        private readonly bool _isLightTheme;
        private string _rootFolder = string.Empty;
        private bool _isInitializing = true;
        private bool _isClosing = false;

        private CancellationTokenSource? _thumbCts;
        private readonly HashSet<string> _favorites = new(StringComparer.OrdinalIgnoreCase);

        private FileSystemWatcher? _watcher;
        private DispatcherTimer? _watcherDebounce;

        private List<FamilyItem> _activeBaseList = new();
        private List<SubfolderItem> _allSubfoldersForSelectedNode = new();
        private List<CategoryNode> _allRootNodes = new();
        private CategoryNode? _selectedCategoryNode;

        public List<string> SelectedFamilyPaths { get; } = new();
        public bool PlaceAfterLoad { get; private set; }
        public bool OverwriteParameterValues { get; private set; } = true;

        public ManagerWindow(bool isLightTheme = false)
        {
            _isLightTheme = isLightTheme;
            InitializeComponent();
            ApplyRevitTheme(_isLightTheme);

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string configDir = Path.Combine(appData, "AdvancedFamilyManager");
            Directory.CreateDirectory(configDir);
            _configPath = Path.Combine(configDir, "config.json");

            PopulateSmartPathMenu();
            LoadSavedConfig();
            _isInitializing = false;
        }

        // ---------- THEME ----------

        private void ApplyRevitTheme(bool isLight)
        {
            if (!isLight) return;

            SetBrush("BgWindow", "#F3F3F3");
            SetBrush("BgSurface", "#FFFFFF");
            SetBrush("BgCard", "#F9F9FB");
            SetBrush("BgInput", "#FFFFFF");
            SetBrush("BgHover", "#E5E7EB");
            SetBrush("BgThumb", "#ECEFF3");
            SetBrush("FgPrimary", "#1E1E1E");
            SetBrush("FgList", "#222222");
            SetBrush("FgSecondary", "#555555");
            SetBrush("FgMuted", "#767676");
            SetBrush("FgCategoryHeader", "#005A9E");
            SetBrush("FgSubfolderHeader", "#9A4F1E");
            SetBrush("BorderBrushMain", "#D0D4DA");
            SetBrush("BorderBrushCard", "#DCDFE4");
            SetBrush("AccentBrush", "#0067C0");
            SetBrush("LoadPlaceBrush", "#1E8E3E");
        }

        private void SetBrush(string key, string hex)
        {
            var brush = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            Resources[key] = brush;
        }

        // ---------- SMART DEFAULT PATH DRILL-DOWN ----------

        private static string DrillDownSingleChildFolder(string path, int maxDepth = 5)
        {
            string current = path;
            for (int depth = 0; depth < maxDepth; depth++)
            {
                if (!Directory.Exists(current)) break;

                try
                {
                    bool hasDirectFamilies = Directory.EnumerateFiles(current, "*.rfa", SearchOption.TopDirectoryOnly).Any();
                    if (hasDirectFamilies) break;

                    string[] childDirs = Directory.GetDirectories(current);
                    if (childDirs.Length == 1)
                    {
                        current = childDirs[0];
                    }
                    else
                    {
                        break;
                    }
                }
                catch
                {
                    break;
                }
            }
            return current;
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
                AppConfig? cfg = JsonConvert.DeserializeObject<AppConfig>(json);
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
                    string drilled = DrillDownSingleChildFolder(cfg.LibraryPath!);
                    TxtLibraryPath.Text = drilled;
                    SetLibraryRoot(drilled, cfg.SelectedCategoryPath, cfg.SelectedSubfolder);
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

            if (ColSidebar != null && cfg.SidebarWidth >= 180 && cfg.SidebarWidth <= 2000)
                ColSidebar.Width = new GridLength(cfg.SidebarWidth);
        }

        private void SaveConfig()
        {
            if (_isInitializing) return;

            var cfg = new AppConfig
            {
                LibraryPath = TxtLibraryPath.Text.Trim(),
                SelectedCategoryPath = _selectedCategoryNode?.IsFavorites == true
                    ? "__FAVORITES__"
                    : _selectedCategoryNode?.FullPath,
                SelectedSubfolder = (LstSubfolders.SelectedItem as SubfolderItem)?.RelativePath,
                SearchGlobal = ChkGlobalSearch.IsChecked ?? false,
                OverwriteParameterValues = ChkOverwriteParams.IsChecked ?? true,
                Favorites = _favorites.ToList(),
                WindowWidth = double.IsNaN(Width) ? 1120 : Width,
                WindowHeight = double.IsNaN(Height) ? 680 : Height,
                WindowLeft = double.IsNaN(Left) ? null : Left,
                WindowTop = double.IsNaN(Top) ? null : Top,
                SidebarWidth = ColSidebar != null && ColSidebar.Width.IsAbsolute ? ColSidebar.Width.Value : 270,
                CardSize = CardSizeSlider != null ? CardSizeSlider.Value : 145
            };

            try
            {
                File.WriteAllText(_configPath, JsonConvert.SerializeObject(cfg, Formatting.Indented));
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
                    {
                        string drilled = DrillDownSingleChildFolder(line.Trim());
                        if (Directory.Exists(drilled) && !paths.Contains(drilled, StringComparer.OrdinalIgnoreCase))
                            paths.Add(drilled);
                    }
                }
                catch { }
            }

            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            foreach (string year in new[] { "2026", "2025", "2024", "2023", "2022", "2021", "2020" })
            {
                string path = Path.Combine(programData, "Autodesk", $"RVT {year}", "Libraries", "English");
                if (Directory.Exists(path))
                {
                    string drilled = DrillDownSingleChildFolder(path);
                    if (!paths.Contains(drilled, StringComparer.OrdinalIgnoreCase))
                        paths.Add(drilled);
                }
            }

            if (File.Exists(_configPath))
            {
                try
                {
                    string json = File.ReadAllText(_configPath);
                    AppConfig? cfg = JsonConvert.DeserializeObject<AppConfig>(json);
                    string? cfgPath = cfg?.LibraryPath;
                    if (!string.IsNullOrEmpty(cfgPath) && Directory.Exists(cfgPath))
                    {
                        string drilled = DrillDownSingleChildFolder(cfgPath!);
                        if (!paths.Contains(drilled, StringComparer.OrdinalIgnoreCase))
                            paths.Insert(0, drilled);
                    }
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
                        string drilled = DrillDownSingleChildFolder(p);
                        TxtLibraryPath.Text = drilled;
                        SetLibraryRoot(drilled, null, null);
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

        // ---------- LIBRARY ROOT / EXPANDABLE CATEGORY TREE ----------

        private void BtnSelectFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select Root Revit Families Directory"
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string drilled = DrillDownSingleChildFolder(dialog.SelectedPath);
                TxtLibraryPath.Text = drilled;
                SetLibraryRoot(drilled, null, null);
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
            if (Directory.Exists(typedPath))
            {
                string drilled = DrillDownSingleChildFolder(typedPath);
                if (drilled != typedPath)
                    TxtLibraryPath.Text = drilled;

                if (drilled != _rootFolder)
                {
                    SetLibraryRoot(drilled, null, null);
                    SaveConfig();
                }
            }
        }

        private void SetLibraryRoot(string rootPath, string? restoreCategoryPath, string? restoreSubfolder)
        {
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath)) return;
            _rootFolder = rootPath;
            SetupWatcher(rootPath);

            var rootNodes = new List<CategoryNode>();

            int favCount = _favorites.Count(File.Exists);
            var favNode = new CategoryNode
            {
                Name = "Favorites",
                FullPath = "__FAVORITES__",
                Count = favCount,
                IsFavorites = true
            };
            rootNodes.Add(favNode);

            try
            {
                var dirs = Directory.GetDirectories(rootPath)
                                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);

                foreach (var dir in dirs)
                {
                    string name = Path.GetFileName(dir);
                    if (string.IsNullOrEmpty(name)) continue;

                    var node = BuildCategoryNode(dir, name);
                    rootNodes.Add(node);
                }
            }
            catch { }

            _allRootNodes = rootNodes;

            if (TxtCategoryFilter != null && !string.IsNullOrEmpty(TxtCategoryFilter.Text))
                TxtCategoryFilter.Text = string.Empty;

            TreeCategories.ItemsSource = new ObservableCollection<CategoryNode>(_allRootNodes);

            CategoryNode? targetNode = null;
            if (!string.IsNullOrEmpty(restoreCategoryPath))
            {
                targetNode = restoreCategoryPath == "__FAVORITES__"
                    ? favNode
                    : FindNodeByPath(_allRootNodes, restoreCategoryPath);
            }

            if (targetNode == null)
                targetNode = _allRootNodes.FirstOrDefault(n => !n.IsFavorites) ?? favNode;

            ExpandAncestors(_allRootNodes, targetNode);
            targetNode.IsSelected = true;
            _selectedCategoryNode = targetNode;
            LoadSubfoldersForNode(targetNode, restoreSubfolder);
            UpdateActiveFolderDisplay();
        }

        private void TxtCategoryFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing) return;
            ApplyCategoryFilter();
        }

        private void BtnClearCategoryFilter_Click(object sender, RoutedEventArgs e)
        {
            TxtCategoryFilter.Text = string.Empty;
            TxtCategoryFilter.Focus();
        }

        private void ApplyCategoryFilter()
        {
            if (_allRootNodes.Count == 0) return;

            string filter = TxtCategoryFilter?.Text?.Trim() ?? string.Empty;
            var displayed = new ObservableCollection<CategoryNode>();

            var favNode = _allRootNodes.FirstOrDefault(n => n.IsFavorites);
            if (favNode != null)
                displayed.Add(favNode);

            foreach (var node in _allRootNodes.Where(n => !n.IsFavorites))
            {
                if (string.IsNullOrEmpty(filter))
                {
                    displayed.Add(node);
                }
                else
                {
                    var matched = FilterCategoryNode(node, filter, expandMatches: true);
                    if (matched != null)
                        displayed.Add(matched);
                }
            }

            TreeCategories.ItemsSource = displayed;
        }

        private static CategoryNode? FilterCategoryNode(CategoryNode source, string filter, bool expandMatches)
        {
            bool selfMatches = source.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
            var matchingChildren = new List<CategoryNode>();

            foreach (var child in source.Children)
            {
                var filteredChild = FilterCategoryNode(child, filter, expandMatches);
                if (filteredChild != null)
                    matchingChildren.Add(filteredChild);
            }

            if (!selfMatches && matchingChildren.Count == 0)
                return null;

            var clone = new CategoryNode
            {
                Name = source.Name,
                FullPath = source.FullPath,
                Count = source.Count,
                IsFavorites = source.IsFavorites,
                IsExpanded = expandMatches && matchingChildren.Count > 0
            };

            foreach (var child in matchingChildren)
                clone.Children.Add(child);

            return clone;
        }

        private static CategoryNode BuildCategoryNode(string dirPath, string name)
        {
            int count = 0;
            try { count = Directory.EnumerateFiles(dirPath, "*.rfa", SearchOption.AllDirectories).Count(); }
            catch { }

            var node = new CategoryNode
            {
                Name = name,
                FullPath = dirPath,
                Count = count
            };

            try
            {
                var subDirs = Directory.GetDirectories(dirPath)
                                       .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
                foreach (var subDir in subDirs)
                {
                    string subName = Path.GetFileName(subDir);
                    if (!string.IsNullOrEmpty(subName))
                    {
                        node.Children.Add(BuildCategoryNode(subDir, subName));
                    }
                }
            }
            catch { }

            return node;
        }

        private static CategoryNode? FindNodeByPath(IEnumerable<CategoryNode> nodes, string? fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return null;

            foreach (var node in nodes)
            {
                if (!node.IsFavorites && string.Equals(node.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
                    return node;

                var found = FindNodeByPath(node.Children, fullPath);
                if (found != null) return found;
            }
            return null;
        }

        private static bool ExpandAncestors(IEnumerable<CategoryNode> nodes, CategoryNode target)
        {
            foreach (var node in nodes)
            {
                if (ReferenceEquals(node, target)) return true;
                if (ExpandAncestors(node.Children, target))
                {
                    node.IsExpanded = true;
                    return true;
                }
            }
            return false;
        }

        private void TreeCategories_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is not CategoryNode node || string.IsNullOrEmpty(_rootFolder)) return;

            _selectedCategoryNode = node;

            if (TxtSubfolderFilter != null && !string.IsNullOrEmpty(TxtSubfolderFilter.Text))
                TxtSubfolderFilter.Text = string.Empty;

            if (node.IsFavorites)
            {
                _allSubfoldersForSelectedNode = new List<SubfolderItem>();
                LstSubfolders.ItemsSource = _allSubfoldersForSelectedNode;
            }
            else
            {
                LoadSubfoldersForNode(node, null);
            }

            UpdateActiveFolderDisplay();
            SaveConfig();
        }

        // ---------- SUBFOLDERS + LIVE FILTER ----------

        private void LoadSubfoldersForNode(CategoryNode node, string? restoreSubfolder)
        {
            if (node.IsFavorites || string.IsNullOrEmpty(node.FullPath) || !Directory.Exists(node.FullPath))
            {
                _allSubfoldersForSelectedNode = new List<SubfolderItem>();
                LstSubfolders.ItemsSource = _allSubfoldersForSelectedNode;
                return;
            }

            int totalInNode = 0;
            try { totalInNode = Directory.EnumerateFiles(node.FullPath, "*.rfa", SearchOption.AllDirectories).Count(); }
            catch { }

            var list = new List<SubfolderItem>
            {
                new SubfolderItem
                {
                    RelativePath = "",
                    FullPath = node.FullPath,
                    DisplayText = $"All Subfolders ({totalInNode})",
                    Count = totalInNode
                }
            };

            try
            {
                string[] allDirs = Directory.GetDirectories(node.FullPath, "*", SearchOption.AllDirectories);
                var childItems = new List<SubfolderItem>();

                foreach (string dir in allDirs)
                {
                    string relPath = GetRelativePath(node.FullPath, dir);
                    if (string.IsNullOrEmpty(relPath)) continue;

                    int count = 0;
                    try { count = Directory.EnumerateFiles(dir, "*.rfa", SearchOption.AllDirectories).Count(); }
                    catch { }

                    childItems.Add(new SubfolderItem
                    {
                        RelativePath = relPath,
                        FullPath = dir,
                        DisplayText = $"{relPath} ({count})",
                        Count = count
                    });
                }

                list.AddRange(childItems.OrderBy(s => s.RelativePath, StringComparer.OrdinalIgnoreCase));
            }
            catch { }

            _allSubfoldersForSelectedNode = list;
            ApplySubfolderFilter(restoreSubfolder);
        }

        private void ApplySubfolderFilter(string? preferredRelativePath = null)
        {
            string filter = TxtSubfolderFilter?.Text?.Trim() ?? string.Empty;
            var currentSelectedRel = preferredRelativePath ?? (LstSubfolders.SelectedItem as SubfolderItem)?.RelativePath;

            List<SubfolderItem> filtered;
            if (string.IsNullOrEmpty(filter))
            {
                filtered = _allSubfoldersForSelectedNode.ToList();
            }
            else
            {
                filtered = _allSubfoldersForSelectedNode
                    .Where(s => string.IsNullOrEmpty(s.RelativePath) ||
                                s.RelativePath.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }

            LstSubfolders.ItemsSource = filtered;

            if (!string.IsNullOrEmpty(currentSelectedRel))
            {
                var match = filtered.FirstOrDefault(s =>
                    string.Equals(s.RelativePath, currentSelectedRel, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    LstSubfolders.SelectedItem = match;
                    return;
                }
            }

            if (filtered.Count > 0)
                LstSubfolders.SelectedIndex = 0;
        }

        private void TxtSubfolderFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing) return;
            ApplySubfolderFilter();
        }

        private void BtnClearSubfolderFilter_Click(object sender, RoutedEventArgs e)
        {
            TxtSubfolderFilter.Text = string.Empty;
            TxtSubfolderFilter.Focus();
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

        private void LstSubfolders_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateActiveFolderDisplay();
            SaveConfig();
        }

        private void UpdateActiveFolderDisplay()
        {
            if (_selectedCategoryNode == null || string.IsNullOrEmpty(_rootFolder)) return;

            if (_selectedCategoryNode.IsFavorites)
            {
                ShowFavorites();
                return;
            }

            string targetPath = _selectedCategoryNode.FullPath;
            if (LstSubfolders.SelectedItem is SubfolderItem sub && !string.IsNullOrEmpty(sub.FullPath))
                targetPath = sub.FullPath;

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

            string? currentCatPath = _selectedCategoryNode?.IsFavorites == true
                ? "__FAVORITES__"
                : _selectedCategoryNode?.FullPath;
            string? currentSub = (LstSubfolders.SelectedItem as SubfolderItem)?.RelativePath;

            SetLibraryRoot(_rootFolder, currentCatPath, currentSub);
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
            if ((sender as FrameworkElement)?.DataContext is CategoryNode node)
            {
                if (node.IsFavorites || string.IsNullOrEmpty(node.FullPath)) return;
                AddAllToFavorites(node.FullPath);
            }
        }

        private void CtxSubfolderAddFav_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is SubfolderItem sub && !string.IsNullOrEmpty(sub.FullPath))
            {
                AddAllToFavorites(sub.FullPath);
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
            var favNode = _allRootNodes.FirstOrDefault(c => c.IsFavorites);
            if (favNode != null)
            {
                favNode.Count = _favorites.Count(File.Exists);
            }
        }
    }
}