using log4net;
using log4net.Appender;
using SW2URDF.Utilities;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace SW2URDF.Test
{
    public class TestLogger
    {
        [Fact]
        public void TestGetLogger()
        {
            Assert.NotNull(Logger.GetLogger());
        }

        [Fact]
        public void TestGetLoggerTwice()
        {
            Assert.Equal(Logger.GetLogger(), Logger.GetLogger());
        }

        [Fact]
        public void TestLoggerFileExists()
        {
            ILog logger = Logger.GetLogger();
            string filename = Logger.GetFileName();
            string message = "Hello there";
            logger.Info(message);
            LogManager.Flush(1000);
            Assert.True(File.Exists(filename));

            string text = ReadSharedText(filename, Encoding.UTF8);
            Assert.Contains(message, text);
        }

        [Fact]
        public void TestLoggerUsesUtf8Encoding()
        {
            Logger.GetLogger();
            RollingFileAppender appender = LogManager.GetRepository()
                .GetAppenders()
                .OfType<RollingFileAppender>()
                .FirstOrDefault();

            Assert.NotNull(appender);
            Assert.Equal("utf-8", appender.Encoding.WebName);
        }

        [Fact]
        public void TestLoggerWritesValidUtf8()
        {
            ILog logger = Logger.GetLogger();
            string filename = Logger.GetFileName();
            string message = "UTF-8 path check: 中文路径";

            logger.Info(message);
            LogManager.Flush(1000);

            UTF8Encoding strictUtf8 = new UTF8Encoding(false, true);
            string text = ReadSharedText(filename, strictUtf8);
            Assert.Contains(message, text);
        }

        private static string ReadSharedText(string filename, Encoding encoding)
        {
            using (FileStream stream = new FileStream(
                filename,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite))
            using (StreamReader reader = new StreamReader(stream, encoding, true))
            {
                return reader.ReadToEnd();
            }
        }
    }
}
