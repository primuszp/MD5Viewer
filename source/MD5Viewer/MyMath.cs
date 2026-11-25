using OpenTK.Mathematics;

namespace MD5Viewer
{
    public static class MyMath
    {
        /// <summary>
        /// MD5 files store only the x, y, z components of unit quaternions; w is
        /// reconstructed from |q| = 1. Doom 3 uses the negative root.
        /// If rounding makes |xyz| slightly exceed 1, w is 0 and xyz is renormalised
        /// so the quaternion remains a valid rotation.
        /// </summary>
        public static void ComputeWComponent(ref Quaternion q)
        {
            float t = 1.0f - (q.X * q.X) - (q.Y * q.Y) - (q.Z * q.Z);

            if (t <= 0.0f)
            {
                q.W = 0.0f;
                float len = MathF.Sqrt((q.X * q.X) + (q.Y * q.Y) + (q.Z * q.Z));
                if (len > 0.0f)
                {
                    q.X /= len;
                    q.Y /= len;
                    q.Z /= len;
                }
            }
            else
            {
                q.W = -MathF.Sqrt(t);
            }
        }

        /// <summary>
        /// Rotates v by the unit quaternion q (equivalent to q * v * conj(q)),
        /// using the optimised form v + 2w(u x v) + 2u x (u x v).
        /// </summary>
        public static Vector3 RotatePoint(ref Quaternion q, ref Vector3 v)
        {
            Vector3 u = new Vector3(q.X, q.Y, q.Z);
            Vector3 t = 2.0f * Vector3.Cross(u, v);
            return v + (q.W * t) + Vector3.Cross(u, t);
        }

        public static Quaternion Normalize(ref Quaternion q)
        {
            /* compute magnitude of the quaternion */
            float mag = (float)Math.Sqrt((q.Xyz.X * q.Xyz.X) + (q.Xyz.Y * q.Xyz.Y) + (q.Xyz.Z * q.Xyz.Z) + (q.W * q.W));

            /* check for bogus length, to protect against divide by zero */
            if (mag > 0.0f)
            {
                /* normalize it */
                float oneOverMag = 1.0f / mag;

                q.X *= oneOverMag;
                q.Y *= oneOverMag;
                q.Z *= oneOverMag;
                q.W *= oneOverMag;
            }
            return q;
        }

        public static Quaternion Mult(ref Quaternion qb, ref Quaternion qa)
        {
            Quaternion outq = new Quaternion();

            outq.W = ((qb.W * qa.W) - (qb.Xyz.X * qa.Xyz.X) - (qb.Xyz.Y * qa.Xyz.Y) - (qb.Xyz.Z * qa.Xyz.Z));
            outq.X = ((qb.W * qa.Xyz.X) + (qb.Xyz.X * qa.W) + (qb.Xyz.Y * qa.Xyz.Z)) - (qb.Xyz.Z * qa.Xyz.Y);
            outq.Y = ((qb.W * qa.Xyz.Y) + (qb.Xyz.Y * qa.W) + (qb.Xyz.Z * qa.Xyz.X)) - (qb.Xyz.X * qa.Xyz.Z);
            outq.Z = ((qb.W * qa.Xyz.Z) + (qb.Xyz.Z * qa.W) + (qb.Xyz.X * qa.Xyz.Y)) - (qb.Xyz.Y * qa.Xyz.X);

            return (outq);
        }

        public static Quaternion MultVec(ref Quaternion q, ref Vector3 v)
        {
            Quaternion outq = new Quaternion();

            outq.W = -(q.Xyz.X * v.X) - (q.Xyz.Y * v.Y) - (q.Xyz.Z * v.Z);
            outq.X = ((q.W * v.X) + (q.Xyz.Y * v.Z)) - (q.Xyz.Z * v.Y);
            outq.Y = ((q.W * v.Y) + (q.Xyz.Z * v.X)) - (q.Xyz.X * v.Z);
            outq.Z = ((q.W * v.Z) + (q.Xyz.X * v.Y)) - (q.Xyz.Y * v.X);

            return (outq);
        }

        public static Quaternion Slerp(ref Quaternion qa, ref Quaternion qb, float t)
        {
            Quaternion from = qa;
            Quaternion to = qb;
            Normalize(ref from);
            Normalize(ref to);

            // check for out-of range parameter and return edge points if so
            if (t <= 0.0)
            {
                return from;
            }

            if (t >= 1.0)
            {
                return to;
            }

            // compute "cosine of angle between quaternions" using dot product
            float cosOmega = DotProduct(ref from, ref to);

            // if negative dot, use -q1. two quaternions q and -q
            // represent the same Rotation, but may produce
            // different slerp. we chose q or -q to rotate using
            // the acute angle.
            float q1w = to.W;
            float q1x = to.Xyz.X;
            float q1y = to.Xyz.Y;
            float q1z = to.Xyz.Z;

            if (cosOmega < 0.0f)
            {
                q1w = -q1w;
                q1x = -q1x;
                q1y = -q1y;
                q1z = -q1z;
                cosOmega = -cosOmega;
            }

            // Floating-point drift can make the dot product slightly greater
            // than one, which would make sqrt(1 - cos²) return NaN.
            cosOmega = Math.Clamp(cosOmega, 0.0f, 1.0f);

            // compute interpolation fraction, checking for quaternions
            // almost exactly the same
            float k0;

            // compute interpolation fraction, checking for quaternions
            // almost exactly the same
            float k1;

            if (cosOmega > 0.9999f)
            {
                // very close - just use linear interpolation,
                // which will protect againt a divide by zero
                k0 = 1.0f - t;
                k1 = t;
            }
            else
            {
                // compute the sin of the angle using the
                // trig identity sin^2(omega) + cos^2(omega) = 1
                float sinOmega = (float)Math.Sqrt(1.0f - (cosOmega * cosOmega));

                // compute the angle from its sin and cosine
                float omega = (float)Math.Atan2(sinOmega, cosOmega);

                // compute inverse of denominator, so we only have to divide
                // once
                float oneOverSinOmega = 1.0f / sinOmega;

                // Compute interpolation parameters
                k0 = (float)Math.Sin((1.0f - t) * omega) * oneOverSinOmega;
                k1 = (float)Math.Sin(t * omega) * oneOverSinOmega;
            }

            // interpolate and return new quaternion
            Quaternion outr = new Quaternion();
            outr.W = (k0 * from.W) + (k1 * q1w);
            outr.X = (k0 * from.Xyz.X) + (k1 * q1x);
            outr.Y = (k0 * from.Xyz.Y) + (k1 * q1y);
            outr.Z = (k0 * from.Xyz.Z) + (k1 * q1z);

            return Normalize(ref outr);
        }

        public static float DotProduct(ref Quaternion qa, ref Quaternion qb)
        {
            return ((qa.Xyz.X * qb.Xyz.X) + (qa.Xyz.Y * qb.Xyz.Y) + (qa.Xyz.Z * qb.Xyz.Z) + (qa.W * qb.W));
        }
    }
}
