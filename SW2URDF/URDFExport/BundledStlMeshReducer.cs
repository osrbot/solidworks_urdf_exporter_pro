using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SW2URDF.URDFExport
{
    // Native geometry code runs outside the SolidWorks process. Only a verified
    // candidate may replace the source; the cache owns bytes, never file paths.
    internal static class BundledStlMeshReducer
    {
        private const long CacheLimit = 64L * 1024 * 1024;
        private const long WorkingSetBudget = 2L * 1024 * 1024 * 1024;
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, CacheEntry> Cache = new Dictionary<string, CacheEntry>();
        private static readonly Queue<string> CacheOrder = new Queue<string>();
        private static long cacheBytes;

        internal sealed class Progress
        {
            public string Stage;
            public int Completed;
            public int Total;
        }

        private sealed class CacheEntry
        {
            internal byte[] Bytes;
            internal StlMeshReducer.Result Result;
        }

        internal static StlMeshReducer.Result ReduceFile(string path, double ratio,
            Action<Progress> progress = null)
        {
            string root = Path.GetDirectoryName(typeof(BundledStlMeshReducer).Assembly.Location);
            return ReduceFile(path, ratio,
                Path.Combine(root, "tools", "openusd_runtime", "python.exe"),
                Path.Combine(root, "tools", "mesh_reduction", "reduce_stl.py"),
                TimeSpan.FromSeconds(300), progress, CancellationToken.None);
        }

        internal static StlMeshReducer.Result ReduceFile(string path, double ratio,
            string python, string worker, TimeSpan timeout, Action<Progress> progress,
            CancellationToken cancellation)
        {
            if (Double.IsNaN(ratio) || Double.IsInfinity(ratio) || ratio < 0 || ratio > 1)
                throw new ArgumentOutOfRangeException(nameof(ratio));
            if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > Int32.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(timeout));
            cancellation.ThrowIfCancellationRequested();
            // This path also retains the existing ASCII no-op support.
            if (ratio == 0) return StlMeshReducer.ReduceFile(path, 0);
            path = Path.GetFullPath(path);
            var result = new StlMeshReducer.Result { Status = "unchanged", Warning = "" };
            string work = null;
            bool sourceValidated = false;
            try
            {
                using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    result.OriginalBytes = result.FinalBytes = source.Length;
                    result.OriginalTriangles = result.FinalTriangles = ReadCount(source);
                    sourceValidated = true;
                    result.TargetTriangles = Math.Max(1U,
                        result.OriginalTriangles - (uint)Math.Floor(result.OriginalTriangles * ratio));
                    if ((long)result.OriginalTriangles * 2048 > WorkingSetBudget)
                        throw new InvalidDataException("The mesh exceeds the reduction working-set budget.");
                    if (!File.Exists(python) || !File.Exists(worker))
                        throw new FileNotFoundException("The bundled STL reduction runtime is missing; repair the installation.");
                    string sourceHash = Hash(source);
                    string key = sourceHash + ":" + ratio.ToString("R", CultureInfo.InvariantCulture) + ":" + HashFile(worker) +
                        ":" + timeout.TotalSeconds.ToString("R", CultureInfo.InvariantCulture);
                    string runtimeLock = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(worker), "..", "mesh_reduction_runtime.lock.json"));
                    key += ":" + (File.Exists(runtimeLock) ? HashFile(runtimeLock) : Path.GetFullPath(python));
                    work = Path.Combine(Path.GetDirectoryName(path), ".sw2urdf-reduce-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(work);
                    string candidate = Path.Combine(work, "candidate.stl");
                    CacheEntry cached;
                    lock (CacheLock) Cache.TryGetValue(key, out cached);
                    if (cached != null)
                    {
                        File.WriteAllBytes(candidate, cached.Bytes);
                        result = Copy(cached.Result);
                        progress?.Invoke(new Progress { Stage = "cache", Completed = 1, Total = 1 });
                    }
                    else
                    {
                        string request = Path.Combine(work, "request.json");
                        string response = Path.Combine(work, "result.json");
                        File.WriteAllText(request, JsonConvert.SerializeObject(new
                        {
                            schemaVersion = 1, input = path, output = candidate, ratio,
                            timeoutSeconds = Math.Max(0.1, timeout.TotalSeconds - Math.Min(20, timeout.TotalSeconds / 5)),
                            maxError = 0.0005, relativeError = 0.005
                        }), new UTF8Encoding(false));
                        Run(python, worker, request, response, timeout, progress, cancellation);
                        if (!File.Exists(response) || new FileInfo(response).Length > 65536)
                            throw new InvalidDataException("The STL reducer returned no valid result document.");
                        JObject report = JObject.Parse(File.ReadAllText(response, Encoding.UTF8));
                        foreach (string field in new[] { "schemaVersion", "originalBytes", "originalTriangles", "finalBytes", "finalTriangles" })
                            if (report[field]?.Type != JTokenType.Integer)
                                throw new InvalidDataException("Invalid numeric STL result field: " + field);
                        if ((report["ratio"]?.Type != JTokenType.Float && report["ratio"]?.Type != JTokenType.Integer) ||
                            report["sourceSha256"]?.Type != JTokenType.String || report["status"]?.Type != JTokenType.String ||
                            (report["warning"] != null && report["warning"].Type != JTokenType.String))
                            throw new InvalidDataException("Invalid STL result field types.");
                        if ((int?)report["schemaVersion"] != 1 ||
                            !String.Equals((string)report["sourceSha256"], sourceHash, StringComparison.OrdinalIgnoreCase) ||
                            (double?)report["ratio"] != ratio ||
                            (long?)report["originalBytes"] != result.OriginalBytes ||
                            (long?)report["originalTriangles"] != result.OriginalTriangles)
                            throw new InvalidDataException("The STL reduction result does not match the source and requested settings.");
                        string status = (string)report["status"];
                        result.Warning = (string)report["warning"] ?? "";
                        if (status == "failed") throw new InvalidDataException("STL reducer: " + result.Warning);
                        if (status != "reduced" && status != "unchanged")
                            throw new InvalidDataException("Unknown STL reduction result status.");
                        using (var output = File.OpenRead(candidate))
                        {
                            uint count = ReadCount(output);
                            if ((long?)report["finalTriangles"] != count ||
                                (long?)report["finalBytes"] != output.Length || count > result.OriginalTriangles ||
                                count < result.TargetTriangles ||
                                output.Length > source.Length ||
                                (status == "reduced" && count >= result.OriginalTriangles) ||
                                (status == "unchanged" && Hash(output) != sourceHash))
                                throw new InvalidDataException("The candidate STL does not match the reduction report.");
                            result.FinalTriangles = count;
                            result.FinalBytes = output.Length;
                            result.Status = status;
                        }
                    }
                    cancellation.ThrowIfCancellationRequested();
                    if (Hash(source) != sourceHash)
                        throw new InvalidDataException("The source STL changed during reduction.");
                    if (result.Status != "reduced") return result;
                    byte[] cacheCandidate = cached == null && result.FinalBytes <= CacheLimit
                        ? File.ReadAllBytes(candidate) : null;
                    cancellation.ThrowIfCancellationRequested();
                    // Release the read lock only for the atomic replacement on the same volume.
                    source.Dispose();
                    File.Replace(candidate, path, null);
                    if (cacheCandidate != null) AddCache(key, cacheCandidate, result);
                }
                return result;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (sourceValidated && (exception is IOException || exception is UnauthorizedAccessException ||
                exception is InvalidDataException || exception is JsonException || exception is TimeoutException ||
                exception is System.ComponentModel.Win32Exception || exception is FormatException ||
                exception is InvalidCastException || exception is OverflowException))
            {
                result.FinalTriangles = result.OriginalTriangles;
                result.FinalBytes = result.OriginalBytes;
                result.Status = "unchanged";
                result.Warning = "STL reduction was skipped; the complete original mesh was retained. " + exception.Message;
                return result;
            }
            finally
            {
                // This uniquely owned directory contains only protocol/candidate files.
                if (work != null)
                    try { Directory.Delete(work, true); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
            }
        }

        private static void Run(string python, string worker, string request, string response,
            TimeSpan timeout, Action<Progress> progress, CancellationToken cancellation)
        {
            var start = new ProcessStartInfo
            {
                FileName = python,
                Arguments = "-B " + Quote(worker) + " --request " + Quote(request) + " --result " + Quote(response),
                WorkingDirectory = Path.GetDirectoryName(worker),
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            start.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            start.EnvironmentVariables["OMP_NUM_THREADS"] = "2";
            var messages = new ConcurrentQueue<Progress>();
            var errors = new StringBuilder();
            using (var process = new Process { StartInfo = start })
            {
                if (!process.Start()) throw new IOException("The STL reducer could not start.");
                Task stdout = Task.Run(() =>
                {
                    string line;
                    while ((line = process.StandardOutput.ReadLine()) != null)
                    {
                        if (line.Length > 8192 || messages.Count > 128) continue;
                        try
                        {
                            JObject item = JObject.Parse(line);
                            if (item["type"]?.Type == JTokenType.String && (string)item["type"] == "progress" &&
                                item["stage"]?.Type == JTokenType.String &&
                                item["completed"]?.Type == JTokenType.Integer && item["total"]?.Type == JTokenType.Integer)
                                messages.Enqueue(new Progress { Stage = (string)item["stage"],
                                    Completed = (int?)item["completed"] ?? 0, Total = (int?)item["total"] ?? 0 });
                        }
                        catch (Exception error) when (error is JsonException || error is FormatException ||
                            error is InvalidCastException || error is OverflowException) { }
                    }
                });
                Task stderr = Task.Run(() =>
                {
                    char[] buffer = new char[1024];
                    int count;
                    while ((count = process.StandardError.Read(buffer, 0, buffer.Length)) > 0)
                        if (errors.Length < 8192) errors.Append(buffer, 0, Math.Min(count, 8192 - errors.Length));
                });
                try
                {
                    var timer = Stopwatch.StartNew();
                    while (!process.WaitForExit(100))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (timer.Elapsed > timeout) throw new TimeoutException("STL reduction reached its time limit.");
                        DeliverProgress(messages, progress);
                    }
                    try { Task.WaitAll(stdout, stderr); }
                    catch (AggregateException error) { throw new IOException("Cannot read STL reducer output.", error); }
                    DeliverProgress(messages, progress);
                    cancellation.ThrowIfCancellationRequested();
                    if (process.ExitCode != 0)
                        throw new IOException("The STL reducer exited with code " + process.ExitCode + ": " + errors);
                }
                finally
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        process.WaitForExit();
                    }
                    try { Task.WaitAll(stdout, stderr); }
                    catch (AggregateException) { }
                }
            }
        }

        private static void DeliverProgress(ConcurrentQueue<Progress> messages, Action<Progress> callback)
        {
            Progress latest = null, item;
            while (messages.TryDequeue(out item)) latest = item;
            if (latest != null) callback?.Invoke(latest);
        }

        private static uint ReadCount(Stream stream)
        {
            if (stream.Length < 84) throw new InvalidDataException("Invalid binary STL header.");
            stream.Position = 80;
            var bytes = new byte[4];
            if (stream.Read(bytes, 0, 4) != 4) throw new EndOfStreamException();
            uint count = BitConverter.ToUInt32(bytes, 0);
            if (count == 0 || 84L + 50L * count != stream.Length)
                throw new InvalidDataException("Invalid binary STL triangle count.");
            return count;
        }

        private static string HashFile(string path)
        {
            using (var file = File.OpenRead(path)) return Hash(file);
        }

        private static string Hash(Stream stream)
        {
            stream.Position = 0;
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        private static string Quote(string value) { return "\"" + value.Replace("\"", "\\\"") + "\""; }

        private static StlMeshReducer.Result Copy(StlMeshReducer.Result value)
        {
            return new StlMeshReducer.Result { OriginalTriangles = value.OriginalTriangles,
                FinalTriangles = value.FinalTriangles, TargetTriangles = value.TargetTriangles,
                OriginalBytes = value.OriginalBytes, FinalBytes = value.FinalBytes,
                Status = value.Status, Warning = value.Warning };
        }

        private static void AddCache(string key, byte[] bytes, StlMeshReducer.Result result)
        {
            lock (CacheLock)
            {
                if (Cache.ContainsKey(key)) return;
                while (cacheBytes + bytes.Length > CacheLimit && CacheOrder.Count > 0)
                {
                    string oldest = CacheOrder.Dequeue();
                    cacheBytes -= Cache[oldest].Bytes.Length;
                    Cache.Remove(oldest);
                }
                Cache.Add(key, new CacheEntry { Bytes = bytes, Result = Copy(result) });
                CacheOrder.Enqueue(key);
                cacheBytes += bytes.Length;
            }
        }
    }
}
