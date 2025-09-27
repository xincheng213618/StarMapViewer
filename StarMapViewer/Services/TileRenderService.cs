using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfImage = System.Windows.Controls.Image;

namespace StarMapViewer.Services
{
    public class TileRenderService
    {
        public void PlaceTile(WpfImage img, int tx, int ty, double drawTileW, double drawTileH, System.Windows.Point offset, Window parentWindow)
        {
            var src = PresentationSource.FromVisual(parentWindow);
            double dx = 1.0, dy = 1.0;
            if (src != null)
            {
                dx = src.CompositionTarget.TransformToDevice.M11;
                dy = src.CompositionTarget.TransformToDevice.M22;
            }
            double left = offset.X + tx * drawTileW;
            double top = offset.Y + ty * drawTileH;
            double right = offset.X + (tx + 1) * drawTileW;
            double bottom = offset.Y + (ty + 1) * drawTileH;
            double dLeft = Math.Round(left * dx);
            double dTop = Math.Round(top * dy);
            double dRight = Math.Round(right * dx);
            double dBottom = Math.Round(bottom * dy);
            double snappedLeft = dLeft / dx;
            double snappedTop = dTop / dy;
            double snappedWidth = (dRight - dLeft) / dx;
            double snappedHeight = (dBottom - dTop) / dy;
            Canvas.SetLeft(img, snappedLeft);
            Canvas.SetTop(img, snappedTop);
            img.Width = snappedWidth;
            img.Height = snappedHeight;
        }

        public int UpdateDesiredZoomLevel(double scale, double minScale, int currentZoom, int maxZoomLevel)
        {
            if (scale <= 0 || minScale <= 0) return currentZoom;
            double rel = scale / minScale; // >=1
            double target = Math.Log(rel, 2.0); // 1->0,2->1,4->2...
            int desired = (int)Math.Floor(target + 1e-6);
            if (desired < 0) desired = 0;
            if (desired > maxZoomLevel) desired = maxZoomLevel;
            return desired;
        }

        public int TileCountPerAxis(int zoom) => 1 << zoom; // 2^zoom

        public double WorldTileWidth(int worldWidth, int zoom) => (double)worldWidth / TileCountPerAxis(zoom);

        public double WorldTileHeight(int worldHeight, int zoom) => (double)worldHeight / TileCountPerAxis(zoom);
    }
}