using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Collections.Generic;
using System.IO;
using StarMapViewer.Services;
using System.Linq;

namespace StarMapViewer
{
    public partial class MainWindow : Window
    {
        const string DefaultTilesRoot = "C\\Users\\17917\\Desktop\\scgd_general_wpf\\x64\\Debug\\tiles"; // tiles/{zoom}/{x}_{y}.jpg
        private string _tilesRoot = DefaultTilesRoot; // 可变瓦片根目录
        const int MaxZoomLevel = 5;   // 瓦片最高级别(数据上限)

        double _scale = 1.0;          // 当前缩放(屏幕像素/世界像素)
        double _minScale = 0.01;      // 适配整图最小缩放
        System.Windows.Point _offset = new(0, 0);    // 屏幕平移
        bool _transformDirty = true;  // 仅坐标或缩放变化

        int _zoom = 0;                // 当前瓦片级别 (0 = 只有一张整图)
        bool _fitPending = true;      // 需要执行自适应
        bool _firstFit = true;        // 首次适配标记

        int _lastZoom = -1;
        int _lastTx0, _lastTy0, _lastTx1, _lastTy1; // 上次可视瓦片范围
        bool _tilesDirty = true;      // 需要刷新瓦片集合
        bool _forceImmediateTileLoad = false; // 缩放级别切换时立即加载以避免白屏

        int _frameCounter = 0;        // 渲染帧计数

        // Service instances
        private readonly TileCacheService _cacheService = new();
        private readonly TileRenderService _renderService = new();
        private readonly WorldCoordinateService _worldService = new();
        private readonly VisibleRangeService _rangeService = new();
        private readonly ActiveTileService _tileService = new();
        private readonly InteractionService _interactionService = new();
        private readonly CoordinateDisplayService _coordinateService = new();

        public MainWindow()
        {
            InitializeComponent();
            CompositionTarget.Rendering += OnRenderFrame;
            MainCanvas.SizeChanged += (_, __) => { _fitPending = true; MarkTilesDirty(); };
            Loaded += (_, __) => { _fitPending = true; MarkTilesDirty(); };
        }

        void ResetWorldState()
        {
            _tileService.ClearAllActiveTiles(MainCanvas);
            _worldService.Reset();
            _zoom = 0;
            _lastZoom = -1;
            _firstFit = true;
            _fitPending = true;
            _offset = new System.Windows.Point(0, 0);
            _scale = 1.0;
            MarkTilesDirty();
        }

        #region Helpers
        void MarkTilesDirty() { _tilesDirty = true; _transformDirty = true; }
        void MarkTransformDirtyOnly() => _transformDirty = true;
        #endregion

        #region Zoom selection
        // 根据当前缩放倍率选取瓦片级别：scale 相对 fitScale 的 2 对数（>=1 时才进入更高层级）
        void UpdateDesiredZoomLevel()
        {
            int desired = _renderService.UpdateDesiredZoomLevel(_scale, _minScale, _zoom, MaxZoomLevel);
            if (desired != _zoom)
            {
                _zoom = desired;
                _tileService.ClearAllActiveTiles(MainCanvas);
                _forceImmediateTileLoad = true;
                MarkTilesDirty();
            }
        }
        #endregion

        #region Cache
        void ClearAllCaches()
        {
            _tileService.ClearAllActiveTiles(MainCanvas);
            _cacheService.ClearCache();
        }
        #endregion

        #region Active Tiles (managed by service)
        #endregion

        #region World size & Fit
        void EnsureWorldSize()
        {
            _worldService.EnsureWorldSize(_tilesRoot);
            if (_worldService.WorldSizeLoaded)
            {
                _fitPending = true;
                MarkTilesDirty();
            }
        }

        void FitToViewIfNeeded()
        {
            if (!_worldService.WorldSizeLoaded || !_fitPending) return;
            if (MainCanvas.ActualWidth <= 0 || MainCanvas.ActualHeight <= 0) return;

            var (newScale, newOffset) = _worldService.FitToView(
                MainCanvas.ActualWidth, MainCanvas.ActualHeight, 
                _scale, _offset, _firstFit);
            
            _minScale = _worldService.GetMinScale(MainCanvas.ActualWidth, MainCanvas.ActualHeight);
            _scale = newScale;
            _offset = newOffset;
            _firstFit = false;
            _fitPending = false;
            MarkTransformDirtyOnly();
        }
        #endregion

        #region Visible range computation
        bool ComputeVisibleTileRange(out int tx0, out int ty0, out int tx1, out int ty1)
        {
            return _rangeService.ComputeVisibleTileRange(
                MainCanvas.ActualWidth, MainCanvas.ActualHeight,
                _scale, _offset, _worldService.WorldWidth, _worldService.WorldHeight, _zoom,
                out tx0, out ty0, out tx1, out ty1);
        }

        void CheckIfRangeChanged()
        {
            if (!ComputeVisibleTileRange(out int tx0, out int ty0, out int tx1, out int ty1)) return;
            if (_rangeService.HasRangeChanged(_zoom, tx0, ty0, tx1, ty1, _lastZoom, _lastTx0, _lastTy0, _lastTx1, _lastTy1))
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
            if (!_worldService.WorldSizeLoaded) 
            { 
                coordText.Text = ""; 
                return; 
            }

            coordText.Text = _coordinateService.GenerateCoordinateText(
                MainCanvas.ActualWidth, MainCanvas.ActualHeight,
                _offset, _scale, _zoom, _tileService.TileCount,
                _worldService.WorldWidth, _worldService.WorldHeight,
                _renderService);
        }

        void OnRenderFrame(object? sender, EventArgs e)
        {
            _frameCounter++;
            EnsureWorldSize();
            FitToViewIfNeeded();
            if (!_worldService.WorldSizeLoaded) return;
            UpdateDesiredZoomLevel();
            CheckIfRangeChanged();
            UpdateCoordText();

            double tileWorldW = _renderService.WorldTileWidth(_worldService.WorldWidth, _zoom);
            double tileWorldH = _renderService.WorldTileHeight(_worldService.WorldHeight, _zoom);
            double drawTileW = tileWorldW * _scale;
            double drawTileH = tileWorldH * _scale;

            if (!_tilesDirty && _transformDirty)
            {
                foreach (var kv in _tileService.ActiveImages)
                {
                    var (tx, ty) = kv.Key;
                    _renderService.PlaceTile(kv.Value, tx, ty, drawTileW, drawTileH, _offset, this);
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
                    var img = _tileService.GetOrCreateTileImage((tx, ty), MainCanvas);
                    var bmp = _cacheService.LoadTile(_tilesRoot, _zoom, tx, ty);
                    if (bmp == null) 
                    { 
                        img.Visibility = Visibility.Hidden; 
                        continue; 
                    }
                    if (!ReferenceEquals(img.Source, bmp)) img.Source = bmp;
                    _renderService.PlaceTile(img, tx, ty, drawTileW, drawTileH, _offset, this);
                    img.Visibility = Visibility.Visible;
                }
            }
            _tileService.RemoveUnusedTiles(needed, MainCanvas);
        }

        #region Interaction
        private void MainCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!_worldService.WorldSizeLoaded) return;
            var (newScale, newOffset) = _interactionService.HandleMouseWheel(e, MainCanvas, _scale, _minScale, _offset);
            _scale = newScale;
            _offset = newOffset;
            MarkTransformDirtyOnly();
            CheckIfRangeChanged();
        }

        private void MainCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _interactionService.HandleMouseLeftButtonDown(e, MainCanvas, this);
        }

        private void MainCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            _offset = _interactionService.HandleMouseMove(e, MainCanvas, _offset, this);
            if (MainCanvas.IsMouseCaptured)
            {
                MarkTransformDirtyOnly();
                CheckIfRangeChanged();
            }
        }

        private void MainCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _interactionService.HandleMouseLeftButtonUp(MainCanvas);
        }

        private void ClearCacheBtn_Click(object sender, RoutedEventArgs e)
        {
            ClearAllCaches();
        }

        private void ClearViewBtn_Click(object sender, RoutedEventArgs e)
        {
            _tileService.ClearAllActiveTiles(MainCanvas);
            MarkTilesDirty();
        }

        private void OpenRootBtn_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog();
            dlg.Description = "选择 Tiles 根目录 (包含 0/0_0.jpg)";
            dlg.SelectedPath = _tilesRoot;
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                if (!string.IsNullOrWhiteSpace(dlg.SelectedPath) && Directory.Exists(dlg.SelectedPath))
                {
                    _tilesRoot = dlg.SelectedPath;
                    ClearAllCaches();
                    ResetWorldState();
                }
            }
            dlg.Dispose();
        }
        #endregion
    }
}