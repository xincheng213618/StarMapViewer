using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Collections.Generic;
using System.IO;

namespace StarMapViewer
{
    public partial class MainWindow : Window
    {
        const string TilesRoot = "C:\\Users\\17917\\Desktop\\scgd_general_wpf\\x64\\Debug\\tiles"; // tiles/{zoom}/{x}_{y}.jpg
        const int MaxZoomLevel = 5;   // 瓦片最高级别(数据上限)

        double _scale = 1.0;          // 当前缩放(屏幕像素/世界像素)
        double _minScale = 0.01;      // 适配整图最小缩放
        Point _offset = new(0, 0);    // 屏幕平移
        bool _transformDirty = true;  // 仅坐标或缩放变化

        int _zoom = 0;                // 当前瓦片级别 (0 = 只有一张整图)
        int _worldWidth = 0;          // 0级整图宽
        int _worldHeight = 0;         // 0级整图高
        bool _worldSizeLoaded = false;

        readonly Dictionary<(int x, int y), Image> _activeImages = new();
        readonly Dictionary<string, BitmapImage> _bmpCache = new();
        readonly LinkedList<string> _lru = new();
        const int CacheCapacity = 128;

        bool _fitPending = true;      // 需要执行自适应
        bool _firstFit = true;        // 首次适配标记

        int _lastZoom = -1;
        int _lastTx0, _lastTy0, _lastTx1, _lastTy1; // 上次可视瓦片范围
        bool _tilesDirty = true;      // 需要刷新瓦片集合
        bool _forceImmediateTileLoad = false; // 缩放级别切换时立即加载以避免白屏

        int _frameCounter = 0;        // 渲染帧计数

        public MainWindow()
        {
            InitializeComponent();
            CompositionTarget.Rendering += OnRenderFrame;
            MainCanvas.SizeChanged += (_, __) => { _fitPending = true; MarkTilesDirty(); };
            Loaded += (_, __) => { _fitPending = true; MarkTilesDirty(); };
        }

        #region Helpers
        int TileCountPerAxis(int zoom) => 1 << zoom; // 2^zoom
        double WorldTileWidth(int zoom) => (double)_worldWidth / TileCountPerAxis(zoom);
        double WorldTileHeight(int zoom) => (double)_worldHeight / TileCountPerAxis(zoom);

        void MarkTilesDirty() { _tilesDirty = true; _transformDirty = true; }
        void MarkTransformDirtyOnly() => _transformDirty = true;

        void PlaceTile(Image img, int tx, int ty, double drawTileW, double drawTileH)
        {
            var src = PresentationSource.FromVisual(this);
            double dx = 1.0, dy = 1.0;
            if (src != null)
            {
                dx = src.CompositionTarget.TransformToDevice.M11;
                dy = src.CompositionTarget.TransformToDevice.M22;
            }
            double left = _offset.X + tx * drawTileW;
            double top = _offset.Y + ty * drawTileH;
            double right = _offset.X + (tx + 1) * drawTileW;
            double bottom = _offset.Y + (ty + 1) * drawTileH;
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
        #endregion

        #region Zoom selection
        // 根据当前缩放倍率选取瓦片级别：scale 相对 fitScale 的 2 对数（>=1 时才进入更高层级）
        void UpdateDesiredZoomLevel()
        {
            if (_scale <= 0 || _minScale <= 0) return;
            double rel = _scale / _minScale; // >=1
            double target = Math.Log(rel, 2.0); // 1->0,2->1,4->2...
            int desired = (int)Math.Floor(target + 1e-6);
            if (desired < 0) desired = 0;
            if (desired > MaxZoomLevel) desired = MaxZoomLevel;
            if (desired != _zoom)
            {
                _zoom = desired;
                // 不立即清空画布，先保留旧级别瓦片直到新级别加载 (减少闪白)
                // 但为防止索引冲突仍需清空，随后立即加载新瓦片（强制取消节流）
                ClearAllActiveTiles();
                _forceImmediateTileLoad = true;
                MarkTilesDirty();
            }
        }
        #endregion

        #region Cache
        BitmapImage? LoadTile(int zoom, int x, int y)
        {
            string path = Path.Combine(TilesRoot, zoom.ToString(), $"{x}_{y}.jpg");
            if (!File.Exists(path)) return null;
            if (_bmpCache.TryGetValue(path, out var cached)) { Promote(path); return cached; }
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                _bmpCache[path] = bmp;
                _lru.AddFirst(path);
                Evict();
                return bmp;
            }
            catch { return null; }
        }
        void Promote(string path)
        {
            var node = _lru.Find(path);
            if (node != null && node != _lru.First) { _lru.Remove(node); _lru.AddFirst(node); }
        }
        void Evict()
        {
            while (_bmpCache.Count > CacheCapacity && _lru.Last != null)
            { var victim = _lru.Last.Value; _lru.RemoveLast(); _bmpCache.Remove(victim); }
        }
        #endregion

        #region Active Tiles
        void ClearAllActiveTiles()
        {
            foreach (var kv in _activeImages) MainCanvas.Children.Remove(kv.Value);
            _activeImages.Clear();
        }
        #endregion

        #region World size & Fit
        void EnsureWorldSize()
        {
            if (_worldSizeLoaded) return;
            string p = Path.Combine(TilesRoot, "0", "0_0.jpg");
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
                _fitPending = true;
                MarkTilesDirty();
            }
            catch { }
        }

        void FitToViewIfNeeded()
        {
            if (!_worldSizeLoaded || !_fitPending) return;
            if (MainCanvas.ActualWidth <= 0 || MainCanvas.ActualHeight <= 0) return;
            double fitScale = Math.Min(MainCanvas.ActualWidth / _worldWidth, MainCanvas.ActualHeight / _worldHeight);
            _minScale = fitScale;
            if (_firstFit)
            {
                _scale = fitScale; // 首次强制适配 => zoom 0
                _firstFit = false;
            }
            else if (_scale < _minScale)
            {
                _scale = _minScale;
            }
            _offset.X = (MainCanvas.ActualWidth - _worldWidth * _scale) / 2.0;
            _offset.Y = (MainCanvas.ActualHeight - _worldHeight * _scale) / 2.0;
            _fitPending = false;
            MarkTransformDirtyOnly();
        }
        #endregion

        #region Visible range computation
        bool ComputeVisibleTileRange(out int tx0, out int ty0, out int tx1, out int ty1)
        {
            tx0 = ty0 = tx1 = ty1 = 0;
            if (!_worldSizeLoaded) return false;
            if (MainCanvas.ActualWidth <= 0 || MainCanvas.ActualHeight <= 0) return false;

            double tileW = WorldTileWidth(_zoom);
            double tileH = WorldTileHeight(_zoom);
            double invScale = 1.0 / _scale;
            double worldLeft = (-_offset.X) * invScale;
            double worldTop = (-_offset.Y) * invScale;
            double worldRight = worldLeft + MainCanvas.ActualWidth * invScale;
            double worldBottom = worldTop + MainCanvas.ActualHeight * invScale;

            int tilesPerAxis = TileCountPerAxis(_zoom);
            int maxIndex = tilesPerAxis - 1;

            tx0 = (int)Math.Floor(worldLeft / tileW) - 1; if (tx0 < 0) tx0 = 0;
            ty0 = (int)Math.Floor(worldTop / tileH) - 1; if (ty0 < 0) ty0 = 0;
            tx1 = (int)Math.Floor(worldRight / tileW) + 1; if (tx1 > maxIndex) tx1 = maxIndex;
            ty1 = (int)Math.Floor(worldBottom / tileH) + 1; if (ty1 > maxIndex) ty1 = maxIndex;
            return true;
        }

        void CheckIfRangeChanged()
        {
            if (!ComputeVisibleTileRange(out int tx0, out int ty0, out int tx1, out int ty1)) return;
            if (_lastZoom != _zoom || tx0 != _lastTx0 || ty0 != _lastTy0 || tx1 != _lastTx1 || ty1 != _lastTy1)
            {
                _lastZoom = _zoom; _lastTx0 = tx0; _lastTy0 = ty0; _lastTx1 = tx1; _lastTy1 = ty1;
                MarkTilesDirty();
            }
        }
        #endregion

        void UpdateCoordText()
        {
            var coordText = this.FindName("CoordText") as TextBlock;
            if (coordText == null) return;
            if (!_worldSizeLoaded || MainCanvas.ActualWidth <= 0 || MainCanvas.ActualHeight <= 0) { coordText.Text = ""; return; }
            double centerX = (MainCanvas.ActualWidth / 2.0 - _offset.X) / _scale;
            double centerY = (MainCanvas.ActualHeight / 2.0 - _offset.Y) / _scale;
            int tileCount = _activeImages.Count;
            int tilesPerAxis = TileCountPerAxis(_zoom);
            double tileW = WorldTileWidth(_zoom);
            double tileH = WorldTileHeight(_zoom);
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
            if (names.Count > 4) names = names.GetRange(0, 4);
            string tileNames = string.Join(", ", names);
            coordText.Text = $"Level: {_zoom} | Tiles: {tileCount} | ({centerX:F0}, {centerY:F0}) | {tileNames}";
        }

        void OnRenderFrame(object? sender, EventArgs e)
        {
            _frameCounter++;
            EnsureWorldSize();
            FitToViewIfNeeded();
            if (!_worldSizeLoaded) return;
            UpdateDesiredZoomLevel();
            CheckIfRangeChanged();
            UpdateCoordText();

            double tileWorldW = WorldTileWidth(_zoom);
            double tileWorldH = WorldTileHeight(_zoom);
            double drawTileW = tileWorldW * _scale;
            double drawTileH = tileWorldH * _scale;

            if (!_tilesDirty && _transformDirty)
            {
                foreach (var kv in _activeImages)
                {
                    var (tx, ty) = kv.Key;
                    PlaceTile(kv.Value, tx, ty, drawTileW, drawTileH);
                }
                _transformDirty = false;
                return;
            }

            if (!_tilesDirty) return;
            // 若是缩放级别刚变更，强制本帧加载；否则每 5 帧节流一次
            if (!_forceImmediateTileLoad && _frameCounter % 5 != 0) return;

            _tilesDirty = false;
            _transformDirty = false;
            _forceImmediateTileLoad = false;

            if (!ComputeVisibleTileRange(out int tx0, out int ty0, out int tx1, out int ty1)) return;

            var needed = new HashSet<(int, int)>();
            for (int ty = ty0; ty <= ty1; ty++)
            {
                for (int tx = tx0; tx <= tx1; tx++)
                {
                    needed.Add((tx, ty));
                    if (!_activeImages.TryGetValue((tx, ty), out var img))
                    {
                        img = new Image { Visibility = Visibility.Hidden, SnapsToDevicePixels = true, UseLayoutRounding = true, Stretch = Stretch.Fill };
                        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
                        _activeImages[(tx, ty)] = img;
                        MainCanvas.Children.Add(img);
                    }
                    var bmp = LoadTile(_zoom, tx, ty);
                    if (bmp == null) { img.Visibility = Visibility.Hidden; continue; }
                    if (!ReferenceEquals(img.Source, bmp)) img.Source = bmp;
                    PlaceTile(img, tx, ty, drawTileW, drawTileH);
                    img.Visibility = Visibility.Visible;
                }
            }
            if (_activeImages.Count > needed.Count)
            {
                var remove = new List<(int, int)>();
                foreach (var kv in _activeImages) if (!needed.Contains(kv.Key)) remove.Add(kv.Key);
                foreach (var key in remove) { MainCanvas.Children.Remove(_activeImages[key]); _activeImages.Remove(key); }
            }
        }

        #region Interaction
        private void MainCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!_worldSizeLoaded) return;
            double old = _scale;
            _scale *= e.Delta > 0 ? 1.1 : 0.9;
            if (_scale < _minScale) _scale = _minScale; // 仅保持不小于适配比例
            Point m = e.GetPosition(MainCanvas);
            _offset.X = m.X - (m.X - _offset.X) * (_scale / old);
            _offset.Y = m.Y - (m.Y - _offset.Y) * (_scale / old);
            MarkTransformDirtyOnly();
            CheckIfRangeChanged();
        }
        Point _last;
        private void MainCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        { _last = e.GetPosition(this); MainCanvas.CaptureMouse(); }
        private void MainCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (MainCanvas.IsMouseCaptured)
            {
                Point p = e.GetPosition(this);
                _offset.X += p.X - _last.X;
                _offset.Y += p.Y - _last.Y;
                _last = p;
                MarkTransformDirtyOnly();
                CheckIfRangeChanged();
            }
        }
        private void MainCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        { MainCanvas.ReleaseMouseCapture(); }
        #endregion
    }
}