using System.Collections.Generic;
using UnityEngine;

namespace AdvancedRope
{
    public static class VerletPhysics
    {
        private static List<VerletRopeGenerator> ropeGenerators = new List<VerletRopeGenerator>();
        
        public static void RegisterRopeGenerator(VerletRopeGenerator generator)
        {
            if (!ropeGenerators.Contains(generator))
            {
                ropeGenerators.Add(generator);
            }
        }
        public static void UnregisterRopeGenerator(VerletRopeGenerator generator)
        {
            if (ropeGenerators.Contains(generator))
            {
                ropeGenerators.Remove(generator);
            }
        }

        public static bool Raycast(Vector3 origin, Vector3 direction, out RopeHitInfo hitInfo, float maxDistance)
        {
            return Raycast(new Ray(origin, direction), out hitInfo, maxDistance);
        }
        
        public static bool Raycast(Ray ray, out RopeHitInfo hitInfo, float maxDistance)
        {
            var closestHitDistance = float.MaxValue;
            var isHit = false;
            RopeHitInfo tempHitInfo = default;
            foreach (var generator in ropeGenerators)
            {
                if (generator.Raycast(ray, out var hit, maxDistance))
                {
                    if (hit.Distance >= closestHitDistance) continue;
                    closestHitDistance = hit.Distance;
                    tempHitInfo = hit;
                    isHit = true;
                }
            }

            if (isHit)
            {
                hitInfo = tempHitInfo;
                return true;
            }
            hitInfo = default;
            return false;
        }
        public static bool Raycast(Ray ray, out RopeHitInfo hitInfo, float maxDistance, VerletRopeGenerator excludeGenerator)
        {
            var closestHitDistance = float.MaxValue;
            var isHit = false;
            RopeHitInfo tempHitInfo = default;
            foreach (var generator in ropeGenerators)
            {
                if (generator == excludeGenerator) continue;
                if (generator.Raycast(ray, out var hit, maxDistance))
                {
                    if (hit.Distance >= closestHitDistance) continue;
                    closestHitDistance = hit.Distance;
                    tempHitInfo = hit;
                    isHit = true;
                }
            }
            if (isHit)
            {
                hitInfo = tempHitInfo;
                return true;
            }
            hitInfo = default;
            return false;
        }
    }
}