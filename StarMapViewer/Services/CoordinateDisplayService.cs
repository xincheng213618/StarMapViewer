using System;
using System.Collections.Generic;
using System.Linq;

namespace StarMapViewer.Services
{
    public class CoordinateDisplayService
    {
        public string GenerateCoordinateText(
            double canvasWidth, double canvasHeight,
            System.Windows.Point offset, double scale,
            int zoom, int tileCount,
            int worldWidth, int worldHeight,
            TileRenderService renderService)
        {
            if (canvasWidth <= 0 || canvasHeight <= 0) return "";

            double centerX = (canvasWidth / 2.0 - offset.X) / scale;
            double centerY = (canvasHeight / 2.0 - offset.Y) / scale;
            
            int tilesPerAxis = renderService.TileCountPerAxis(zoom);
            double tileW = renderService.WorldTileWidth(worldWidth, zoom);
            double tileH = renderService.WorldTileHeight(worldHeight, zoom);
            
            int tx = (int)Math.Floor(centerX / tileW);
            int ty = (int)Math.Floor(centerY / tileH);
            
            var names = new List<string>();
            for (int dy = 0; dy <= 1; dy++)
            {
                for (int dx = 0; dx <= 1; dx++)
                {
                    int nx = tx + dx;
                    int ny = ty + dy;
                    if (nx < tilesPerAxis && ny < tilesPerAxis)
                    {
                        double left = nx * tileW, right = (nx + 1) * tileW;
                        double top = ny * tileH, bottom = (ny + 1) * tileH;
                        if (centerX >= left && centerX <= right && centerY >= top && centerY <= bottom)
                            names.Add($"{nx}_{ny}");
                    }
                }
            }
            
            if (names.Count == 0) names.Add($"{tx}_{ty}");
            if (names.Count > 4) names = names.Take(4).ToList();
            string tileNames = string.Join(", ", names);
            
            return $"Level: {zoom} | Tiles: {tileCount} | ({centerX:F0}, {centerY:F0}) | {tileNames}";
        }
    }
}