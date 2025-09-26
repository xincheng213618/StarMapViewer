using System;

namespace StarMapViewer.Configuration
{
    public static class AppConfig
    {
        // Default configuration values
        public static string TilesRoot { get; set; } = "tiles";
        public static int MaxZoomLevel { get; set; } = 5;
        public static int CacheCapacity { get; set; } = 1024;
        public static int TileLoadingThrottleFrames { get; set; } = 5;
        public static double ZoomScaleFactor { get; set; } = 1.1;
        public static double MinScaleFactor { get; set; } = 0.01;
        
        /// <summary>
        /// Load configuration from environment variables or config file if available
        /// </summary>
        public static void Initialize()
        {
            // Try to load from environment variables first
            var tilesRoot = Environment.GetEnvironmentVariable("STARMAP_TILES_ROOT");
            if (!string.IsNullOrEmpty(tilesRoot))
            {
                TilesRoot = tilesRoot;
            }
            
            var maxZoom = Environment.GetEnvironmentVariable("STARMAP_MAX_ZOOM");
            if (int.TryParse(maxZoom, out var maxZoomValue))
            {
                MaxZoomLevel = maxZoomValue;
            }
            
            var cacheCapacity = Environment.GetEnvironmentVariable("STARMAP_CACHE_CAPACITY");
            if (int.TryParse(cacheCapacity, out var cacheValue))
            {
                CacheCapacity = cacheValue;
            }
        }
    }
}