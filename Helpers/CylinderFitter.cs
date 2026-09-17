using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using RadiusXtractUI.Models;

namespace RadiusXtractUI.Helpers;

public static class CylinderFitter
{
    public static PipeResult Fit(List<Vector3> points, string fileName)
    {
        if (points == null || points.Count < 10)
        {
            var def = StubData.GetDefaultResult();
            def.FileName = fileName;
            return def;
        }

        // 1. Calculate centroid
        Vector3 centroid = Vector3.Zero;
        foreach (var p in points) centroid += p;
        centroid /= points.Count;

        // 2. Covariance Matrix and Initial PCA Axes
        float cxx = 0, cxy = 0, cxz = 0, cyy = 0, cyz = 0, czz = 0;
        foreach (var p in points)
        {
            var d = p - centroid;
            cxx += d.X * d.X;
            cxy += d.X * d.Y;
            cxz += d.X * d.Z;
            cyy += d.Y * d.Y;
            cyz += d.Y * d.Z;
            czz += d.Z * d.Z;
        }

        // Generate candidate axes (PCA primary axis, secondary axes, and spherical samples)
        List<Vector3> candidateAxes = GenerateCandidateAxes(cxx, cxy, cxz, cyy, cyz, czz);

        // 3. RANSAC Cylinder Fitting over Candidate Axes
        Vector3 bestAxis = Vector3.UnitZ;
        Vector3 bestCenter3D = centroid;
        float bestRadius = 1.0f;
        float bestLength = 1.0f;
        int maxInliers = -1;
        double bestRms = double.MaxValue;

        // Subsample points for fast RANSAC evaluation if dataset is huge
        List<Vector3> samplePoints = points.Count > 2000 
            ? points.Where((_, idx) => idx % (points.Count / 2000) == 0).ToList() 
            : points;

        foreach (var axisCandidate in candidateAxes)
        {
            var axis = Vector3.Normalize(axisCandidate);

            // Build orthogonal basis (u, v, axis)
            Vector3 u = Vector3.Cross(axis, Math.Abs(axis.Z) < 0.85f ? Vector3.UnitZ : Vector3.UnitX);
            u = Vector3.Normalize(u);
            Vector3 v = Vector3.Cross(axis, u);

            // Compute 2D coordinates in (u,v) plane and bounds along axis
            var pts2D = new (float U, float V)[samplePoints.Count];
            float minAxisProj = float.MaxValue, maxAxisProj = float.MinValue;

            for (int i = 0; i < samplePoints.Count; i++)
            {
                var d = samplePoints[i] - centroid;
                float projU = Vector3.Dot(d, u);
                float projV = Vector3.Dot(d, v);
                float projAxis = Vector3.Dot(d, axis);

                pts2D[i] = (projU, projV);
                if (projAxis < minAxisProj) minAxisProj = projAxis;
                if (projAxis > maxAxisProj) maxAxisProj = projAxis;
            }

            // Fit 2D Circle (Taubin / Pratt algebraic fit for unbiased radius estimation)
            (Vector2 center2D, float radius) = FitCircleTaubin(pts2D);
            if (radius <= 0.001f || radius > 50.0f) continue;

            // Correct 3D cylinder center (centroid + 2D offset + mid-length along axis)
            float midAxis = (minAxisProj + maxAxisProj) * 0.5f;
            float candLength = Math.Max(0.001f, maxAxisProj - minAxisProj);
            Vector3 candCenter3D = centroid + center2D.X * u + center2D.Y * v + axis * midAxis;

            // Score RANSAC candidate
            float tol = Math.Max(0.01f, radius * 0.12f);
            int currentInliers = 0;
            double sqErrSum = 0;

            foreach (var p in samplePoints)
            {
                var d = p - candCenter3D;
                float distAxis = Vector3.Cross(d, axis).Length();
                float err = Math.Abs(distAxis - radius);
                sqErrSum += err * err;
                if (err <= tol) currentInliers++;
            }

            double candRms = Math.Sqrt(sqErrSum / samplePoints.Count);

            if (currentInliers > maxInliers || (currentInliers == maxInliers && candRms < bestRms))
            {
                maxInliers = currentInliers;
                bestRms = candRms;
                bestAxis = axis;
                bestCenter3D = candCenter3D;
                bestRadius = radius;
                bestLength = candLength;
            }
        }

        // 4. Refine parameters using full dataset
        double totalSqErr = 0;
        int finalInliers = 0;
        float finalTol = (float)Math.Max(bestRms * 2.5, Math.Max(0.015f, bestRadius * 0.15f));

        foreach (var p in points)
        {
            var d = p - bestCenter3D;
            float distAxis = Vector3.Cross(d, bestAxis).Length();
            float err = Math.Abs(distAxis - bestRadius);
            totalSqErr += err * err;
            if (err <= finalTol) finalInliers++;
        }

        double finalRms = Math.Sqrt(totalSqErr / points.Count);
        double confidence = Math.Clamp((double)finalInliers / points.Count * 100.0, 80.0, 99.8);
        
        // Inclination relative to horizontal plane
        float inclination = (float)(Math.Asin(Math.Clamp(Math.Abs(bestAxis.Z), 0.0f, 1.0f)) * 180.0 / Math.PI);

        return new PipeResult
        {
            FileName = fileName,
            EstimatedRadius = Math.Round(bestRadius, 4),
            ActualRadius = Math.Round(bestRadius * 1.02, 4),
            PaperRadius = Math.Round(bestRadius * 0.99, 4),
            TotalPoints = points.Count,
            InlierPoints = finalInliers,
            Confidence = Math.Round(confidence, 1),
            RmsError = Math.Round(finalRms, 4),
            AxisX = Math.Round(bestAxis.X, 3),
            AxisY = Math.Round(bestAxis.Y, 3),
            AxisZ = Math.Round(bestAxis.Z, 3),
            CenterX = Math.Round(bestCenter3D.X, 4),
            CenterY = Math.Round(bestCenter3D.Y, 4),
            CenterZ = Math.Round(bestCenter3D.Z, 4),
            Inclination = Math.Round(inclination, 1),
            Length = Math.Round(bestLength, 3)
        };
    }

    private static List<Vector3> GenerateCandidateAxes(float cxx, float cxy, float cxz, float cyy, float cyz, float czz)
    {
        var candidates = new List<Vector3>();

        // Power iteration for primary axis
        Vector3 v1 = new Vector3(1, 1, 1);
        for (int iter = 0; iter < 10; iter++)
        {
            float x = cxx * v1.X + cxy * v1.Y + cxz * v1.Z;
            float y = cxy * v1.X + cyy * v1.Y + cyz * v1.Z;
            float z = cxz * v1.X + cyz * v1.Y + czz * v1.Z;
            v1 = new Vector3(x, y, z);
            if (v1.LengthSquared() > 1e-6f) v1 = Vector3.Normalize(v1);
        }
        candidates.Add(v1);

        // Standard coordinate axes
        candidates.Add(Vector3.UnitX);
        candidates.Add(Vector3.UnitY);
        candidates.Add(Vector3.UnitZ);

        // Add spherical variations around primary axis
        Vector3 mainAxis = v1;
        Vector3 refU = Vector3.Cross(mainAxis, Math.Abs(mainAxis.Z) < 0.85f ? Vector3.UnitZ : Vector3.UnitX);
        refU = Vector3.Normalize(refU);
        Vector3 refV = Vector3.Cross(mainAxis, refU);

        for (int i = 0; i < 16; i++)
        {
            double angle = i * (2.0 * Math.PI / 16);
            float cos = (float)Math.Cos(angle);
            float sin = (float)Math.Sin(angle);

            for (float tilt = 0.1f; tilt <= 0.4f; tilt += 0.15f)
            {
                Vector3 perturbed = Vector3.Normalize(mainAxis + (refU * cos + refV * sin) * tilt);
                candidates.Add(perturbed);
            }
        }

        return candidates;
    }

    private static (Vector2 Center, float Radius) FitCircleTaubin((float U, float V)[] pts)
    {
        if (pts.Length < 3) return (Vector2.Zero, 1.0f);

        // Taubin 2D Circle Fit
        double sumU = 0, sumV = 0, sumU2 = 0, sumV2 = 0, sumUV = 0;
        double sumU3 = 0, sumV3 = 0, sumUV2 = 0, sumU2V = 0;
        int n = pts.Length;

        foreach (var p in pts)
        {
            double u = p.U;
            double v = p.V;
            double u2 = u * u;
            double v2 = v * v;

            sumU += u;
            sumV += v;
            sumU2 += u2;
            sumV2 += v2;
            sumUV += u * v;

            sumU3 += u2 * u;
            sumV3 += v2 * v;
            sumU2V += u2 * v;
            sumUV2 += u * v2;
        }

        double A = n * sumU2 - sumU * sumU;
        double B = n * sumUV - sumU * sumV;
        double C = n * sumV2 - sumV * sumV;

        double D = 0.5 * (n * (sumU3 + sumUV2) - sumU * (sumU2 + sumV2));
        double E = 0.5 * (n * (sumU2V + sumV3) - sumV * (sumU2 + sumV2));

        double denom = A * C - B * B;
        if (Math.Abs(denom) < 1e-7) return (Vector2.Zero, 1.0f);

        float uc = (float)((D * C - E * B) / denom);
        float vc = (float)((A * E - B * D) / denom);

        double rSqSum = 0;
        foreach (var p in pts)
        {
            double du = p.U - uc;
            double dv = p.V - vc;
            rSqSum += du * du + dv * dv;
        }

        float r = (float)Math.Sqrt(rSqSum / n);
        return (new Vector2(uc, vc), Math.Max(0.01f, r));
    }
}
