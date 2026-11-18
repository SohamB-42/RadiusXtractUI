using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace RadiusXtractUI.Helpers;

public sealed class DeepSegEngine : IDisposable
{
    private static readonly Lazy<DeepSegEngine> _instance = new(() => new DeepSegEngine());
    public static DeepSegEngine Instance => _instance.Value;

    private InferenceSession? _session;
    private readonly object _lock = new();
    private bool _isInitialized;
    private bool _usedDirectML;

    public bool IsInitialized => _isInitialized;
    public bool UsedDirectML => _usedDirectML;

    private DeepSegEngine()
    {
    }

    /// <summary>
    /// Initializes the ONNX Runtime session using DirectML GPU acceleration with CPU fallback.
    /// </summary>
    public void Initialize(string? modelPath = null)
    {
        lock (_lock)
        {
            if (_isInitialized && _session != null) return;

            modelPath ??= Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Models", "pointnet2_ssg.onnx");
            if (!File.Exists(modelPath))
            {
                // Fallback check in source tree if running in dev environment
                string altPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Assets", "Models", "pointnet2_ssg.onnx");
                if (File.Exists(altPath))
                {
                    modelPath = Path.GetFullPath(altPath);
                }
                else
                {
                    throw new FileNotFoundException($"PointNet++ ONNX model not found at '{modelPath}'. Please ensure the model is exported to Assets/Models/pointnet2_ssg.onnx.");
                }
            }

            // Attempt 1: Try DirectML for GPU acceleration
            try
            {
                var dmlOptions = new SessionOptions();
                dmlOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
                dmlOptions.AppendExecutionProvider_DML(deviceId: 0);
                _session = new InferenceSession(modelPath, dmlOptions);
                _usedDirectML = true;
                _isInitialized = true;
                return;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DeepSegEngine] DirectML initialization failed ({ex.Message}), falling back to CPU.");
            }

            // Attempt 2: Fallback to CPU execution provider
            var cpuOptions = new SessionOptions();
            cpuOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            _session = new InferenceSession(modelPath, cpuOptions);
            _usedDirectML = false;
            _isInitialized = true;
        }
    }

    /// <summary>
    /// Predicts pointwise binary labels for a 3D point cloud:
    /// 0 = Background / Structural Clutter
    /// 1 = Pipe Surface
    /// </summary>
    public async Task<int[]> PredictLabelsAsync(
        IReadOnlyList<Vector3> points,
        float cellSize = 0.5f,
        int maxPoints = 2048,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (points == null || points.Count == 0)
            return Array.Empty<int>();

        if (!_isInitialized)
        {
            Initialize();
        }

        return await Task.Run(() =>
        {
            int n = points.Count;
            int[] labels = new int[n];

            // 1. Compute bounding box origin
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var p = points[i];
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Z < minZ) minZ = p.Z;
            }
            Vector3 origin = new(minX, minY, minZ);

            // 2. Partition into 3D spatial cells of size cellSize (0.5m)
            var cellIndices = new Dictionary<(long X, long Y, long Z), List<int>>();
            for (int i = 0; i < n; i++)
            {
                var rel = points[i] - origin;
                long cx = (long)Math.Floor(rel.X / cellSize);
                long cy = (long)Math.Floor(rel.Y / cellSize);
                long cz = (long)Math.Floor(rel.Z / cellSize);
                var key = (cx, cy, cz);

                if (!cellIndices.TryGetValue(key, out var list))
                {
                    list = new List<int>();
                    cellIndices[key] = list;
                }
                list.Add(i);
            }

            var cellList = cellIndices.Values.ToList();
            int totalCells = cellList.Count;
            int processedCells = 0;

            // Allocate reusable buffer for a 2048-point chunk
            float[] inputBuffer = new float[1 * maxPoints * 3];
            var tensor = new DenseTensor<float>(inputBuffer.AsMemory(), new[] { 1, maxPoints, 3 });

            // 3. Process each spatial cell
            foreach (var indices in cellList)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int m = indices.Count;
                if (m == 0) continue;

                // Compute cell centroid
                Vector3 centroid = Vector3.Zero;
                for (int j = 0; j < m; j++)
                {
                    centroid += points[indices[j]];
                }
                centroid /= m;

                // Compute max Euclidean distance from centroid (scale)
                float maxDist = 0f;
                for (int j = 0; j < m; j++)
                {
                    float dist = Vector3.Distance(points[indices[j]], centroid);
                    if (dist > maxDist) maxDist = dist;
                }
                float scale = Math.Max(maxDist, 1e-6f);

                // Process cell points in chunks of maxPoints (2048)
                for (int start = 0; start < m; start += maxPoints)
                {
                    int chunkSize = Math.Min(maxPoints, m - start);

                    // Fill 2048-point buffer with normalized coordinates
                    // If chunkSize < 2048, cyclically repeat indices
                    for (int k = 0; k < maxPoints; k++)
                    {
                        int srcIndex = indices[start + (k % chunkSize)];
                        var norm = (points[srcIndex] - centroid) / scale;

                        inputBuffer[k * 3 + 0] = norm.X;
                        inputBuffer[k * 3 + 1] = norm.Y;
                        inputBuffer[k * 3 + 2] = norm.Z;
                    }

                    // Run inference session
                    var inputs = new List<NamedOnnxValue>
                    {
                        NamedOnnxValue.CreateFromTensor("points", tensor)
                    };

                    using var outputs = _session!.Run(inputs);
                    var outputTensor = outputs.First().AsTensor<float>();

                    // Extract predictions only for the valid points in this chunk
                    for (int k = 0; k < chunkSize; k++)
                    {
                        float logit0 = outputTensor[0, k, 0];
                        float logit1 = outputTensor[0, k, 1];
                        int origIndex = indices[start + k];
                        labels[origIndex] = (logit1 > logit0) ? 1 : 0;
                    }
                }

                processedCells++;
                progress?.Report((double)processedCells / totalCells);
            }

            return labels;
        }, cancellationToken);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _session?.Dispose();
            _session = null;
            _isInitialized = false;
        }
    }
}

