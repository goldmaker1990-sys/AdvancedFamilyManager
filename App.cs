using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace AdvancedFamilyManager
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication app)
        {
            string tabName = "BIM Hub";
            try
            {
                app.CreateRibbonTab(tabName);
            }
            catch
            {
                // Tab already exists
            }

            RibbonPanel panel = app.CreateRibbonPanel(tabName, "Management");

            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            PushButtonData buttonData = new PushButtonData(
                "OpenFamilyManagerButton",
                "Family\nManager",
                assemblyPath,
                "AdvancedFamilyManager.OpenFamilyManagerCommand"
            );

            PushButton? pushButton = panel.AddItem(buttonData) as PushButton;
            if (pushButton != null)
            {
                pushButton.ToolTip = "Open Advanced Family Manager & Browser";
                pushButton.LongDescription = "Browse, filter, search, and load Revit families visually with nested folder support.";

                pushButton.Image = LoadEmbeddedPng("icon16.png", 16);
                pushButton.LargeImage = LoadEmbeddedPng("icon32.png", 32);
            }

            // Extract Revit File Locations -> Places library paths using ControlledApplication and cache them locally
            try
            {
                var controlledApp = app.ControlledApplication;
                var libraryPathsMap = controlledApp.GetLibraryPaths(); // Pulls from Options -> File Locations -> Places
                var discoveredPaths = new List<string>();

                foreach (var value in libraryPathsMap.Values)
                {
                    if (value is string pathStr && Directory.Exists(pathStr) && !discoveredPaths.Contains(pathStr))
                    {
                        discoveredPaths.Add(pathStr);
                    }
                }

                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string configDir = Path.Combine(appData, "AdvancedFamilyManager");
                Directory.CreateDirectory(configDir);
                File.WriteAllLines(Path.Combine(configDir, "revit_places.cache"), discoveredPaths);
            }
            catch
            {
                // Fallback gracefully if API context restricts reading places
            }

            return Result.Succeeded;
        }

        private static ImageSource? LoadEmbeddedPng(string fileName, int targetSize)
        {
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                string? resourceName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(r => r.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));

                if (resourceName == null) return null;

                using Stream? resStream = assembly.GetManifestResourceStream(resourceName);
                if (resStream == null) return null;

                using MemoryStream ms = new MemoryStream();
                resStream.CopyTo(ms);
                ms.Position = 0;

                PngBitmapDecoder decoder = new PngBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                BitmapFrame frame = decoder.Frames[0];

                double scaleX = (double)targetSize / frame.PixelWidth;
                double scaleY = (double)targetSize / frame.PixelHeight;

                TransformedBitmap resized = new TransformedBitmap(frame, new ScaleTransform(scaleX, scaleY));

                PngBitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(resized));

                using MemoryStream finalStream = new MemoryStream();
                encoder.Save(finalStream);
                finalStream.Position = 0;

                BitmapImage result = new BitmapImage();
                result.BeginInit();
                result.StreamSource = finalStream;
                result.CacheOption = BitmapCacheOption.OnLoad;
                result.EndInit();
                result.Freeze();

                return result;
            }
            catch
            {
                return null;
            }
        }

        public Result OnShutdown(UIControlledApplication app)
        {
            return Result.Succeeded;
        }
    }
}