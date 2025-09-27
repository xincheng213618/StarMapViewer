using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using WpfImage = System.Windows.Controls.Image;

namespace StarMapViewer.Services
{
    public class ActiveTileService
    {
        private readonly Dictionary<(int x, int y), WpfImage> _activeImages = new();

        public Dictionary<(int x, int y), WpfImage> ActiveImages => _activeImages;

        public int TileCount => _activeImages.Count;

        public void ClearAllActiveTiles(Canvas canvas)
        {
            foreach (var kv in _activeImages)
            {
                kv.Value.Source = null;
                canvas.Children.Remove(kv.Value);
            }
            _activeImages.Clear();
        }

        public WpfImage GetOrCreateTileImage((int x, int y) key, Canvas canvas)
        {
            if (!_activeImages.TryGetValue(key, out var img))
            {
                img = new WpfImage 
                { 
                    Visibility = Visibility.Hidden, 
                    SnapsToDevicePixels = true, 
                    UseLayoutRounding = true, 
                    Stretch = System.Windows.Media.Stretch.Fill 
                };
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(img, System.Windows.Media.BitmapScalingMode.NearestNeighbor);
                _activeImages[key] = img;
                canvas.Children.Add(img);
            }
            return img;
        }

        public void RemoveUnusedTiles(HashSet<(int, int)> neededTiles, Canvas canvas)
        {
            if (_activeImages.Count <= neededTiles.Count) return;

            var remove = new List<(int, int)>();
            foreach (var kv in _activeImages) 
                if (!neededTiles.Contains(kv.Key)) 
                    remove.Add(kv.Key);
            
            foreach (var key in remove) 
            { 
                _activeImages[key].Source = null; 
                canvas.Children.Remove(_activeImages[key]); 
                _activeImages.Remove(key); 
            }
        }
    }
}