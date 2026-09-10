using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CSharp;
using SW2URDF.URDFExport;
using Xunit;

namespace SW2URDF.Test
{
    public sealed class TestBundledStlMeshReducer : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "stl-worker-" + Guid.NewGuid().ToString("N"));
        private string Mesh => Path.Combine(directory, "source.stl");
        private string Worker => Path.Combine(directory, "worker.json");
        private string Executable => Path.Combine(directory, "fake-python.exe");

        public TestBundledStlMeshReducer()
        {
            Directory.CreateDirectory(directory);
            WriteMesh();
        }

        [Theory]
        [InlineData(-0.1)]
        [InlineData(1.1)]
        [InlineData(Double.NaN)]
        [InlineData(Double.PositiveInfinity)]
        public void InvalidRatiosAreRejectedBeforeChangingSource(double ratio)
        {
            byte[] original = File.ReadAllBytes(Mesh);
            Assert.Throws<ArgumentOutOfRangeException>(() => Run(ratio));
            Assert.Equal(original, File.ReadAllBytes(Mesh));
        }

        [Fact]
        public void ZeroRatioNeedsNoRuntimeAndPreservesBytes()
        {
            byte[] original = File.ReadAllBytes(Mesh);
            var result = Run(0);
            Assert.Equal("unchanged", result.Status);
            Assert.Equal("", result.Warning);
            Assert.Equal(original, File.ReadAllBytes(Mesh));
        }

        [Fact]
        public void MissingRuntimeRetainsCompleteMeshWithWarning()
        {
            byte[] original = File.ReadAllBytes(Mesh);
            var result = Run(.67);
            Assert.Equal("unchanged", result.Status);
            Assert.Contains("runtime is missing", result.Warning);
            Assert.Equal(original, File.ReadAllBytes(Mesh));
            Assert.Empty(Directory.GetDirectories(directory));
        }

        [Fact]
        public void InvalidSourceIsNotReportedAsSuccessfulFallback()
        {
            File.WriteAllBytes(Mesh, new byte[84]);
            Assert.Throws<InvalidDataException>(() => Run(.5));
        }

        [Fact]
        public void CandidateCannotExceedRequestedRemoval()
        {
            CreateWorker("valid");
            byte[] original = File.ReadAllBytes(Mesh);
            var result = Run(.1);
            Assert.Equal("unchanged", result.Status);
            Assert.NotEmpty(result.Warning);
            Assert.Equal(original, File.ReadAllBytes(Mesh));
        }

        [Theory]
        [InlineData("wrong-hash")]
        [InlineData("wrong-ratio")]
        [InlineData("wrong-count")]
        [InlineData("crash")]
        [InlineData("unchanged-modified")]
        [InlineData("malformed-schema")]
        [InlineData("overflow-count")]
        public void InvalidWorkerOutputNeverReplacesOriginal(string behavior)
        {
            CreateWorker(behavior);
            byte[] original = File.ReadAllBytes(Mesh);
            var result = Run(.5);
            Assert.Equal("unchanged", result.Status);
            Assert.NotEmpty(result.Warning);
            Assert.Equal(original, File.ReadAllBytes(Mesh));
            Assert.Empty(Directory.GetDirectories(directory));
        }

        [Fact]
        public void VerifiedCandidateIsReplacedAndContentCacheDoesNotUseSourceName()
        {
            CreateWorker("valid");
            byte[] original = File.ReadAllBytes(Mesh);
            var result = Run(.5);
            Assert.Equal("reduced", result.Status);
            Assert.Equal(1U, result.FinalTriangles);
            Assert.Equal(134L, new FileInfo(Mesh).Length);
            File.WriteAllBytes(Mesh, original);
            bool cacheHit = false;
            result = Run(.5, update => cacheHit |= update.Stage == "cache");
            Assert.True(cacheHit);
            Assert.Equal("reduced", result.Status);
            WriteMesh(9);
            cacheHit = false;
            Run(.5, update => cacheHit |= update.Stage == "cache");
            Assert.False(cacheHit);
        }

        [Fact]
        public void TimeoutKillsOwnedWorkerAndRetainsOriginal()
        {
            CreateWorker("sleep");
            byte[] original = File.ReadAllBytes(Mesh);
            var result = BundledStlMeshReducer.ReduceFile(Mesh, .5, Executable, Worker,
                TimeSpan.FromMilliseconds(200), null, CancellationToken.None);
            Assert.Equal("unchanged", result.Status);
            Assert.Contains("time limit", result.Warning);
            Assert.Equal(original, File.ReadAllBytes(Mesh));
            Assert.Empty(Directory.GetDirectories(directory));
        }

        [Fact]
        public void CancellationStopsWorkerWithoutReplacingSource()
        {
            CreateWorker("sleep");
            byte[] original = File.ReadAllBytes(Mesh);
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.CancelAfter(150);
                Assert.Throws<OperationCanceledException>(() => BundledStlMeshReducer.ReduceFile(
                    Mesh, .5, Executable, Worker, TimeSpan.FromSeconds(10), null, cancellation.Token));
            }
            Assert.Equal(original, File.ReadAllBytes(Mesh));
            Assert.Empty(Directory.GetDirectories(directory));
        }

        private StlMeshReducer.Result Run(double ratio, Action<BundledStlMeshReducer.Progress> progress = null)
        {
            return BundledStlMeshReducer.ReduceFile(Mesh, ratio, Executable, Worker,
                TimeSpan.FromSeconds(10), progress, CancellationToken.None);
        }

        private void WriteMesh(float offset = 0)
        {
            using (var writer = new BinaryWriter(File.Create(Mesh)))
            {
                writer.Write(new byte[80]); writer.Write(2U);
                foreach (int i in new[] { 0, 1 })
                {
                    writer.Write(0f); writer.Write(0f); writer.Write(1f);
                    writer.Write(offset + i); writer.Write(0f); writer.Write(0f);
                    writer.Write(offset + i + 1); writer.Write(0f); writer.Write(0f);
                    writer.Write(offset + i); writer.Write(1f); writer.Write(0f);
                    writer.Write((ushort)0);
                }
            }
        }

        private void CreateWorker(string behavior)
        {
            File.WriteAllText(Worker, behavior);
            using (var compiler = new CSharpCodeProvider())
            {
                var options = new CompilerParameters(new[] { "System.dll", "System.Core.dll", "System.Web.Extensions.dll" }, Executable)
                { GenerateExecutable = true };
                CompilerResults compiled = compiler.CompileAssemblyFromSource(options, @"
using System; using System.IO; using System.Linq; using System.Collections.Generic;
using System.Web.Script.Serialization; using System.Security.Cryptography; using System.Threading;
class Fake {
 static int Main(string[] args) {
  string mode=File.ReadAllText(args[1]);
  if(mode==""crash"") return 7;
  if(mode==""sleep"") Thread.Sleep(20000);
  var json=new JavaScriptSerializer();
  var r=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(args[3]));
  byte[] source=File.ReadAllBytes((string)r[""input""]);
  byte[] output=source.Take(134).ToArray(); output[80]=1;
  string hash; using(var sha=SHA256.Create()) hash=BitConverter.ToString(sha.ComputeHash(source)).Replace(""-"","""").ToLowerInvariant();
  if(mode==""unchanged-modified"") { output=(byte[])source.Clone(); output[0]=42; }
  File.WriteAllBytes((string)r[""output""],output);
  var result=new { schemaVersion=1, sourceSha256=mode==""wrong-hash""?""wrong"":hash,
   ratio=mode==""wrong-ratio""?0.1:Convert.ToDouble(r[""ratio""]), originalTriangles=2, originalBytes=source.Length,
   finalTriangles=mode==""wrong-count""?123:mode==""unchanged-modified""?2:1, finalBytes=output.Length,
   status=mode==""unchanged-modified""?""unchanged"":""reduced"", warning="""" };
  Console.WriteLine(""{\""type\"":\""progress\"",\""stage\"":\""reduce\"",\""completed\"":1,\""total\"":1}"");
  string text=json.Serialize(result);
  if(mode==""malformed-schema"") text=text.Replace(""\""schemaVersion\"":1"", ""\""schemaVersion\"":{}"");
  if(mode==""overflow-count"") text=text.Replace(""\""originalTriangles\"":2"", ""\""originalTriangles\"":999999999999999999999999999"");
  File.WriteAllText(args[5],text); return 0;
 }
}");
                Assert.False(compiled.Errors.HasErrors, String.Join("\n", compiled.Errors.Cast<CompilerError>()));
            }
        }

        public void Dispose() { Directory.Delete(directory, true); }
    }
}
