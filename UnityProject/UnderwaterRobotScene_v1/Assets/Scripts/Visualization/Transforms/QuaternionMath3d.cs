using System;
using UnderwaterRobotScene.Visualization.Data;

namespace UnderwaterRobotScene.Visualization.Transforms
{
    public static class QuaternionMath3d
    {
        public static Quaterniond Multiply(Quaterniond left, Quaterniond right)
        {
            return new Quaterniond(
                left.W * right.X + left.X * right.W + left.Y * right.Z - left.Z * right.Y,
                left.W * right.Y - left.X * right.Z + left.Y * right.W + left.Z * right.X,
                left.W * right.Z + left.X * right.Y - left.Y * right.X + left.Z * right.W,
                left.W * right.W - left.X * right.X - left.Y * right.Y - left.Z * right.Z);
        }

        public static Quaterniond Conjugate(Quaterniond value)
        {
            return new Quaterniond(-value.X, -value.Y, -value.Z, value.W);
        }

        public static Quaterniond Negate(Quaterniond value)
        {
            return new Quaterniond(-value.X, -value.Y, -value.Z, -value.W);
        }

        public static double Dot(Quaterniond left, Quaterniond right)
        {
            return left.X * right.X + left.Y * right.Y + left.Z * right.Z + left.W * right.W;
        }

        public static Quaterniond FromAxisAngleRadians(Vector3d axis, double angleRadians)
        {
            if (!VectorMath3d.TryNormalize(axis, out Vector3d normalizedAxis) ||
                double.IsNaN(angleRadians) ||
                double.IsInfinity(angleRadians))
            {
                throw new ArgumentException("Axis and angle must be finite and the axis must be non-zero.");
            }

            double halfAngle = angleRadians * 0.5;
            double sine = Math.Sin(halfAngle);
            var result = new Quaterniond(
                normalizedAxis.X * sine,
                normalizedAxis.Y * sine,
                normalizedAxis.Z * sine,
                Math.Cos(halfAngle));
            if (!result.TryNormalize(out Quaterniond normalized))
            {
                throw new InvalidOperationException("Axis-angle conversion produced an invalid quaternion.");
            }

            return normalized;
        }

        public static Vector3d Rotate(Quaterniond rotation, Vector3d vector)
        {
            if (!rotation.TryNormalize(out Quaterniond normalized))
            {
                throw new ArgumentException("Rotation quaternion is not usable.", nameof(rotation));
            }

            Quaterniond vectorQuaternion = new Quaterniond(vector.X, vector.Y, vector.Z, 0.0);
            Quaterniond rotated = Multiply(Multiply(normalized, vectorQuaternion), Conjugate(normalized));
            return new Vector3d(rotated.X, rotated.Y, rotated.Z);
        }

        public static bool RepresentsSameRotation(Quaterniond left, Quaterniond right, double tolerance)
        {
            if (tolerance < 0.0 ||
                !left.TryNormalize(out Quaterniond normalizedLeft) ||
                !right.TryNormalize(out Quaterniond normalizedRight))
            {
                return false;
            }

            return Math.Abs(Dot(normalizedLeft, normalizedRight)) >= 1.0 - tolerance;
        }

        internal static Matrix3d ToMatrix(Quaterniond value)
        {
            if (!value.TryNormalize(out Quaterniond q))
            {
                throw new ArgumentException("Quaternion is not usable.", nameof(value));
            }

            double xx = q.X * q.X;
            double yy = q.Y * q.Y;
            double zz = q.Z * q.Z;
            double xy = q.X * q.Y;
            double xz = q.X * q.Z;
            double yz = q.Y * q.Z;
            double wx = q.W * q.X;
            double wy = q.W * q.Y;
            double wz = q.W * q.Z;

            return new Matrix3d(
                1.0 - 2.0 * (yy + zz), 2.0 * (xy - wz), 2.0 * (xz + wy),
                2.0 * (xy + wz), 1.0 - 2.0 * (xx + zz), 2.0 * (yz - wx),
                2.0 * (xz - wy), 2.0 * (yz + wx), 1.0 - 2.0 * (xx + yy));
        }

        internal static bool TryFromMatrix(Matrix3d matrix, out Quaterniond quaternion)
        {
            if (!matrix.TryOrthonormalizeProper(out Matrix3d m))
            {
                quaternion = default;
                return false;
            }

            double trace = m.M00 + m.M11 + m.M22;
            if (trace > 0.0)
            {
                double scale = Math.Sqrt(trace + 1.0) * 2.0;
                quaternion = new Quaterniond(
                    (m.M21 - m.M12) / scale,
                    (m.M02 - m.M20) / scale,
                    (m.M10 - m.M01) / scale,
                    0.25 * scale);
            }
            else if (m.M00 > m.M11 && m.M00 > m.M22)
            {
                double scale = Math.Sqrt(1.0 + m.M00 - m.M11 - m.M22) * 2.0;
                quaternion = new Quaterniond(
                    0.25 * scale,
                    (m.M01 + m.M10) / scale,
                    (m.M02 + m.M20) / scale,
                    (m.M21 - m.M12) / scale);
            }
            else if (m.M11 > m.M22)
            {
                double scale = Math.Sqrt(1.0 + m.M11 - m.M00 - m.M22) * 2.0;
                quaternion = new Quaterniond(
                    (m.M01 + m.M10) / scale,
                    0.25 * scale,
                    (m.M12 + m.M21) / scale,
                    (m.M02 - m.M20) / scale);
            }
            else
            {
                double scale = Math.Sqrt(1.0 + m.M22 - m.M00 - m.M11) * 2.0;
                quaternion = new Quaterniond(
                    (m.M02 + m.M20) / scale,
                    (m.M12 + m.M21) / scale,
                    0.25 * scale,
                    (m.M10 - m.M01) / scale);
            }

            return quaternion.TryNormalize(out quaternion);
        }
    }
}
