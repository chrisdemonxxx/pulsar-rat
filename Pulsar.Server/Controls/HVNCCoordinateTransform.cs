using System.Drawing;

namespace Pulsar.Server.Controls
{
    public static class HVNCCoordinateTransform
    {
        public static int ScaleToRemote(int controlCoordinate, int controlExtent, int remoteExtent)
        {
            if (controlExtent <= 0 || remoteExtent <= 0)
                return controlCoordinate;

            var scaled = (double)controlCoordinate * remoteExtent / controlExtent;
            return Clamp((int)System.Math.Round(scaled), 0, remoteExtent);
        }

        public static Point ScalePoint(Point controlPoint, Size controlSize, Size remoteSize)
        {
            return new Point(
                ScaleToRemote(controlPoint.X, controlSize.Width, remoteSize.Width),
                ScaleToRemote(controlPoint.Y, controlSize.Height, remoteSize.Height));
        }

        public static Rectangle GetStretchedViewport(Size controlSize, Size remoteSize)
        {
            if (controlSize.Width <= 0 || controlSize.Height <= 0 ||
                remoteSize.Width <= 0 || remoteSize.Height <= 0)
            {
                return new Rectangle(Point.Empty, controlSize);
            }

            return new Rectangle(Point.Empty, controlSize);
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            if (value < minimum)
                return minimum;
            if (value > maximum)
                return maximum;
            return value;
        }
    }
}
