using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace StarMapViewer.Services
{
    public class WorldCoordinateService
    {
        private int _worldWidth = 0;
        private int _worldHeight = 0;
        private bool _worldSizeLoaded = false;

        public int WorldWidth => _worldWidth;
        public int WorldHeight => _worldHeight;
        public bool WorldSizeLoaded => _worldSizeLoaded;

        public void EnsureWorldSize(string tilesRoot)
        {
            if (_worldSizeLoaded) return;
            string p = Path.Combine(tilesRoot, "0", "0_0.jpg");
            if (!File.Exists(p)) return;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(p, UriKind.Absolute);
                bmp.EndInit();
                _worldWidth = bmp.PixelWidth;
                _worldHeight = bmp.PixelHeight;
                _worldSizeLoaded = true;
            }
            catch { }
        }

        public (double scale, System.Windows.Point offset) FitToView(double canvasWidth, double canvasHeight, double currentScale, System.Windows.Point currentOffset, bool isFirstFit)
        {
            if (!_worldSizeLoaded || canvasWidth <= 0 || canvasHeight <= 0) 
                return (currentScale, currentOffset);

            double fitScale = Math.Min(canvasWidth / _worldWidth, canvasHeight / _worldHeight);
            double scale = currentScale;
            
            if (isFirstFit)
            {
                scale = fitScale; // 首次强制适配 => zoom 0
            }
            else if (currentScale < fitScale)
            {
                scale = fitScale;
            }

            var offset = new System.Windows.Point(
                (canvasWidth - _worldWidth * scale) / 2.0,
                (canvasHeight - _worldHeight * scale) / 2.0
            );

            return (scale, offset);
        }

        public double GetMinScale(double canvasWidth, double canvasHeight)
        {
            if (!_worldSizeLoaded || canvasWidth <= 0 || canvasHeight <= 0) 
                return 0.01;
            return Math.Min(canvasWidth / _worldWidth, canvasHeight / _worldHeight);
        }

        public void Reset()
        {
            _worldSizeLoaded = false;
            _worldWidth = _worldHeight = 0;
        }
    }
}