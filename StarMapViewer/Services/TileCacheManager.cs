using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using StarMapViewer.Configuration;

namespace StarMapViewer.Services
{
    public interface ITileCacheManager : IDisposable
    {
        Task<BitmapImage?> LoadTileAsync(int zoom, int x, int y);
        void ClearCache();
        int CacheCount { get; }
    }

    public class TileCacheManager : ITileCacheManager
    {
        private readonly Dictionary<string, BitmapImage> _bmpCache;
        private readonly LinkedList<string> _lru;
        private readonly object _cacheLock;
        private bool _disposed;

        public int CacheCount => _bmpCache.Count;

        public TileCacheManager()
        {
            _bmpCache = new Dictionary<string, BitmapImage>();
            _lru = new LinkedList<string>();
            _cacheLock = new object();
        }

        public async Task<BitmapImage?> LoadTileAsync(int zoom, int x, int y)
        {
            var path = Path.Combine(AppConfig.TilesRoot, zoom.ToString(), $"{x}_{y}.jpg");
            
            // Check cache first
            lock (_cacheLock)
            {
                if (_bmpCache.TryGetValue(path, out var cached))
                {
                    Promote(path);
                    return cached;
                }
            }

            // Check if file exists
            if (!File.Exists(path))
                return null;

            try
            {
                // Load asynchronously
                var bmp = await LoadBitmapAsync(path);
                if (bmp == null)
                    return null;

                lock (_cacheLock)
                {
                    // Double-check in case another thread loaded it
                    if (_bmpCache.TryGetValue(path, out var existing))
                    {
                        bmp.Freeze(); // Ensure it's frozen for thread safety
                        return existing;
                    }

                    bmp.Freeze();
                    _bmpCache[path] = bmp;
                    _lru.AddFirst(path);
                    Evict();
                    return bmp;
                }
            }
            catch (Exception ex)
            {
                // Log exception if logging is available
                System.Diagnostics.Debug.WriteLine($"Error loading tile {path}: {ex.Message}");
                return null;
            }
        }

        private async Task<BitmapImage?> LoadBitmapAsync(string path)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var fs = File.OpenRead(path);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = fs;
                    bmp.EndInit();
                    return bmp;
                }
                catch
                {
                    return null;
                }
            });
        }

        private void Promote(string path)
        {
            var node = _lru.Find(path);
            if (node != null && node != _lru.First)
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
            }
        }

        private void Evict()
        {
            while (_bmpCache.Count > AppConfig.CacheCapacity && _lru.Last != null)
            {
                var victim = _lru.Last.Value;
                _lru.RemoveLast();
                _bmpCache.Remove(victim);
            }
        }

        public void ClearCache()
        {
            lock (_cacheLock)
            {
                _bmpCache.Clear();
                _lru.Clear();
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            ClearCache();
            _disposed = true;
        }
    }
}