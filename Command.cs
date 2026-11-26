using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace AdvancedFamilyManager
{
    public class FamilyLoadOptions : IFamilyLoadOptions
    {
        private readonly bool _overwriteParameterValues;

        public FamilyLoadOptions(bool overwriteParameterValues)
        {
            _overwriteParameterValues = overwriteParameterValues;
        }

        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        {
            overwriteParameterValues = _overwriteParameterValues;
            return true;
        }

        public bool OnSharedFamilyFound(Family sharedFamily, bool inUse, out FamilySource source, out bool overwriteParameterValues)
        {
            source = FamilySource.Family;
            overwriteParameterValues = _overwriteParameterValues;
            return true;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class OpenFamilyManagerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null)
            {
                TaskDialog.Show("Family Manager", "Please open a project before launching the Family Manager.");
                return Result.Cancelled;
            }

            Document doc = uidoc.Document;
            int currentRevitYear = int.Parse(commandData.Application.Application.VersionNumber);
            bool isLightTheme = IsRevitLightTheme();

            var window = new ManagerWindow(isLightTheme);
            bool? result = window.ShowDialog();

            if (result != true || window.SelectedFamilyPaths.Count == 0)
                return Result.Succeeded;

            var options = new FamilyLoadOptions(window.OverwriteParameterValues);
            var loadedFamilies = new List<Family>();
            var failures = new List<string>();

            // Setup Cache Folder specific to the Revit Version
            string cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AdvancedFamilyManager",
                "UpgradedFamilies",
                currentRevitYear.ToString());
            Directory.CreateDirectory(cacheDir);

            foreach (var path in window.SelectedFamilyPaths)
            {
                string? tempLoadPath = null;
                try
                {
                    bool isOldVersion = IsFamilyOld(path, currentRevitYear);
                    string cachePath = GetCachePath(path, cacheDir);
                    bool loadingFromCache = false;
                    string pathToLoad = path;

                    // 1. Determine what file to load (Original or Cache)
                    if (isOldVersion && File.Exists(cachePath))
                    {
                        pathToLoad = cachePath;
                        loadingFromCache = true;
                    }

                    // 2. Create a temporary copy to load (Bypasses all Revit file locks)
                    tempLoadPath = CreateTempCopy(pathToLoad);
                    if (tempLoadPath == null)
                    {
                        failures.Add($"{Path.GetFileName(path)}: File is locked or inaccessible.");
                        continue;
                    }

                    // 3. Load into project
                    Family loaded = null!;
                    bool ok = false;
                    using (var trans = new Transaction(doc, "Load Family from Manager"))
                    {
                        trans.Start();
                        ok = doc.LoadFamily(tempLoadPath, options, out loaded);
                        trans.Commit();
                    }

                    if (ok && loaded != null)
                    {
                        loadedFamilies.Add(loaded);

                        // 4. If it was an old version and we loaded the original, create the cache
                        if (isOldVersion && !loadingFromCache)
                        {
                            try
                            {
                                Document editDoc = doc.EditFamily(loaded);
                                if (editDoc != null)
                                {
                                    SaveAsOptions saveOpts = new SaveAsOptions { OverwriteExistingFile = true };
                                    editDoc.SaveAs(cachePath, saveOpts);
                                    editDoc.Close(false);
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Failed to save to cache: {ex.Message}");
                            }
                        }
                    }
                    else
                    {
                        failures.Add(Path.GetFileName(path));
                    }
                }
                catch (Exception ex)
                {
                    failures.Add($"{Path.GetFileName(path)}: {ex.Message}");
                }
                finally
                {
                    // 5. Clean up the temporary file
                    if (tempLoadPath != null && File.Exists(tempLoadPath))
                    {
                        try { File.Delete(tempLoadPath); } catch { }
                    }
                }
            }

            if (failures.Count > 0)
            {
                string msg = "Some families could not be loaded:\n\n" + string.Join("\n", failures);
                TaskDialog.Show("Family Load Results", msg);
            }

            if (window.PlaceAfterLoad && loadedFamilies.Count > 0)
            {
                var first = loadedFamilies[0];
                var symbolIds = first.GetFamilySymbolIds();
                if (symbolIds.Count > 0)
                {
                    if (doc.GetElement(symbolIds.First()) is FamilySymbol symbol)
                    {
                        try
                        {
                            uidoc.PromptForFamilyInstancePlacement(symbol);
                        }
                        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                        {
                            // User pressed ESC — expected
                        }
                        catch (Exception ex)
                        {
                            TaskDialog.Show("Placement Failed", $"Could not place family: {ex.Message}");
                        }
                    }
                }
            }

            return Result.Succeeded;
        }

        private static bool IsRevitLightTheme()
        {
            try
            {
                Assembly uiAsm = typeof(UIApplication).Assembly;
                Type? themeManagerType = uiAsm.GetType("Autodesk.Revit.UI.UIThemeManager");
                if (themeManagerType != null)
                {
                    PropertyInfo? prop = themeManagerType.GetProperty(
                        "CurrentTheme",
                        BindingFlags.Public | BindingFlags.Static);
                    string? currentTheme = prop?.GetValue(null)?.ToString();
                    if (!string.IsNullOrEmpty(currentTheme))
                    {
                        return currentTheme!.IndexOf("Light", StringComparison.OrdinalIgnoreCase) >= 0;
                    }
                }
            }
            catch
            {
                // Fallback to dark theme if unsupported or unavailable
            }

            return false;
        }

        /// <summary>
        /// Copies a file to a temporary location, retrying if the file is briefly locked by the OS.
        /// </summary>
        private static string? CreateTempCopy(string sourcePath)
        {
            string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".rfa");
            int retries = 10;
            while (retries > 0)
            {
                try
                {
                    File.Copy(sourcePath, tempPath, true);
                    return tempPath;
                }
                catch (IOException)
                {
                    retries--;
                    System.Threading.Thread.Sleep(100);
                }
                catch (UnauthorizedAccessException)
                {
                    retries--;
                    System.Threading.Thread.Sleep(100);
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }

        private static string GetCachePath(string originalPath, string cacheDir)
        {
            try
            {
                FileInfo fi = new FileInfo(originalPath);
                string safeName = string.Join(
                    "_",
                    Path.GetFileNameWithoutExtension(originalPath).Split(Path.GetInvalidFileNameChars()));
                // Include LastWriteTime so if the user overwrites the original, a new cache is generated.
                return Path.Combine(cacheDir, $"{safeName}_{fi.Length}_{fi.LastWriteTimeUtc.Ticks}.rfa");
            }
            catch
            {
                return Path.Combine(cacheDir, Guid.NewGuid().ToString() + ".rfa");
            }
        }

        private static bool IsFamilyOld(string filePath, int currentRevitYear)
        {
            try
            {
                BasicFileInfo info = BasicFileInfo.Extract(filePath);
                if (info == null || string.IsNullOrEmpty(info.Format))
                    return false;

                if (int.TryParse(info.Format, out int fileYear))
                {
                    return fileYear < currentRevitYear;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}