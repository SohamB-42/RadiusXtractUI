using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RadiusXtractUI.Services;

/// <summary>
/// Result of a LiDAR to PCD conversion operation.
/// </summary>
public sealed class LidarConversionResult
{
    public bool Success { get; init; }
    public string ConvertedPcdPath { get; init; } = string.Empty;
    public string OriginalFilePath { get; init; } = string.Empty;
    public bool WasConverted { get; init; }
    public string? ErrorMessage { get; init; }
    public string? OutputLog { get; init; }
}

/// <summary>
/// Automated service for raw LiDAR ingestion and conversion into origin-shifted binary PCD files.
/// </summary>
public sealed class LidarConversionService : IDisposable
{
    private static readonly Lazy<LidarConversionService> _instance = new(() => new LidarConversionService());
    public static LidarConversionService Instance => _instance.Value;

    private readonly ConcurrentBag<string> _tempFiles = new();
    private readonly string _tempDirectory;
    private bool _disposed;

    private static readonly HashSet<string> RawLidarExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".laz", ".las", ".ply", ".xyz", ".pts", ".txt", ".csv", ".e57"
    };

    private LidarConversionService()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "RadiusXtract_LiDAR");
        try
        {
            if (!Directory.Exists(_tempDirectory))
            {
                Directory.CreateDirectory(_tempDirectory);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LidarConversionService] Failed to create temp dir: {ex.Message}");
        }

        AppDomain.CurrentDomain.ProcessExit += (s, e) => Cleanup();
    }

    /// <summary>
    /// Checks whether the given file requires conversion before entering the PCD radius estimation pipeline.
    /// </summary>
    public bool RequiresConversion(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        string ext = Path.GetExtension(filePath);
        return RawLidarExtensions.Contains(ext);
    }

    /// <summary>
    /// Checks whether the given file format is supported by the ingestion pipeline (.pcd or raw LiDAR).
    /// </summary>
    public bool IsSupportedFormat(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        string ext = Path.GetExtension(filePath);
        return ext.Equals(".pcd", StringComparison.OrdinalIgnoreCase) || RawLidarExtensions.Contains(ext);
    }

    /// <summary>
    /// Converts a raw LiDAR file (.laz, .las, .ply, etc.) to a clean, origin-shifted binary .pcd file asynchronously.
    /// If the file is already a .pcd file, it passes through untouched without overhead.
    /// </summary>
    public async Task<LidarConversionResult> ConvertToPcdAsync(
        string inputFilePath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(inputFilePath))
        {
            return new LidarConversionResult
            {
                Success = false,
                OriginalFilePath = inputFilePath ?? string.Empty,
                ErrorMessage = "File path cannot be null or empty."
            };
        }

        if (!File.Exists(inputFilePath))
        {
            return new LidarConversionResult
            {
                Success = false,
                OriginalFilePath = inputFilePath,
                ErrorMessage = $"File does not exist: '{inputFilePath}'"
            };
        }

        string ext = Path.GetExtension(inputFilePath);

        // Passthrough for native .pcd files
        if (ext.Equals(".pcd", StringComparison.OrdinalIgnoreCase))
        {
            return new LidarConversionResult
            {
                Success = true,
                ConvertedPcdPath = inputFilePath,
                OriginalFilePath = inputFilePath,
                WasConverted = false
            };
        }

        if (!RawLidarExtensions.Contains(ext))
        {
            return new LidarConversionResult
            {
                Success = false,
                OriginalFilePath = inputFilePath,
                ErrorMessage = $"Unsupported LiDAR format '{ext}'. Supported formats: .pcd, .laz, .las, .ply, .xyz, .pts, .txt, .e57"
            };
        }

        progress?.Report($"Converting raw LiDAR ({ext.ToUpperInvariant()}) to origin-shifted PCD...");

        // Generate clean temporary destination path
        string safeFileName = Path.GetFileNameWithoutExtension(inputFilePath);
        string outputPcdPath = Path.Combine(_tempDirectory, $"{safeFileName}_{Guid.NewGuid():N}.pcd");

        // Resolve converter binary or Python script
        (string executable, string arguments) = ResolveConverterExecution(inputFilePath, outputPcdPath);

        try
        {
            var result = await RunConverterProcessAsync(executable, arguments, inputFilePath, outputPcdPath, progress, cancellationToken);
            if (result.Success && File.Exists(outputPcdPath))
            {
                _tempFiles.Add(outputPcdPath);
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            if (File.Exists(outputPcdPath))
            {
                try { File.Delete(outputPcdPath); } catch { /* ignore */ }
            }
            return new LidarConversionResult
            {
                Success = false,
                OriginalFilePath = inputFilePath,
                ErrorMessage = "Conversion operation was cancelled."
            };
        }
        catch (Exception ex)
        {
            return new LidarConversionResult
            {
                Success = false,
                OriginalFilePath = inputFilePath,
                ErrorMessage = $"Conversion process failed: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Resolves whether to use the bundled lidar_to_pcd.exe or fall back to python lidar_to_pcd.py.
    /// </summary>
    private (string Executable, string Arguments) ResolveConverterExecution(string inputPath, string outputPath)
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;

        // 1. Check for bundled executable in output or Tools folder
        string[] exeCandidates =
        {
            Path.Combine(baseDir, "Tools", "lidar_to_pcd.exe"),
            Path.Combine(baseDir, "lidar_to_pcd.exe"),
            Path.Combine(baseDir, "Assets", "Tools", "lidar_to_pcd.exe"),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Tools", "lidar_to_pcd.exe"))
        };

        foreach (var candidate in exeCandidates)
        {
            if (File.Exists(candidate))
            {
                return (candidate, $"--input \"{inputPath}\" --output \"{outputPath}\"");
            }
        }

        // 2. Fallback to Python script
        string[] pyCandidates =
        {
            Path.Combine(baseDir, "Tools", "lidar_to_pcd.py"),
            Path.Combine(baseDir, "lidar_to_pcd.py"),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Tools", "lidar_to_pcd.py"))
        };

        string? scriptPath = pyCandidates.FirstOrDefault(File.Exists);
        if (scriptPath != null)
        {
            return ("python", $"\"{scriptPath}\" --input \"{inputPath}\" --output \"{outputPath}\"");
        }

        throw new FileNotFoundException(
            "LiDAR conversion sidecar not found. Please ensure 'lidar_to_pcd.exe' or 'Tools/lidar_to_pcd.py' is bundled with the application."
        );
    }

    /// <summary>
    /// Executes the conversion process asynchronously without blocking the UI thread.
    /// </summary>
    private static async Task<LidarConversionResult> RunConverterProcessAsync(
        string executable,
        string arguments,
        string inputPath,
        string outputPath,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        var stdoutBuilder = new System.Text.StringBuilder();
        var stderrBuilder = new System.Text.StringBuilder();

        process.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                stdoutBuilder.AppendLine(e.Data);
                if (e.Data.StartsWith("SUCCESS:", StringComparison.OrdinalIgnoreCase))
                {
                    progress?.Report(e.Data);
                }
            }
        };

        process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                stderrBuilder.AppendLine(e.Data);
            }
        };

        if (!process.Start())
        {
            return new LidarConversionResult
            {
                Success = false,
                OriginalFilePath = inputPath,
                ErrorMessage = $"Failed to start converter process '{executable}'."
            };
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);

        string stdout = stdoutBuilder.ToString();
        string stderr = stderrBuilder.ToString();

        if (process.ExitCode != 0)
        {
            string err = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            if (string.IsNullOrWhiteSpace(err))
            {
                err = $"Converter exited with code {process.ExitCode}.";
            }

            return new LidarConversionResult
            {
                Success = false,
                OriginalFilePath = inputPath,
                ErrorMessage = err.Trim(),
                OutputLog = stdout
            };
        }

        if (!File.Exists(outputPath))
        {
            return new LidarConversionResult
            {
                Success = false,
                OriginalFilePath = inputPath,
                ErrorMessage = "Converter reported success, but the output .pcd file was not generated.",
                OutputLog = stdout
            };
        }

        return new LidarConversionResult
        {
            Success = true,
            ConvertedPcdPath = outputPath,
            OriginalFilePath = inputPath,
            WasConverted = true,
            OutputLog = stdout
        };
    }

    /// <summary>
    /// Cleans up temporary PCD files generated during raw LiDAR conversion.
    /// </summary>
    public void Cleanup()
    {
        while (_tempFiles.TryTake(out var tempFile))
        {
            try
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LidarConversionService] Failed to delete temp file '{tempFile}': {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Cleanup();
        GC.SuppressFinalize(this);
    }
}
