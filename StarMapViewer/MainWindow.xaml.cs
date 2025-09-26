using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StarMapViewer.Configuration;
using StarMapViewer.Services;
using StarMapViewer.Utils;

namespace StarMapViewer
{
    public partial class MainWindow : Window
    {
        // Core services
        private readonly ITileCacheManager _cacheManager;
        private readonly ITileRenderer _tileRenderer;
        private readonly IWorldSizeManager _worldSizeManager;

        // View state
        private double _scale = 1.0;          // 当前缩放(屏幕像素/世界像素)
        private double _minScale = 0.01;      // 适配整图最小缩放
        private Point _offset = new(0, 0);    // 屏幕平移
        private bool _transformDirty = true;  // 仅坐标或缩放变化

        // Zoom state
        private int _zoom = 0;                // 当前瓦片级别
        private bool _fitPending = true;      // 需要执行自适应
        private bool _firstFit = true;        // 首次适配标记

        // Tile loading state
        private TileRange _lastVisibleRange = new();
        private int _lastZoom = -1;
        private bool _tilesDirty = true;      // 需要刷新瓦片集合
        private bool _forceImmediateTileLoad = false; // 缩放级别切换时立即加载以避免白屏

        // Frame management
        private int _frameCounter = 0;        // 渲染帧计数

        public MainWindow()
        {
            InitializeComponent();
            
            // Initialize configuration
            AppConfig.Initialize();
            
            // Initialize services
            _cacheManager = new TileCacheManager();
            _tileRenderer = new TileRenderer(_cacheManager);
            _worldSizeManager = new WorldSizeManager();
            
            // Setup event handlers
            CompositionTarget.Rendering += OnRenderFrame;
            MainCanvas.SizeChanged += (_, __) => { _fitPending = true; MarkTilesDirty(); };
            Loaded += (_, __) => { _fitPending = true; MarkTilesDirty(); };
        }

        protected override void OnClosed(EventArgs e)
        {
            // Cleanup services
            _tileRenderer?.Dispose();
            _cacheManager?.Dispose();
            base.OnClosed(e);
        }

        #region Helpers
        void MarkTilesDirty() { _tilesDirty = true; _transformDirty = true; }
        void MarkTransformDirtyOnly() => _transformDirty = true;
        #endregion

        #region Zoom selection
        // 根据当前缩放倍率选取瓦片级别：scale 相对 fitScale 的 2 对数（>=1 时才进入更高层级）
        void UpdateDesiredZoomLevel()
        {
            var desired = CoordinateCalculator.CalculateDesiredZoomLevel(_scale, _minScale, AppConfig.MaxZoomLevel);
            if (desired != _zoom)
            {
                _zoom = desired;
                // 不立即清空画布，先保留旧级别瓦片直到新级别加载 (减少闪白)
                // 但为防止索引冲突仍需清空，随后立即加载新瓦片（强制取消节流）
                _tileRenderer.ClearActiveTiles(MainCanvas);
                _forceImmediateTileLoad = true;
                MarkTilesDirty();
            }
        }
        #endregion

        #region World size & Fit
        async Task EnsureWorldSizeAsync()
        {
            if (_worldSizeManager.IsWorldSizeLoaded) return;
            
            var size = await _worldSizeManager.GetWorldSizeAsync();
            if (size.HasValue)
            {
                _fitPending = true;
                MarkTilesDirty();
            }
        }

        void FitToViewIfNeeded()
        {
            if (!_worldSizeManager.IsWorldSizeLoaded || !_fitPending) return;
            if (MainCanvas.ActualWidth <= 0 || MainCanvas.ActualHeight <= 0) return;
            
            double fitScale = Math.Min(
                MainCanvas.ActualWidth / _worldSizeManager.WorldWidth,
                MainCanvas.ActualHeight / _worldSizeManager.WorldHeight);
                
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
            
            _offset.X = (MainCanvas.ActualWidth - _worldSizeManager.WorldWidth * _scale) / 2.0;
            _offset.Y = (MainCanvas.ActualHeight - _worldSizeManager.WorldHeight * _scale) / 2.0;
            _fitPending = false;
            MarkTransformDirtyOnly();
        }
        #endregion

        #region Visible range computation
        void CheckIfRangeChanged()
        {
            if (!_worldSizeManager.IsWorldSizeLoaded) return;
            
            var visibleRange = CoordinateCalculator.CalculateVisibleTileRange(
                MainCanvas.ActualWidth, MainCanvas.ActualHeight,
                _offset, _scale, _zoom,
                _worldSizeManager.WorldWidth, _worldSizeManager.WorldHeight);
                
            if (_lastZoom != _zoom || !_lastVisibleRange.Equals(visibleRange))
            {
                _lastZoom = _zoom;
                _lastVisibleRange = visibleRange;
                MarkTilesDirty();
            }
        }
        #endregion

        void UpdateCoordText()
        {
            var coordText = this.FindName("CoordText") as TextBlock;
            if (coordText == null) return;
            
            if (!_worldSizeManager.IsWorldSizeLoaded || MainCanvas.ActualWidth <= 0 || MainCanvas.ActualHeight <= 0) 
            { 
                coordText.Text = ""; 
                return; 
            }

            var center = CoordinateCalculator.ScreenToWorld(
                new Point(MainCanvas.ActualWidth / 2.0, MainCanvas.ActualHeight / 2.0),
                _offset, _scale);

            var tileNames = CoordinateCalculator.GetCenterTileNames(
                MainCanvas.ActualWidth, MainCanvas.ActualHeight,
                _offset, _scale, _zoom,
                _worldSizeManager.WorldWidth, _worldSizeManager.WorldHeight);

            coordText.Text = $"Level: {_zoom} | Tiles: {_tileRenderer.ActiveTileCount} | ({center.X:F0}, {center.Y:F0}) | {tileNames}";
        }

        async void OnRenderFrame(object? sender, EventArgs e)
        {
            _frameCounter++;
            await EnsureWorldSizeAsync();
            FitToViewIfNeeded();
            
            if (!_worldSizeManager.IsWorldSizeLoaded) return;
            
            UpdateDesiredZoomLevel();
            CheckIfRangeChanged();
            UpdateCoordText();

            var (tileW, tileH) = CoordinateCalculator.GetWorldTileDimensions(_zoom, 
                _worldSizeManager.WorldWidth, _worldSizeManager.WorldHeight);
            var drawTileW = tileW * _scale;
            var drawTileH = tileH * _scale;

            if (!_tilesDirty && _transformDirty)
            {
                _tileRenderer.UpdateTilePositions(MainCanvas, drawTileW, drawTileH, _offset);
                _transformDirty = false;
                return;
            }

            if (!_tilesDirty) return;
            
            // 若是缩放级别刚变更，强制本帧加载；否则每配置帧数节流一次
            if (!_forceImmediateTileLoad && _frameCounter % AppConfig.TileLoadingThrottleFrames != 0) return;

            _tilesDirty = false;
            _transformDirty = false;
            _forceImmediateTileLoad = false;

            var visibleRange = CoordinateCalculator.CalculateVisibleTileRange(
                MainCanvas.ActualWidth, MainCanvas.ActualHeight,
                _offset, _scale, _zoom,
                _worldSizeManager.WorldWidth, _worldSizeManager.WorldHeight);

            if (visibleRange.IsValid)
            {
                // Load tiles asynchronously to avoid blocking the UI thread
                _ = Task.Run(async () =>
                {
                    await _tileRenderer.LoadVisibleTilesAsync(MainCanvas, visibleRange, _zoom, drawTileW, drawTileH, _offset);
                });
            }
        }

        #region Interaction
        private void MainCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!_worldSizeManager.IsWorldSizeLoaded) return;
            
            var old = _scale;
            _scale *= e.Delta > 0 ? AppConfig.ZoomScaleFactor : (1.0 / AppConfig.ZoomScaleFactor);
            if (_scale < _minScale) _scale = _minScale; // 仅保持不小于适配比例
            
            var m = e.GetPosition(MainCanvas);
            _offset.X = m.X - (m.X - _offset.X) * (_scale / old);
            _offset.Y = m.Y - (m.Y - _offset.Y) * (_scale / old);
            MarkTransformDirtyOnly();
            CheckIfRangeChanged();
        }
        
        Point _last;
        private void MainCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        { 
            _last = e.GetPosition(this); 
            MainCanvas.CaptureMouse(); 
        }
        
        private void MainCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (MainCanvas.IsMouseCaptured)
            {
                var p = e.GetPosition(this);
                _offset.X += p.X - _last.X;
                _offset.Y += p.Y - _last.Y;
                _last = p;
                MarkTransformDirtyOnly();
                CheckIfRangeChanged();
            }
        }
        
        private void MainCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        { 
            MainCanvas.ReleaseMouseCapture(); 
        }

        private void ClearCacheBtn_Click(object sender, RoutedEventArgs e)
        {
            _cacheManager.ClearCache();
            _tileRenderer.ClearActiveTiles(MainCanvas);
            
            // Force garbage collection to free up memory
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        #endregion
    }
}