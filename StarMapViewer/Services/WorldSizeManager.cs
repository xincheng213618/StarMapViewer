using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using StarMapViewer.Configuration;

namespace StarMapViewer.Services
{
    public interface IWorldSizeManager
    {
        Task<(int width, int height)?> GetWorldSizeAsync();
        bool IsWorldSizeLoaded { get; }
        int WorldWidth { get; }
        int WorldHeight { get; }
    }

    public class WorldSizeManager : IWorldSizeManager
    {
        private int _worldWidth;
        private int _worldHeight;
        private bool _isLoaded;

        public bool IsWorldSizeLoaded => _isLoaded;
        public int WorldWidth => _worldWidth;
        public int WorldHeight => _worldHeight;

        public async Task<(int width, int height)?> GetWorldSizeAsync()
        {
            if (_isLoaded)
                return (_worldWidth, _worldHeight);

            try
            {
                var path = Path.Combine(AppConfig.TilesRoot, "0", "0_0.jpg");
                if (!File.Exists(path))
                    return null;

                var size = await LoadImageSizeAsync(path);
                if (size.HasValue)
                {
                    _worldWidth = size.Value.width;
                    _worldHeight = size.Value.height;
                    _isLoaded = true;
                    return size;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading world size: {ex.Message}");
            }

            return null;
        }

        private static async Task<(int width, int height)?> LoadImageSizeAsync(string path)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(path, UriKind.Absolute);
                    bmp.EndInit();
                    return (bmp.PixelWidth, bmp.PixelHeight);
                }
                catch
                {
                    return null;
                }
            });
        }
    }
}