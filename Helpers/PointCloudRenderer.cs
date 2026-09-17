using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace RadiusXtractUI.Helpers;

public class PointCloudRenderer
{
    private List<Vector3> _points = new List<Vector3>();
    private WriteableBitmap _bitmap;
    private byte[] _pixelData;
    private int _width;
    private int _height;

    // Camera/Transform
    private float _scale = 1.0f;
    private float _offsetX = 0;
    private float _offsetY = 0;
    private float _yaw = 0; // rotation around Y
    private float _pitch = 0; // rotation around X

    private Vector3 _center;
    private float _modelRadius = 1.0f;
    
    // Display properties
    public float PointSize { get; set; } = 2.4f;
    public string ColorMode { get; set; } = "axis"; // axis, depth, solid
    public bool ShowOutliers { get; set; }
    public bool ShowOverlay { get; set; } = true;

    // Fitted Cylinder Properties
    public Vector3 FittedAxis { get; set; } = Vector3.UnitZ;
    public float FittedRadius { get; set; } = 0.208f;
    public float FittedLength { get; set; } = 0.6f;
    public Vector3 FittedCenter { get; set; } = Vector3.Zero;
    public float FittedRms { get; set; } = 0.01f;

    public PointCloudRenderer(int width, int height)
    {
        Resize(width, height);
    }

    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        _width = width;
        _height = height;
        _bitmap = new WriteableBitmap(width, height);
        _pixelData = new byte[width * height * 4];
    }

    public WriteableBitmap GetBitmap() => _bitmap;

    public void LoadPoints(List<Vector3> points)
    {
        _points = points;
        
        if (_points.Count == 0) return;

        // Calculate bounding box
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

        foreach (var p in _points)
        {
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Z < minZ) minZ = p.Z;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
            if (p.Z > maxZ) maxZ = p.Z;
        }

        _center = new Vector3((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2);
        FittedCenter = _center;
        
        float dx = maxX - minX;
        float dy = maxY - minY;
        float dz = maxZ - minZ;
        
        _modelRadius = Math.Max(dx, Math.Max(dy, dz)) / 2;
        if (_modelRadius < 0.001f) _modelRadius = 1.0f;
        
        FittedLength = Math.Max(dx, Math.Max(dy, dz));
        FittedRadius = _modelRadius * 0.5f;

        ResetView();
    }

    public void ResetView()
    {
        _yaw = 0;
        _pitch = 0;
        _offsetX = _width / 2f;
        _offsetY = _height / 2f;
        
        // Fit to screen
        float minDim = Math.Min(_width, _height);
        _scale = (minDim * 0.4f) / _modelRadius;
        
        Render();
    }

    public void Rotate(float deltaX, float deltaY)
    {
        _yaw += deltaX * 0.01f;
        _pitch += deltaY * 0.01f;
        Render();
    }

    public void Pan(float deltaX, float deltaY)
    {
        _offsetX += deltaX;
        _offsetY += deltaY;
        Render();
    }

    public void Zoom(float deltaZoom, float centerX, float centerY)
    {
        float zoomFactor = deltaZoom > 0 ? 1.1f : 0.9f;
        
        // Adjust offset so we zoom towards the mouse cursor
        _offsetX = centerX + (_offsetX - centerX) * zoomFactor;
        _offsetY = centerY + (_offsetY - centerY) * zoomFactor;
        
        _scale *= zoomFactor;
        Render();
    }

    public void Render()
    {
        if (_bitmap == null || _points.Count == 0) return;

        // Background color
        byte bgR = 21, bgG = 28, bgB = 35;
        for (int i = 0; i < _pixelData.Length; i += 4)
        {
            _pixelData[i] = bgB;
            _pixelData[i + 1] = bgG;
            _pixelData[i + 2] = bgR;
            _pixelData[i + 3] = 255;
        }

        // Transformation matrix
        var rotX = Matrix4x4.CreateRotationX(_pitch);
        var rotY = Matrix4x4.CreateRotationY(_yaw);
        var transform = rotX * rotY;

        int pSize = Math.Max(1, (int)PointSize);
        int halfSize = pSize / 2;

        // Residual threshold for academic outlier detection (Rusu et al. 2008 & Schnabel et al. 2007)
        Vector3 axisDir = FittedAxis.LengthSquared() > 0.001f ? Vector3.Normalize(FittedAxis) : Vector3.UnitZ;
        float outlierTol = FittedRms > 0.0001f ? (float)Math.Max(FittedRms * 2.5f, FittedRadius * 0.15f) : (float)Math.Max(0.015f, FittedRadius * 0.15f);

        // Render Point Cloud
        foreach (var point in _points)
        {
            var p = point - _center;
            var rotated = Vector3.Transform(p, transform);
            
            float screenX = rotated.X * _scale + _offsetX;
            float screenY = rotated.Y * _scale + _offsetY;

            int px = (int)screenX;
            int py = (int)screenY;

            if (px >= halfSize && px < _width - halfSize && py >= halfSize && py < _height - halfSize)
            {
                byte r = 255, g = 255, b = 255;

                if (ColorMode == "axis")
                {
                    var normalized = p / _modelRadius;
                    r = (byte)Math.Clamp((normalized.X + 1f) * 127f, 0, 255);
                    g = (byte)Math.Clamp((normalized.Y + 1f) * 127f, 0, 255);
                    b = (byte)Math.Clamp((normalized.Z + 1f) * 127f, 0, 255);
                }
                else if (ColorMode == "depth")
                {
                    float depth = (rotated.Z / _modelRadius + 1f) / 2f;
                    byte c = (byte)Math.Clamp(depth * 255, 50, 255);
                    r = g = b = c;
                }
                else // solid
                {
                    r = 61; g = 199; b = 235;
                }

                if (ShowOutliers)
                {
                    // Exact point-to-cylinder axis distance residual test (Schnabel et al. 2007)
                    var relP = point - FittedCenter;
                    float distAxis = Vector3.Cross(relP, axisDir).Length();
                    float radialError = Math.Abs(distAxis - FittedRadius);

                    if (radialError > outlierTol)
                    {
                        r = 255; g = 80; b = 80; // Bright Red for Outliers
                    }
                }

                // Draw point
                for (int dy = -halfSize; dy <= halfSize; dy++)
                {
                    for (int dx = -halfSize; dx <= halfSize; dx++)
                    {
                        int targetX = px + dx;
                        int targetY = py + dy;
                        int idx = (targetY * _width + targetX) * 4;
                        
                        _pixelData[idx] = b;
                        _pixelData[idx + 1] = g;
                        _pixelData[idx + 2] = r;
                        _pixelData[idx + 3] = 255;
                    }
                }
            }
        }

        // Render 3D Fitted Cylinder Overlay
        if (ShowOverlay)
        {
            RenderCylinderOverlay(transform, axisDir);
        }

        // Copy to bitmap
        _pixelData.CopyTo(_bitmap.PixelBuffer);
        _bitmap.Invalidate();
    }

    private void RenderCylinderOverlay(Matrix4x4 transform, Vector3 axisDir)
    {
        // Construct basis for cylinder cross-section rings
        Vector3 u = Vector3.Cross(axisDir, Math.Abs(axisDir.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX);
        u = Vector3.Normalize(u);
        Vector3 v = Vector3.Cross(axisDir, u);

        float halfLen = FittedLength * 0.5f;
        int ringSegments = 24;
        byte ringR = 61, ringG = 199, ringB = 235; // Cyan
        byte axisR = 111, axisG = 224, axisB = 163; // Green

        // Draw Rings at bottom, middle, and top along cylinder axis
        float[] zOffsets = new float[] { -halfLen, 0, halfLen };

        Vector2[][] ringPoints2D = new Vector2[zOffsets.Length][];

        for (int rIdx = 0; rIdx < zOffsets.Length; rIdx++)
        {
            float zOff = zOffsets[rIdx];
            Vector3 ringCenter3D = FittedCenter + axisDir * zOff;
            ringPoints2D[rIdx] = new Vector2[ringSegments];

            for (int s = 0; s < ringSegments; s++)
            {
                double angle = s * (2.0 * Math.PI / ringSegments);
                float cos = (float)Math.Cos(angle);
                float sin = (float)Math.Sin(angle);

                Vector3 p3d = ringCenter3D + (u * cos + v * sin) * FittedRadius - _center;
                Vector3 rotP = Vector3.Transform(p3d, transform);

                float sx = rotP.X * _scale + _offsetX;
                float sy = rotP.Y * _scale + _offsetY;

                ringPoints2D[rIdx][s] = new Vector2(sx, sy);
            }

            // Draw ring circle edges
            for (int s = 0; s < ringSegments; s++)
            {
                var p1 = ringPoints2D[rIdx][s];
                var p2 = ringPoints2D[rIdx][(s + 1) % ringSegments];
                DrawLine((int)p1.X, (int)p1.Y, (int)p2.X, (int)p2.Y, ringR, ringG, ringB);
            }
        }

        // Draw longitudinal cylinder side lines
        for (int s = 0; s < ringSegments; s += 6)
        {
            var pBottom = ringPoints2D[0][s];
            var pTop = ringPoints2D[2][s];
            DrawLine((int)pBottom.X, (int)pBottom.Y, (int)pTop.X, (int)pTop.Y, ringR, ringG, ringB);
        }

        // Draw Central Axis Line
        Vector3 axisStart3D = FittedCenter - axisDir * (halfLen * 1.15f) - _center;
        Vector3 axisEnd3D = FittedCenter + axisDir * (halfLen * 1.15f) - _center;

        Vector3 rotAxisStart = Vector3.Transform(axisStart3D, transform);
        Vector3 rotAxisEnd = Vector3.Transform(axisEnd3D, transform);

        float ax1 = rotAxisStart.X * _scale + _offsetX;
        float ay1 = rotAxisStart.Y * _scale + _offsetY;
        float ax2 = rotAxisEnd.X * _scale + _offsetX;
        float ay2 = rotAxisEnd.Y * _scale + _offsetY;

        DrawLine((int)ax1, (int)ay1, (int)ax2, (int)ay2, axisR, axisG, axisB);
    }

    private void DrawLine(int x0, int y0, int x1, int y1, byte r, byte g, byte b)
    {
        int dx = Math.Abs(x1 - x0);
        int dy = Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;

        while (true)
        {
            if (x0 >= 0 && x0 < _width && y0 >= 0 && y0 < _height)
            {
                int idx = (y0 * _width + x0) * 4;
                _pixelData[idx] = b;
                _pixelData[idx + 1] = g;
                _pixelData[idx + 2] = r;
                _pixelData[idx + 3] = 255;
            }

            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 < dx) { err += dx; y0 += sy; }
        }
    }
}
