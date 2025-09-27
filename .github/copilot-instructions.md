# StarMapViewer - Copilot Instructions

## Project Overview

StarMapViewer is a WPF (.NET 8) multi-level tiled map viewer application designed for displaying hierarchical tile-based maps with smooth pan and zoom functionality. The application provides an efficient tile loading system with LRU memory caching and device-independent pixel (DPI) handling.

## Architecture & Key Components

### Core Components
- **MainWindow.xaml/cs**: Main application window containing the Canvas-based tile rendering system
- **Services/**: Contains helper services (currently WorldSizeManager)
- **Tile System**: Hierarchical tile structure from zoom level 0 to MaxZoomLevel

### Tile System Architecture
- **Tile Naming**: `{zoom}/{x}_{y}.jpg` (e.g., `tiles/0/0_0.jpg`)
- **Zoom Levels**: 
  - Level 0: 1×1 tile (base/overview image)
  - Level N: (2^N)×(2^N) tiles
  - Maximum configurable via `MaxZoomLevel` constant (default: 5)
- **Coordinate System**: Top-left origin (0,0), x increases right, y increases down

### Transform System
- `_scale`: Current zoom scale (screen pixels / world pixels)
- `_offset`: Screen translation offset
- `_minScale`: Minimum scale to fit entire image
- Transform mapping: World coordinates → Screen coordinates via scale + offset

### Caching System
- **LRU Cache**: Memory-efficient tile caching (default capacity: 512)
- **Dynamic Loading**: Loads only visible tiles plus one surrounding ring
- **Cleanup**: Automatic tile cleanup every 5 frames to prevent UI lag

## Code Style & Conventions

### Language Usage
- **UI Elements**: Chinese text for user-facing elements (buttons, labels)
- **Code**: English variable names, method names, and comments
- **Documentation**: Mixed Chinese/English (Chinese for user docs, English for technical comments)

### Naming Conventions
- **Private Fields**: Underscore prefix (e.g., `_scale`, `_offset`, `_tilesRoot`)
- **Constants**: PascalCase (e.g., `DefaultTilesRoot`, `MaxZoomLevel`)
- **Methods**: PascalCase following .NET conventions
- **Events**: Standard WPF event handler naming (`Control_Event`)

### Key Constants & Configuration
```csharp
const string DefaultTilesRoot = "C\\Users\\17917\\Desktop\\scgd_general_wpf\\x64\\Debug\\tiles";
const int MaxZoomLevel = 5;
```

## Development Workflow

### Build Requirements
- **Framework**: .NET 8.0 with Windows Desktop support
- **Platform**: Windows only (WPF + WindowsForms dependencies)
- **IDE**: Visual Studio 2022 or dotnet CLI

### Build Commands
```bash
dotnet build          # Build solution
dotnet run            # Run application (F5 in Visual Studio)
```

### Key Implementation Patterns

#### Transform Management
- Use `_transformDirty` flag to minimize recalculations
- Always call coordinate conversion methods when mapping between world/screen space
- DPI-aware rendering using `PlaceTile` with pixel rounding

#### Tile Loading & Management
- Visible tile detection based on current viewport bounds
- Asynchronous tile loading (avoid blocking UI thread)
- Proper disposal of bitmap resources in LRU cache

#### Event Handling
- Mouse events: `MouseWheel` (zoom), `MouseLeftButtonDown/Move/Up` (pan)
- Use capture/release mouse for smooth dragging experience
- Update coordinate display in real-time during interactions

## Testing Approach

Currently no automated tests exist. Manual testing focuses on:
- **Tile Loading**: Verify correct tiles load for different zoom levels
- **Performance**: Smooth pan/zoom without frame drops
- **Memory**: Cache management working correctly (no memory leaks)
- **Edge Cases**: Boundary conditions, extreme zoom levels, empty tile directories

## Common Implementation Details

### Coordinate Calculations
- World bounds calculated from tile count at current zoom level
- Viewport-to-tile mapping accounts for tile boundaries and overlaps
- `UpdateCoordText()` provides real-time coordinate feedback

### Performance Considerations
- Minimize Canvas children manipulation (expensive operations)
- Use `_tilesDirty` flag to batch tile updates
- Implement efficient visible tile detection algorithm
- Consider bitmap scaling modes for quality vs. performance trade-offs

### Error Handling
- Graceful handling of missing tile files
- Path validation for custom tile directories
- DPI scaling edge cases

## UI Structure

### Main Canvas
- **Background**: White
- **Mouse Events**: Wheel (zoom), Left button (pan)
- **Children**: Dynamically added/removed tile Image elements

### Control Elements
- **CoordText**: Top-right coordinate display
- **Button Group**: Top-left (Open Directory, Clear Canvas, Clear Cache)
- Semi-transparent backgrounds for better visibility

## Extension Points

Future enhancements could include:
- **Async tile loading**: Background thread with placeholder images
- **Multi-threaded loading**: Parallel tile processing
- **Tile overlays**: Support for annotations, markers
- **GPU acceleration**: WriteableBitmap/Direct2D/Win2D integration
- **Network sources**: HTTP tile providers, online map services

## Important Notes for Contributors

1. **DPI Handling**: Always consider device pixel ratio when placing tiles
2. **Memory Management**: Dispose bitmaps properly, monitor cache size
3. **Performance**: Profile tile loading and rendering performance regularly
4. **Chinese UI**: Maintain Chinese text for user-facing elements while keeping code in English
5. **Windows Platform**: This is a Windows-only application due to WPF dependencies