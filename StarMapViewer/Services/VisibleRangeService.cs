using System;

namespace StarMapViewer.Services
{
    public class VisibleRangeService
    {
        public bool ComputeVisibleTileRange(
            double canvasWidth, double canvasHeight, 
            double scale, System.Windows.Point offset,
            int worldWidth, int worldHeight, int zoom,
            out int tx0, out int ty0, out int tx1, out int ty1)
        {
            tx0 = ty0 = tx1 = ty1 = 0;
            if (worldWidth <= 0 || worldHeight <= 0) return false;
            if (canvasWidth <= 0 || canvasHeight <= 0) return false;

            int tilesPerAxis = 1 << zoom; // 2^zoom
            double tileW = (double)worldWidth / tilesPerAxis;
            double tileH = (double)worldHeight / tilesPerAxis;
            double invScale = 1.0 / scale;
            double worldLeft = (-offset.X) * invScale;
            double worldTop = (-offset.Y) * invScale;
            double worldRight = worldLeft + canvasWidth * invScale;
            double worldBottom = worldTop + canvasHeight * invScale;

            int maxIndex = tilesPerAxis - 1;

            tx0 = (int)Math.Floor(worldLeft / tileW) - 1; if (tx0 < 0) tx0 = 0;
            ty0 = (int)Math.Floor(worldTop / tileH) - 1; if (ty0 < 0) ty0 = 0;
            tx1 = (int)Math.Floor(worldRight / tileW) + 1; if (tx1 > maxIndex) tx1 = maxIndex;
            ty1 = (int)Math.Floor(worldBottom / tileH) + 1; if (ty1 > maxIndex) ty1 = maxIndex;
            return true;
        }

        public bool HasRangeChanged(int zoom, int tx0, int ty0, int tx1, int ty1, 
            int lastZoom, int lastTx0, int lastTy0, int lastTx1, int lastTy1)
        {
            return lastZoom != zoom || tx0 != lastTx0 || ty0 != lastTy0 || tx1 != lastTx1 || ty1 != lastTy1;
        }
    }
}