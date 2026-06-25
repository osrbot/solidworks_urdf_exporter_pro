using SW2URDF.Utilities;
using System;
using Xunit;

namespace SW2URDF.Test
{
    public class TestOperationHeartbeat
    {
        [Theory]
        [InlineData(0, 0, 5, "00:05")]
        [InlineData(0, 12, 34, "12:34")]
        [InlineData(1, 2, 3, "01:02:03")]
        public void TestFormatElapsed(int hours, int minutes, int seconds, string expected)
        {
            TimeSpan elapsed = new TimeSpan(hours, minutes, seconds);

            Assert.Equal(expected, OperationHeartbeat.FormatElapsed(elapsed));
        }
    }
}
