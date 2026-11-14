# Advanced Family Manager for Revit

A high-performance, dark-themed WPF plugin for Autodesk Revit that replaces the native family browser. Built for speed, safety, and visual clarity.

**Developer:** Dr. BELABED  
**Compatibility:** Revit 2024 (.NET Framework 4.8) | Revit 2025–2026 (.NET 8.0)

---

## ✨ Key Features

- **Async Thumbnail Engine:** Extracts `.rfa` previews in the background and caches them in memory. Zero UI freezing, even with thousands of families.
- **Silent On-Demand Upgrade:** Automatically detects outdated `.rfa` files, upgrades them, and caches them locally (`%AppData%`). Your original library folders are **never** modified or corrupted. Revit will only ask to convert a file once.
- **Smart Search & Filtering:** Instantly filter by category, subfolder, or perform a global search across your entire library.
- **Favorites System:** Right-click any family card, category, or subfolder to add it to a dedicated "Favorites" tab.
- **Batch Loading:** Select multiple families using `Ctrl+Click` or `Shift+Click` and load them into the project at once.
- **Dynamic UI:** Drag the "Card Size" slider to instantly scale the family grid. Window size, splitter position, and card size are saved to `config.json`.
- **Live File Watcher:** Automatically detects and refreshes the view when new `.rfa` files are added or deleted from your library folder.
- **Rich Context Menus:** Right-click for "Open Containing Folder", "Copy Path", "Copy Name", and quick-load actions.

---

## 🛠️ Technical Stack

- **Frameworks:** Multi-targeted for `.NET Framework 4.8` and `.NET 8.0-windows`.
- **UI:** Native WPF dark theme (`#1E1E1E` palette) with custom styling.
- **Revit API:** Utilizes `BasicFileInfo`, `EditFamily`, and a local `%AppData%` caching strategy to bypass Revit's native file-locking corruption traps.
- **Storage:** Persists user settings, favorites, and upgraded families in `%AppData%\AdvancedFamilyManager\`.

---
