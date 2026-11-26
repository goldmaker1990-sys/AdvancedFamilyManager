# Advanced Family Manager (Autodesk Revit)

An add-in for Autodesk Revit designed to browse, filter, search, preview, and load `.rfa` family files into Revit projects. It targets both `.NET Framework 4.8` (`net48`) for Revit 2019–2024 and `.NET 8.0 Windows` (`net8.0-windows`) for Revit 2025+.

## Features

* **Expandable Category Tree & Real-Time Filter**: Browse family folders in a hierarchical `TreeView` with live text filtering while keeping **Favorites** accessible at the top, and add entire category or subfolder trees to favorites from the context menu.
* **Subfolder List & Real-Time Filter**: View subfolders for the selected category and filter them dynamically as you type.
* **Visual Family Cards & Thumbnails**: View `.rfa` families in a resizable wrap grid with asynchronous shell thumbnail loading, multi-selection, double-click/Enter load-and-place support, and quick context menu actions (Load Into Project, Load & Place, Open Containing Folder, Copy Full Path, Copy Name, Toggle Favorite).
* **Search & Global Search**: Filter families by name within the active folder or search across the entire library directory.
* **Smart Library Path Selection & Drill-Down**: Pick from cached Revit library places, default Autodesk library folders, or custom paths, automatically stepping into a single child subfolder when a root folder has no direct `.rfa` files and only one subdirectory.
* **Revit Theme Awareness**: Adapts window and control colors to match Revit's light or dark theme.
* **Version Upgrade Caching & Lock Bypass**: Copies `.rfa` files to a temporary path before loading to avoid file lock conflicts, caches upgraded older-version families per Revit year under `%AppData%\AdvancedFamilyManager\UpgradedFamilies\<Year>`, and honors the **Overwrite params on load** option.


* **Live File System Watching & Config Persistence**: Watches library changes with debounced refresh and saves window size/position, library path, favorites, and settings to `%AppData%\AdvancedFamilyManager\config.json`.



## Project Structure

Here are the main files in the project:

* `AdvancedFamilyManager.csproj`: Multi-targeted WPF + Windows Forms class library (`net48` and `net8.0-windows`, `x64`) referencing `Newtonsoft.Json` and `Nice3point.Revit.Api.RevitAPI` / `RevitAPIUI`


* `App.cs`: `IExternalApplication` entry point creating the **BIM Hub → Management → Family Manager** ribbon button, loading embedded `icon16.png` / `icon32.png` resources, and caching Revit library places


* `Command.cs`: `IExternalCommand` (`OpenFamilyManagerCommand`) handling theme detection, temporary file copying, family loading (`IFamilyLoadOptions`), upgraded family caching, and placement (`PromptForFamilyInstancePlacement`)


* `ManagerWindow.xaml` & `ManagerWindow.xaml.cs`: WPF browser UI, category tree, subfolder filter, thumbnail grid, file watcher, and JSON configuration persistence


* `FamilyItem.cs`: Observable model representing a `.rfa` family card (`Name`, `FullPath`, `Category`, `SubCategory`, `Thumbnail`, `IsFavorite`)


* `ThumbnailHelper.cs`: Windows Shell `IShellItemImageFactory` thumbnail extractor with timestamped caching


* `AdvancedFamilyManager2024.addin` & `AdvancedFamilyManager2025+.addin`: Revit add-in manifest templates



## Building

1. Ensure the required SDKs/targeting packs are installed on Windows:
* **.NET 8.0 SDK** for `net8.0-windows` (Revit 2025+)


* **.NET Framework 4.8 Developer Pack** (or Visual Studio / MSBuild) for `net48` (Revit 2019–2024)




2. Run a build from the project directory:
```bat
dotnet build -c Release

```


Or target a single framework:
```bat
dotnet build -f net48 -c Release
dotnet build -f net8.0-windows -c Release

```


3. Compiled outputs are written to:
* `bin\Release\net48\AdvancedFamilyManager.dll`


* `bin\Release\net8.0-windows\AdvancedFamilyManager.dll`





## Installation in Revit

YOU CAN USE THE BUILD INSTALLER (.EXE) OR

1. Update the `<Assembly>` path inside `AdvancedFamilyManager2024.addin` and `AdvancedFamilyManager2025+.addin` to match your actual build output path.


2. Copy the `.addin` files to the appropriate Revit Addins folder for your installed versions (for example, `%AppData%\Autodesk\Revit\Addins\2024\` or `%AppData%\Autodesk\Revit\Addins\2025\`).


3. Start Revit and open a project model to launch **Family Manager** from the **BIM Hub** tab.



## Author

Developed by **Dr. BELABED**.
