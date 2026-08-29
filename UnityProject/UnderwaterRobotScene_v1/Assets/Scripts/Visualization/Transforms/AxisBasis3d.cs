using System;
using UnderwaterRobotScene.Visualization.Data;

namespace UnderwaterRobotScene.Visualization.Transforms
{
    public readonly struct AxisBasis3d
    {
        private const double ValidationTolerance = 1e-9;

        public AxisBasis3d(
            Vector3d sourceXInTarget,
            Vector3d sourceYInTarget,
            Vector3d sourceZInTarget)
        {
            SourceXInTarget = sourceXInTarget;
            SourceYInTarget = sourceYInTarget;
            SourceZInTarget = sourceZInTarget;
        }

        public static AxisBasis3d Identity => new AxisBasis3d(
            new Vector3d(1.0, 0.0, 0.0),
            new Vector3d(0.0, 1.0, 0.0),
            new Vector3d(0.0, 0.0, 1.0));

        public Vector3d SourceXInTarget { get; }
        public Vector3d SourceYInTarget { get; }
        public Vector3d SourceZInTarget { get; }

        public double Determinant => VectorMath3d.Dot(
            SourceXInTarget,
            VectorMath3d.Cross(SourceYInTarget, SourceZInTarget));

        public int Handedness
        {
            get
            {
                double determinant = Determinant;
                if (Math.Abs(determinant - 1.0) <= ValidationTolerance) return 1;
                if (Math.Abs(determinant + 1.0) <= ValidationTolerance) return -1;
                return 0;
            }
        }

        public bool IsValid =>
            SourceXInTarget.IsFinite &&
            SourceYInTarget.IsFinite &&
            SourceZInTarget.IsFinite &&
            Math.Abs(VectorMath3d.LengthSquared(SourceXInTarget) - 1.0) <= ValidationTolerance &&
            Math.Abs(VectorMath3d.LengthSquared(SourceYInTarget) - 1.0) <= ValidationTolerance &&
            Math.Abs(VectorMath3d.LengthSquared(SourceZInTarget) - 1.0) <= ValidationTolerance &&
            Math.Abs(VectorMath3d.Dot(SourceXInTarget, SourceYInTarget)) <= ValidationTolerance &&
            Math.Abs(VectorMath3d.Dot(SourceXInTarget, SourceZInTarget)) <= ValidationTolerance &&
            Math.Abs(VectorMath3d.Dot(SourceYInTarget, SourceZInTarget)) <= ValidationTolerance &&
            Handedness != 0;

        public Vector3d Transform(Vector3d source)
        {
            return VectorMath3d.Add(
                VectorMath3d.Add(
                    VectorMath3d.Scale(SourceXInTarget, source.X),
                    VectorMath3d.Scale(SourceYInTarget, source.Y)),
                VectorMath3d.Scale(SourceZInTarget, source.Z));
        }

        internal Matrix3d ToMatrix()
        {
            return Matrix3d.FromColumns(SourceXInTarget, SourceYInTarget, SourceZInTarget);
        }
    }

    internal static class VectorMath3d
    {
        public static Vector3d Add(Vector3d left, Vector3d right)
        {
            return new Vector3d(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
        }

        public static Vector3d Subtract(Vector3d left, Vector3d right)
        {
            return new Vector3d(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        }

        public static Vector3d Scale(Vector3d value, double scale)
        {
            return new Vector3d(value.X * scale, value.Y * scale, value.Z * scale);
        }

        public static double Dot(Vector3d left, Vector3d right)
        {
            return left.X * right.X + left.Y * right.Y + left.Z * right.Z;
        }

        public static Vector3d Cross(Vector3d left, Vector3d right)
        {
            return new Vector3d(
                left.Y * right.Z - left.Z * right.Y,
                left.Z * right.X - left.X * right.Z,
                left.X * right.Y - left.Y * right.X);
        }

        public static double LengthSquared(Vector3d value)
        {
            return Dot(value, value);
        }

        public static bool TryNormalize(Vector3d value, out Vector3d normalized)
        {
            double lengthSquared = LengthSquared(value);
            if (!value.IsFinite || double.IsNaN(lengthSquared) || double.IsInfinity(lengthSquared) ||
                lengthSquared <= 1e-24)
            {
                normalized = default;
                return false;
            }

            normalized = Scale(value, 1.0 / Math.Sqrt(lengthSquared));
            return true;
        }
    }

    internal readonly struct Matrix3d
    {
        public Matrix3d(
            double m00, double m01, double m02,
            double m10, double m11, double m12,
            double m20, double m21, double m22)
        {
            M00 = m00;
            M01 = m01;
            M02 = m02;
            M10 = m10;
            M11 = m11;
            M12 = m12;
            M20 = m20;
            M21 = m21;
            M22 = m22;
        }

        public double M00 { get; }
        public double M01 { get; }
        public double M02 { get; }
        public double M10 { get; }
        public double M11 { get; }
        public double M12 { get; }
        public double M20 { get; }
        public double M21 { get; }
        public double M22 { get; }

        public Vector3d Column0 => new Vector3d(M00, M10, M20);
        public Vector3d Column1 => new Vector3d(M01, M11, M21);
        public Vector3d Column2 => new Vector3d(M02, M12, M22);

        public double Determinant =>
            M00 * (M11 * M22 - M12 * M21) -
            M01 * (M10 * M22 - M12 * M20) +
            M02 * (M10 * M21 - M11 * M20);

        public bool IsFinite =>
            IsFiniteValue(M00) && IsFiniteValue(M01) && IsFiniteValue(M02) &&
            IsFiniteValue(M10) && IsFiniteValue(M11) && IsFiniteValue(M12) &&
            IsFiniteValue(M20) && IsFiniteValue(M21) && IsFiniteValue(M22);

        public static Matrix3d FromColumns(Vector3d column0, Vector3d column1, Vector3d column2)
        {
            return new Matrix3d(
                column0.X, column1.X, column2.X,
                column0.Y, column1.Y, column2.Y,
                column0.Z, column1.Z, column2.Z);
        }

        public static Matrix3d Multiply(Matrix3d left, Matrix3d right)
        {
            return new Matrix3d(
                left.M00 * right.M00 + left.M01 * right.M10 + left.M02 * right.M20,
                left.M00 * right.M01 + left.M01 * right.M11 + left.M02 * right.M21,
                left.M00 * right.M02 + left.M01 * right.M12 + left.M02 * right.M22,
                left.M10 * right.M00 + left.M11 * right.M10 + left.M12 * right.M20,
                left.M10 * right.M01 + left.M11 * right.M11 + left.M12 * right.M21,
                left.M10 * right.M02 + left.M11 * right.M12 + left.M12 * right.M22,
                left.M20 * right.M00 + left.M21 * right.M10 + left.M22 * right.M20,
                left.M20 * right.M01 + left.M21 * right.M11 + left.M22 * right.M21,
                left.M20 * right.M02 + left.M21 * right.M12 + left.M22 * right.M22);
        }

        public Matrix3d Transpose()
        {
            return new Matrix3d(
                M00, M10, M20,
                M01, M11, M21,
                M02, M12, M22);
        }

        public bool TryOrthonormalizeProper(out Matrix3d normalized)
        {
            if (!IsFinite || Determinant <= 0.0 ||
                !VectorMath3d.TryNormalize(Column0, out Vector3d x))
            {
                normalized = default;
                return false;
            }

            Vector3d yWithoutX = VectorMath3d.Subtract(
                Column1,
                VectorMath3d.Scale(x, VectorMath3d.Dot(Column1, x)));
            if (!VectorMath3d.TryNormalize(yWithoutX, out Vector3d y))
            {
                normalized = default;
                return false;
            }

            Vector3d z = VectorMath3d.Cross(x, y);
            if (!VectorMath3d.TryNormalize(z, out z) || VectorMath3d.Dot(z, Column2) <= 0.0)
            {
                normalized = default;
                return false;
            }

            normalized = FromColumns(x, y, z);
            return Math.Abs(normalized.Determinant - 1.0) <= 1e-9;
        }

        private static bool IsFiniteValue(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
