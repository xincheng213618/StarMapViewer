using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace StarMapViewer.Services
{
    public class InteractionService
    {
        private System.Windows.Point _lastMousePosition;

        public (double scale, System.Windows.Point offset) HandleMouseWheel(MouseWheelEventArgs e, Canvas canvas, double currentScale, double minScale, System.Windows.Point currentOffset)
        {
            double oldScale = currentScale;
            double newScale = currentScale * (e.Delta > 0 ? 1.1 : 0.9);
            if (newScale < minScale) newScale = minScale; // 仅保持不小于适配比例
            
            System.Windows.Point mousePos = e.GetPosition(canvas);
            var newOffset = new System.Windows.Point(
                mousePos.X - (mousePos.X - currentOffset.X) * (newScale / oldScale),
                mousePos.Y - (mousePos.Y - currentOffset.Y) * (newScale / oldScale)
            );
            
            return (newScale, newOffset);
        }

        public void HandleMouseLeftButtonDown(MouseButtonEventArgs e, Canvas canvas, Window parentWindow)
        {
            _lastMousePosition = e.GetPosition(parentWindow);
            canvas.CaptureMouse();
        }

        public System.Windows.Point HandleMouseMove(System.Windows.Input.MouseEventArgs e, Canvas canvas, System.Windows.Point currentOffset, Window parentWindow)
        {
            if (canvas.IsMouseCaptured)
            {
                System.Windows.Point currentPos = e.GetPosition(parentWindow);
                var newOffset = new System.Windows.Point(
                    currentOffset.X + currentPos.X - _lastMousePosition.X,
                    currentOffset.Y + currentPos.Y - _lastMousePosition.Y
                );
                _lastMousePosition = currentPos;
                return newOffset;
            }
            return currentOffset;
        }

        public void HandleMouseLeftButtonUp(Canvas canvas)
        {
            canvas.ReleaseMouseCapture();
        }
    }
}