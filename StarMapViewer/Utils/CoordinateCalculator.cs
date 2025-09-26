using System;
using System.Collections.Generic;
using System.Windows;

namespace StarMapViewer.Utils
{
    public struct TileRange
    {
        public int X0 { get; set; }
        public int Y0 { get; set; }
        public int X1 { get; set; }
        public int Y1 { get; set; }

        public bool IsValid => X1 >= X0 && Y1 >= Y0;
        
        public bool Equals(TileRange other) =>
            X0 == other.X0 && Y0 == other.Y0 && X1 == other.X1 && Y1 == other.Y1;
    }

    public struct WorldCoordinate
    {
        public double X { get; set; }
        public double Y { get; set; }
        
        public WorldCoordinate(double x, double y)
        {
            X = x;
            Y = y;
        }
    }

    public static class CoordinateCalculator
    {
        /// <summary>
        /// Calculate the number of tiles per axis for a given zoom level
        /// </summary>
        public static int TileCountPerAxis(int zoom) => 1 << zoom; // 2^zoom

        /// <summary>
        /// Calculate world tile dimensions for a given zoom level
        /// </summary>
        public static (double width, double height) GetWorldTileDimensions(int zoom, int worldWidth, int worldHeight)
        {
            var tilesPerAxis = TileCountPerAxis(zoom);
            return ((double)worldWidth / tilesPerAxis, (double)worldHeight / tilesPerAxis);
        }

        /// <summary>
        /// Convert screen coordinates to world coordinates
        /// </summary>
        public static WorldCoordinate ScreenToWorld(Point screenPoint, Point offset, double scale)
        {
            return new WorldCoordinate(
                (screenPoint.X - offset.X) / scale,
                (screenPoint.Y - offset.Y) / scale
            );
        }

        /// <summary>
        /// Convert world coordinates to screen coordinates
        /// </summary>
        public static Point WorldToScreen(WorldCoordinate worldCoord, Point offset, double scale)
        {
            return new Point(
                worldCoord.X * scale + offset.X,
                worldCoord.Y * scale + offset.Y
            );
        }

        /// <summary>
        /// Calculate visible tile range based on current view parameters
        /// </summary>
        public static TileRange CalculateVisibleTileRange(
            double canvasWidth, double canvasHeight,
            Point offset, double scale,
            int zoom, int worldWidth, int worldHeight)
        {
            if (canvasWidth <= 0 || canvasHeight <= 0)
                return new TileRange();

            var (tileW, tileH) = GetWorldTileDimensions(zoom, worldWidth, worldHeight);
            var invScale = 1.0 / scale;
            
            var worldLeft = -offset.X * invScale;
            var worldTop = -offset.Y * invScale;
            var worldRight = worldLeft + canvasWidth * invScale;
            var worldBottom = worldTop + canvasHeight * invScale;

            var tilesPerAxis = TileCountPerAxis(zoom);
            var maxIndex = tilesPerAxis - 1;

            var tx0 = Math.Max(0, (int)Math.Floor(worldLeft / tileW) - 1);
            var ty0 = Math.Max(0, (int)Math.Floor(worldTop / tileH) - 1);
            var tx1 = Math.Min(maxIndex, (int)Math.Floor(worldRight / tileW) + 1);
            var ty1 = Math.Min(maxIndex, (int)Math.Floor(worldBottom / tileH) + 1);

            return new TileRange { X0 = tx0, Y0 = ty0, X1 = tx1, Y1 = ty1 };
        }

        /// <summary>
        /// Calculate the center tile names for coordinate display (optimized version)
        /// </summary>
        public static string GetCenterTileNames(
            double canvasWidth, double canvasHeight,
            Point offset, double scale,
            int zoom, int worldWidth, int worldHeight)
        {
            var center = ScreenToWorld(
                new Point(canvasWidth / 2.0, canvasHeight / 2.0),
                offset, scale
            );

            var (tileW, tileH) = GetWorldTileDimensions(zoom, worldWidth, worldHeight);
            var tilesPerAxis = TileCountPerAxis(zoom);
            
            var tx = (int)Math.Floor(center.X / tileW);
            var ty = (int)Math.Floor(center.Y / tileH);

            // Optimized: only check the actual center tile instead of 2x2 grid
            if (tx >= 0 && tx < tilesPerAxis && ty >= 0 && ty < tilesPerAxis)
            {
                return $"{tx}_{ty}";
            }

            return $"{Math.Max(0, Math.Min(tilesPerAxis - 1, tx))}_{Math.Max(0, Math.Min(tilesPerAxis - 1, ty))}";
        }

        /// <summary>
        /// Calculate the desired zoom level based on scale
        /// </summary>
        public static int CalculateDesiredZoomLevel(double currentScale, double minScale, int maxZoomLevel)
        {
            if (currentScale <= 0 || minScale <= 0)
                return 0;

            var rel = currentScale / minScale; // >=1
            var target = Math.Log(rel, 2.0); // 1->0, 2->1, 4->2...
            var desired = (int)Math.Floor(target + 1e-6);
            
            return Math.Max(0, Math.Min(maxZoomLevel, desired));
        }
    }
}