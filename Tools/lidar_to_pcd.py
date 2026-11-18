#!/usr/bin/env python3
"""
lidar_to_pcd.py - Automated raw LiDAR ingestion and conversion tool for RadiusXtract.

Converts raw LiDAR point cloud formats (.laz, .las, .ply, .xyz, .pts, .txt) into clean,
origin-shifted binary .pcd (v0.7) files suitable for high-precision geometric radius estimation.

Precision Protection:
  Raw LiDAR coordinates often reside in large georeferenced/UTM coordinate spaces (e.g. 500,000m+).
  Direct truncation to 32-bit floats produces catastrophic precision loss (millimeter rounding jitter).
  This converter computes the 3D centroid in 64-bit float (float64) and shifts the point cloud
  to a local origin BEFORE casting to float32 for binary PCD serialization.
"""

import argparse
import os
import sys
import numpy as np


def parse_args():
    parser = argparse.ArgumentParser(
        description="Convert raw LiDAR files (.laz, .las, .ply, .xyz) to origin-shifted binary PCD v0.7."
    )
    parser.add_argument(
        "-i", "--input", required=True, help="Path to input raw LiDAR point cloud file."
    )
    parser.add_argument(
        "-o", "--output", required=True, help="Path to output .pcd file."
    )
    return parser.parse_args()


def load_las_laz(file_path: str) -> np.ndarray:
    """Reads LAS/LAZ point cloud using laspy with lazrs decompression."""
    try:
        import laspy
    except ImportError:
        raise RuntimeError("laspy is not installed. Please install 'laspy[lazrs]'.")

    try:
        las = laspy.read(file_path)
    except Exception as ex:
        raise RuntimeError(f"Failed to read LAS/LAZ file '{file_path}': {ex}")

    # Extract coordinates in 64-bit float from scaled values
    x = np.asarray(las.x, dtype=np.float64)
    y = np.asarray(las.y, dtype=np.float64)
    z = np.asarray(las.z, dtype=np.float64)

    if len(x) == 0:
        raise ValueError("LAS/LAZ file contains zero points.")

    return np.column_stack((x, y, z))


def load_ply(file_path: str) -> np.ndarray:
    """Reads PLY point cloud using plyfile."""
    try:
        from plyfile import PlyData
    except ImportError:
        raise RuntimeError("plyfile is not installed. Please install 'plyfile'.")

    try:
        ply = PlyData.read(file_path)
    except Exception as ex:
        raise RuntimeError(f"Failed to read PLY file '{file_path}': {ex}")

    if "vertex" not in ply:
        raise ValueError("PLY file does not contain a 'vertex' element.")

    vertex = ply["vertex"]
    for col in ("x", "y", "z"):
        if col not in vertex:
            raise ValueError(f"PLY vertex element missing required '{col}' coordinate property.")

    x = np.asarray(vertex["x"], dtype=np.float64)
    y = np.asarray(vertex["y"], dtype=np.float64)
    z = np.asarray(vertex["z"], dtype=np.float64)

    if len(x) == 0:
        raise ValueError("PLY file contains zero points.")

    return np.column_stack((x, y, z))


def load_ascii_xyz(file_path: str) -> np.ndarray:
    """Reads ASCII XYZ/PTS/TXT point cloud."""
    try:
        # Check first line for delimiters
        with open(file_path, "r", encoding="utf-8", errors="ignore") as f:
            for line in f:
                line = line.strip()
                if not line or line.startswith("#"):
                    continue
                delimiter = "," if "," in line else None
                break

        pts = np.loadtxt(file_path, delimiter=delimiter, usecols=(0, 1, 2), dtype=np.float64)
        if pts.ndim == 1 and len(pts) >= 3:
            pts = pts.reshape(1, -1)[:, :3]
        elif pts.ndim == 2:
            pts = pts[:, :3]
        else:
            raise ValueError("Invalid shape for XYZ points.")

        if len(pts) == 0:
            raise ValueError("ASCII XYZ file contains zero points.")

        return pts
    except Exception as ex:
        raise RuntimeError(f"Failed to parse ASCII XYZ file '{file_path}': {ex}")


def load_e57(file_path: str) -> np.ndarray:
    """Attempts to read E57 point cloud if pye57 is available."""
    try:
        # pyrefly: ignore[missing-import]
        import pye57
    except ImportError:
        raise RuntimeError(
            "E57 support requires the 'pye57' package. Please install pye57 or convert E57 to LAS/LAZ first."
        )

    try:
        e57 = pye57.E57(file_path)
        data = e57.read_scan(0, ignore_missing_fields=True)
        x = np.asarray(data["cartesianX"], dtype=np.float64)
        y = np.asarray(data["cartesianY"], dtype=np.float64)
        z = np.asarray(data["cartesianZ"], dtype=np.float64)
        return np.column_stack((x, y, z))
    except Exception as ex:
        raise RuntimeError(f"Failed to read E57 file '{file_path}': {ex}")


def load_points(file_path: str) -> np.ndarray:
    """Dispatches point cloud loading based on file extension."""
    ext = os.path.splitext(file_path)[1].lower()

    if ext in (".las", ".laz"):
        return load_las_laz(file_path)
    elif ext == ".ply":
        return load_ply(file_path)
    elif ext in (".xyz", ".pts", ".txt", ".csv"):
        return load_ascii_xyz(file_path)
    elif ext == ".e57":
        return load_e57(file_path)
    else:
        raise ValueError(f"Unsupported point cloud format: '{ext}'. Supported formats: .las, .laz, .ply, .xyz, .pts, .txt, .e57")


def write_binary_pcd(points: np.ndarray, output_path: str, centroid: np.ndarray):
    """
    Serializes a numpy Nx3 array of float32 coordinates to standard binary PCD v0.7.
    """
    n_points = len(points)
    
    # Standard PCD v0.7 header
    header = (
        "# .PCD v0.7 - Point Cloud Data file format\n"
        "VERSION 0.7\n"
        "FIELDS x y z\n"
        "SIZE 4 4 4\n"
        "TYPE F F F\n"
        "COUNT 1 1 1\n"
        f"WIDTH {n_points}\n"
        "HEIGHT 1\n"
        "VIEWPOINT 0 0 0 1 0 0 0\n"
        f"POINTS {n_points}\n"
        "DATA binary\n"
    )

    # Ensure parent output directory exists
    os.makedirs(os.path.dirname(os.path.abspath(output_path)), exist_ok=True)

    with open(output_path, "wb") as f:
        f.write(header.encode("ascii"))
        # Ensure contiguous float32 binary buffer
        f32_points = np.ascontiguousarray(points, dtype=np.float32)
        f32_points.tofile(f)


def main():
    args = parse_args()
    input_path = os.path.abspath(args.input)
    output_path = os.path.abspath(args.output)

    if not os.path.exists(input_path):
        sys.stderr.write(f"Error: Input file does not exist: '{input_path}'\n")
        sys.exit(1)

    try:
        # 1. Load points with 64-bit precision
        pts_f64 = load_points(input_path)
        total_pts = len(pts_f64)

        if total_pts == 0:
            sys.stderr.write("Error: Point cloud contains zero points.\n")
            sys.exit(1)

        # 2. Critical Precision Protection: Compute centroid in float64 and shift to local origin
        centroid = np.mean(pts_f64, axis=0, dtype=np.float64)
        pts_shifted = pts_f64 - centroid

        # 3. Serialize to binary PCD v0.7
        write_binary_pcd(pts_shifted, output_path, centroid)

        # Report conversion summary to stdout
        sys.stdout.write(
            f"SUCCESS: Converted {total_pts:,} points from '{os.path.basename(input_path)}' to '{os.path.basename(output_path)}'.\n"
        )
        sys.stdout.write(
            f"Origin Shift (Centroid subtracted): [{centroid[0]:.6f}, {centroid[1]:.6f}, {centroid[2]:.6f}]\n"
        )
        sys.stdout.flush()
        sys.exit(0)

    except Exception as ex:
        sys.stderr.write(f"Conversion Error: {ex}\n")
        sys.stderr.flush()
        sys.exit(1)


if __name__ == "__main__":
    main()
