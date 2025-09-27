using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;

namespace StarMapViewer.Services
{
    public class TileCacheService
    {
        private readonly Dictionary<string, BitmapImage> _bmpCache = new();
        private readonly LinkedList<string> _lru = new();
        private const int CacheCapacity = 1024;

        public BitmapImage? LoadTile(string tilesRoot, int zoom, int x, int y)
        {
            string path = Path.Combine(tilesRoot, zoom.ToString(), $"{x}_{y}.jpg");
            if (!File.Exists(path)) return null;
            
            if (_bmpCache.TryGetValue(path, out var cached)) 
            { 
                Promote(path); 
                return cached; 
            }
            
            try
            {
                using var fs = File.OpenRead(path);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad; // 读取到内存后释放文件句柄
                bmp.StreamSource = fs;
                bmp.EndInit();
                bmp.Freeze();
                _bmpCache[path] = bmp;
                _lru.AddFirst(path);
                Evict();
                return bmp;
            }
            catch { return null; }
        }

        public void ClearCache()
        {
            _bmpCache.Clear();
            _lru.Clear();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
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
            while (_bmpCache.Count > CacheCapacity && _lru.Last != null)
            {
                var victim = _lru.Last.Value; 
                _lru.RemoveLast(); 
                _bmpCache.Remove(victim);
            }
        }
    }
}