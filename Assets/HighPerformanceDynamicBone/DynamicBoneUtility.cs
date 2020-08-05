using UnityEngine;
using Unity.Mathematics;

namespace HighPerformanceDynamicBone
{
    public static class DynamicBoneUtility
    {
        public static bool HandleCollision(in ColliderInfo collider, ref float3 particlePosition,
            in float particleRadius)
        {
            float radius = collider.Radius * math.abs(collider.Scale);
            float h = collider.Height * 0.5f - radius;

            //等价于collider.transform.TransformPoint(collider.Center)
            float3 worldPosition = collider.Position + math.mul(collider.Rotation, collider.Center);

            if (h <= 0)
            {
                return collider.Bound == Bound.Outside
                    ? OutsideSphere(ref particlePosition, particleRadius, worldPosition, radius)
                    : InsideSphere(ref particlePosition, particleRadius, worldPosition, radius);
            }

            float3 center1 = collider.Center;
            float3 center2 = collider.Center;

            switch (collider.Direction)
            {
                case Direction.X:
                    center1.x -= h;
                    center2.x += h;
                    break;
                case Direction.Y:
                    center1.y -= h;
                    center2.y += h;
                    break;
                case Direction.Z:
                    center1.z -= h;
                    center2.z += h;
                    break;
            }


            float3 worldCenter1 = collider.Position + math.mul(collider.Rotation, center1);
            float3 worldCenter2 = collider.Position + math.mul(collider.Rotation, center2);


            return collider.Bound == Bound.Outside
                ? OutsideCapsule(ref particlePosition, particleRadius, worldCenter1,
                    worldCenter2, radius)
                : InsideCapsule(ref particlePosition, particleRadius, worldCenter1,
                    worldCenter2, radius);
        }


        private static bool OutsideSphere(ref float3 particlePosition, float particleRadius, float3 sphereCenter,
            float sphereRadius)
        {
            float r = sphereRadius + particleRadius;
            float r2 = r * r;
            float3 d = particlePosition - sphereCenter;

            float len2 = math.lengthsq(d);

            // if is inside sphere, project onto sphere surface
            if (len2 > 0 && len2 < r2)
            {
                float len = math.sqrt(len2);
                particlePosition = sphereCenter + d * (r / len);
                return true;
            }

            return false;
        }

        private static bool InsideSphere(ref float3 particlePosition, float particleRadius, float3 sphereCenter,
            float sphereRadius)
        {
            float r = sphereRadius - particleRadius;
            float r2 = r * r;
            float3 d = particlePosition - sphereCenter;
            float len2 = math.lengthsq(d);

            // if is outside sphere, project onto sphere surface
            if (len2 > r2)
            {
                float len = math.sqrt(len2);
                particlePosition = sphereCenter + d * (r / len);
                return true;
            }

            return false;
        }


        private static bool OutsideCapsule(ref float3 particlePosition, float particleRadius, float3 capsuleP0,
            float3 capsuleP1,
            float capsuleRadius)
        {
            float r = capsuleRadius + particleRadius;
            float r2 = r * r;
            float3 dir = capsuleP1 - capsuleP0;
            float3 d = particlePosition - capsuleP0;
            float t = math.dot(d, dir);

            if (t <= 0)
            {
                // check sphere1
                float len2 = math.lengthsq(d);
                if (len2 > 0 && len2 < r2)
                {
                    float len = math.sqrt(len2);
                    particlePosition = capsuleP0 + d * (r / len);
                    return true;
                }
            }
            else
            {
                float dl = math.lengthsq(dir);
                if (t >= dl)
                {
                    // check sphere2
                    d = particlePosition - capsuleP1;
                    float len2 = math.lengthsq(d);
                    if (len2 > 0 && len2 < r2)
                    {
                        float len = math.sqrt(len2);
                        particlePosition = capsuleP1 + d * (r / len);
                        return true;
                    }
                }
                else if (dl > 0)
                {
                    // check cylinder
                    t /= dl;
                    d -= dir * t;
                    float len2 = math.lengthsq(d);


                    if (len2 > 0 && len2 < r2)
                    {
                        float len = math.sqrt(len2);
                        particlePosition += d * ((r - len) / len);
                        return true;
                    }
                }
            }

            return false;
        }


        private static bool InsideCapsule(ref float3 particlePosition, float particleRadius, float3 capsuleP0,
            float3 capsuleP1,
            float capsuleRadius)
        {
            float r = capsuleRadius - particleRadius;
            float r2 = r * r;
            float3 dir = capsuleP1 - capsuleP0;
            float3 d = particlePosition - capsuleP0;
            float t = math.dot(d, dir);

            if (t <= 0)
            {
                // check sphere1
                float len2 = math.lengthsq(d);
                if (len2 > r2)
                {
                    float len = Mathf.Sqrt(len2);
                    particlePosition = capsuleP0 + d * (r / len);
                    return true;
                }
            }
            else
            {
                float dl = math.lengthsq(dir);
                if (t >= dl)
                {
                    // check sphere2
                    d = particlePosition - capsuleP1;
                    float len2 = math.lengthsq(d);
                    if (len2 > r2)
                    {
                        float len = Mathf.Sqrt(len2);
                        particlePosition = capsuleP1 + d * (r / len);
                        return true;
                    }
                }
                else if (dl > 0)
                {
                    // check cylinder
                    t /= dl;
                    d -= dir * t;
                    float len2 = math.lengthsq(d);
                    if (len2 > r2)
                    {
                        float len = Mathf.Sqrt(len2);
                        particlePosition += d * ((r - len) / len);
                        return true;
                    }
                }
            }

            return false;
        }
    }
}

