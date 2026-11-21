#if NET9_0_OR_GREATER
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pulsar.Server.Controls;
using System.Drawing;

namespace Pulsar.Common.Tests.HVNC
{
    [TestClass]
    public class HVNCCoordinateTransformTests
    {
        [TestMethod]
        public void ScalePoint_PreservesFitBounds()
        {
            var result = HVNCCoordinateTransform.ScalePoint(
                new Point(400, 300),
                new Size(800, 600),
                new Size(1920, 1080));

            Assert.AreEqual(960, result.X);
            Assert.AreEqual(540, result.Y);
        }

        [TestMethod]
        public void ScaleToRemote_ClampsOutsideCoordinates()
        {
            Assert.AreEqual(1920, HVNCCoordinateTransform.ScaleToRemote(900, 800, 1920));
            Assert.AreEqual(0, HVNCCoordinateTransform.ScaleToRemote(-100, 800, 1920));
        }

        [TestMethod]
        public void ScalePoint_ReturnsInputWhenExtentIsUnavailable()
        {
            var result = HVNCCoordinateTransform.ScalePoint(
                new Point(40, 30),
                new Size(0, 0),
                new Size(1920, 1080));

            Assert.AreEqual(40, result.X);
            Assert.AreEqual(30, result.Y);
        }
    }
}
#endif
