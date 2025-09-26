using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StarMapViewer.Services;
using StarMapViewer.Utils;

namespace StarMapViewer.Services
{
    public interface ITileRenderer : IDisposable
    {
        Task LoadVisibleTilesAsync(Canvas canvas, TileRange visibleRange, int zoom, 
            double drawTileW, double drawTileH, Point offset);
        void ClearActiveTiles(Canvas canvas);
        void UpdateTilePositions(Canvas canvas, double drawTileW, double drawTileH, Point offset);
        int ActiveTileCount { get; }
    }

    public class TileRenderer : ITileRenderer
    {
        private readonly ITileCacheManager _cacheManager;
        private readonly Dictionary<(int x, int y), Image> _activeImages;
        private readonly object _activeTilesLock;
        private bool _disposed;

        public int ActiveTileCount => _activeImages.Count;

        public TileRenderer(ITileCacheManager cacheManager)
        {
            _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
            _activeImages = new Dictionary<(int, int), Image>();
            _activeTilesLock = new object();
        }

        public async Task LoadVisibleTilesAsync(Canvas canvas, TileRange visibleRange, int zoom,
            double drawTileW, double drawTileH, Point offset)
        {
            if (!visibleRange.IsValid)
                return;

            var needed = new HashSet<(int, int)>();
            var loadTasks = new List<Task>();

            // First pass: identify needed tiles and start loading
            for (int ty = visibleRange.Y0; ty <= visibleRange.Y1; ty++)
            {
                for (int tx = visibleRange.X0; tx <= visibleRange.X1; tx++)
                {
                    needed.Add((tx, ty));
                    
                    lock (_activeTilesLock)
                    {
                        if (!_activeImages.ContainsKey((tx, ty)))
                        {
                            var img = CreateTileImage();
                            _activeImages[(tx, ty)] = img;
                            
                            // Add to canvas on UI thread
                            Application.Current.Dispatcher.BeginInvoke(() =>
                            {
                                canvas.Children.Add(img);
                            });
                        }
                    }

                    // Start loading tile asynchronously
                    var loadTask = LoadAndDisplayTile(tx, ty, zoom, drawTileW, drawTileH, offset);
                    loadTasks.Add(loadTask);
                }
            }

            // Wait for all tiles to load
            await Task.WhenAll(loadTasks);

            // Remove unused tiles
            await RemoveUnusedTiles(canvas, needed);
        }

        private async Task LoadAndDisplayTile(int tx, int ty, int zoom, 
            double drawTileW, double drawTileH, Point offset)
        {
            try
            {
                var bmp = await _cacheManager.LoadTileAsync(zoom, tx, ty);
                
                await Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    lock (_activeTilesLock)
                    {
                        if (_activeImages.TryGetValue((tx, ty), out var img))
                        {
                            if (bmp == null)
                            {
                                img.Visibility = Visibility.Hidden;
                            }
                            else
                            {
                                if (!ReferenceEquals(img.Source, bmp))
                                    img.Source = bmp;
                                
                                PlaceTile(img, tx, ty, drawTileW, drawTileH, offset);
                                img.Visibility = Visibility.Visible;
                            }
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading tile ({tx}, {ty}): {ex.Message}");
            }
        }

        private async Task RemoveUnusedTiles(Canvas canvas, HashSet<(int, int)> needed)
        {
            var toRemove = new List<(int, int)>();
            
            lock (_activeTilesLock)
            {
                foreach (var kv in _activeImages)
                {
                    if (!needed.Contains(kv.Key))
                    {
                        toRemove.Add(kv.Key);
                    }
                }
            }

            if (toRemove.Count > 0)
            {
                await Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    lock (_activeTilesLock)
                    {
                        foreach (var key in toRemove)
                        {
                            if (_activeImages.TryGetValue(key, out var img))
                            {
                                img.Source = null; // Release reference
                                canvas.Children.Remove(img);
                                _activeImages.Remove(key);
                            }
                        }
                    }
                });
            }
        }

        public void UpdateTilePositions(Canvas canvas, double drawTileW, double drawTileH, Point offset)
        {
            lock (_activeTilesLock)
            {
                foreach (var kv in _activeImages)
                {
                    var (tx, ty) = kv.Key;
                    PlaceTile(kv.Value, tx, ty, drawTileW, drawTileH, offset);
                }
            }
        }

        public void ClearActiveTiles(Canvas canvas)
        {
            lock (_activeTilesLock)
            {
                foreach (var kv in _activeImages)
                {
                    kv.Value.Source = null; // Release reference
                    canvas.Children.Remove(kv.Value);
                }
                _activeImages.Clear();
            }
        }

        private static Image CreateTileImage()
        {
            var img = new Image
            {
                Visibility = Visibility.Hidden,
                SnapsToDevicePixels = true,
                UseLayoutRounding = true,
                Stretch = Stretch.Fill
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            return img;
        }

        private static void PlaceTile(Image img, int tx, int ty, double drawTileW, double drawTileH, Point offset)
        {
            // Get DPI scaling factors
            var src = PresentationSource.FromVisual(Application.Current.MainWindow);
            double dx = 1.0, dy = 1.0;
            if (src != null)
            {
                dx = src.CompositionTarget.TransformToDevice.M11;
                dy = src.CompositionTarget.TransformToDevice.M22;
            }

            var left = offset.X + tx * drawTileW;
            var top = offset.Y + ty * drawTileH;
            var right = offset.X + (tx + 1) * drawTileW;
            var bottom = offset.Y + (ty + 1) * drawTileH;

            // Snap to device pixels for crisp rendering
            var dLeft = Math.Round(left * dx);
            var dTop = Math.Round(top * dy);
            var dRight = Math.Round(right * dx);
            var dBottom = Math.Round(bottom * dy);

            var snappedLeft = dLeft / dx;
            var snappedTop = dTop / dy;
            var snappedWidth = (dRight - dLeft) / dx;
            var snappedHeight = (dBottom - dTop) / dy;

            Canvas.SetLeft(img, snappedLeft);
            Canvas.SetTop(img, snappedTop);
            img.Width = snappedWidth;
            img.Height = snappedHeight;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            lock (_activeTilesLock)
            {
                _activeImages.Clear();
            }
            
            _disposed = true;
        }
    }
}