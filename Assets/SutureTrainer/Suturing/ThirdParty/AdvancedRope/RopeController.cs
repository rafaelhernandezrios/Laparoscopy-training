using System;
using UnityEngine;

namespace AdvancedRope
{
    public enum RopeDisconnectType
    {
        Start,
        End,
        Both,
        Destroy
    }
    public class RopeController : MonoBehaviour
    {
        [SerializeField] private VerletRopeGenerator ropePrefab;

        /// <summary>
        /// Increases the length of the rope for 1 unit
        /// </summary>
        /// <param name="rope"></param>
        /// <param name="maxRopeNodeCount"></param>
        public static void IncreaseRopeLength(VerletRopeGenerator rope, int maxRopeNodeCount = 50)
        {
            if(rope.Nodes.Count >= maxRopeNodeCount) return;
            rope.IncreaseLength();
        }

        /// <summary>
        /// Decreases the length of the rope for 1 unit
        /// </summary>
        /// <param name="rope"></param>
        /// <param name="minRopeNodeCount"></param>
        public static void DecreaseRopeLength(VerletRopeGenerator rope, int minRopeNodeCount = 2)
        {
            if(rope.Nodes.Count <= minRopeNodeCount) return;
            rope.DecreaseLength();
        }

        /// <summary>
        /// Connects a rope between a rope and a transform
        /// </summary>
        /// <param name="startTransform"></param>
        /// <param name="endRope"></param>
        /// <param name="targetNodeIndex"></param>
        /// <param name="startLocalPos"></param>
        /// <param name="connectionOffset"></param>
        /// <returns></returns>
        public VerletRopeGenerator ConnectRope(Transform startTransform, VerletRopeGenerator endRope,
            int targetNodeIndex, Vector3 startLocalPos = default, Vector3 connectionOffset = default)
        {
            var rope = Instantiate(ropePrefab);
            rope.ropeStartPoint = startTransform.TransformPoint(startLocalPos);
            rope.ropeEndPoint = endRope.Nodes[targetNodeIndex].position;
            rope.Enable();
            rope.PinNode(0, startTransform, startLocalPos);
            rope.ConnectEndToOtherRope(endRope, targetNodeIndex, connectionOffset);
            return rope;
        }

        /// <summary>
        /// Connects a rope between two transforms.
        /// </summary>
        /// <param name="startTransform"></param>
        /// <param name="endTransform"></param>
        /// <param name="startLocalPos"></param>
        /// <param name="endLocalPos"></param>
        /// <returns> Connected Rope</returns>
        public VerletRopeGenerator ConnectRope(Transform startTransform, Transform endTransform, Vector3 startLocalPos = default, Vector3 endLocalPos = default)
        {
            var rope = Instantiate(ropePrefab);
            rope.ropeStartPoint = startTransform.TransformPoint(startLocalPos);
            rope.ropeEndPoint = endTransform.TransformPoint(endLocalPos);
            rope.Enable();
            rope.PinNode(0, startTransform, startLocalPos);
            rope.PinNode(-1, endTransform, endLocalPos);
            return rope;
        }
        
        /// <summary>
        /// Connects a rope between a transform and a rigidbody.
        /// </summary>
        /// <param name="startTransform"></param>
        /// <param name="endRigidbody"></param>
        /// <param name="startLocalPos"></param>
        /// <param name="endLocalPos"></param>
        /// <returns> Connected Rope </returns>
        public VerletRopeGenerator ConnectRope(Transform startTransform, Rigidbody endRigidbody, Vector3 startLocalPos = default, Vector3 endLocalPos = default)
        {
            var rope = Instantiate(ropePrefab);
            //rope.InitializeRope();
            rope.ropeStartPoint = startTransform.TransformPoint(startLocalPos);
            rope.ropeEndPoint = endRigidbody.transform.TransformPoint(endLocalPos);
            rope.Enable();
            rope.PinNode(0, startTransform, startLocalPos);
            rope.ConnectEndRigidbody(endRigidbody, endLocalPos);
            return rope;
        }
        
        /// <summary>
        /// Connects a rope between two rigidbodies.
        /// </summary>
        /// <param name="startRigidbody"></param>
        /// <param name="endRigidbody"></param>
        /// <param name="startLocalPos"></param>
        /// <param name="endLocalPos"></param>
        /// <returns> Connected Rope </returns>
        public VerletRopeGenerator ConnectRope(Rigidbody startRigidbody, Rigidbody endRigidbody, Vector3 startLocalPos = default, Vector3 endLocalPos = default)
        {
            var rope = Instantiate(ropePrefab);
            //rope.InitializeRope();
            rope.ropeStartPoint = startRigidbody.transform.TransformPoint(startLocalPos);
            rope.ropeEndPoint = endRigidbody.transform.TransformPoint(endLocalPos);
            rope.Enable();
            rope.ConnectStartRigidbody(startRigidbody, startLocalPos);
            rope.ConnectEndRigidbody(endRigidbody, endLocalPos);
            return rope;
        }

        /// <summary>
        /// Connects the start of an existing rope to a rope
        /// </summary>
        /// <param name="rope"></param>
        /// <param name="targetRope"></param>
        /// <param name="targetNodeIndex"></param>
        /// <param name="connectionOffset"></param>
        public static void ConnectRopeStart(VerletRopeGenerator rope, VerletRopeGenerator targetRope, int targetNodeIndex, Vector3 connectionOffset = default)
        {
            if(!rope || !targetRope) return;
            rope.UnpinNode(0);
            rope.ConnectStartToOtherRope(targetRope, targetNodeIndex, connectionOffset);
        }
        
        /// <summary>
        /// Connects the start of an existing rope to a transform.
        /// </summary>
        /// <param name="rope"></param>
        /// <param name="startTransform"></param>
        /// <param name="localPos"></param>
        public static void ConnectRopeStart(VerletRopeGenerator rope, Transform startTransform, Vector3 localPos = default)
        {
            if (!rope) return;
            rope.PinNode(0, startTransform, localPos);
        }
        
        /// <summary>
        /// Connects the start of an existing rope to a rigidbody.
        /// </summary>
        /// <param name="rope"></param>
        /// <param name="startRigidbody"></param>
        /// <param name="localPos"></param>
        public static void ConnectRopeStart(VerletRopeGenerator rope, Rigidbody startRigidbody, Vector3 localPos = default)
        {
            if (!rope) return;
            rope.ConnectStartRigidbody(startRigidbody, localPos, 0);
        }
        
        /// <summary>
        /// Connects the start of an existing rope to a rigidbody with a time to reach.
        /// </summary>
        /// <param name="rope"></param>
        /// <param name="startRigidbody"></param>
        /// <param name="localPos"></param>
        public static void ConnectRopeStart(VerletRopeGenerator rope, Rigidbody startRigidbody, Vector3 localPos = default, float timeToReach = 0.5f)
        {
            if (!rope) return;
            rope.ConnectStartRigidbody(startRigidbody, localPos, timeToReach);
        }

        /// <summary>
        /// Connects the end of an existing rope to another rope
        /// </summary>
        /// <param name="rope"></param>
        /// <param name="targetRope"></param>
        /// <param name="targetNodeIndex"></param>
        /// <param name="connectionOffset"></param>
        public static void ConnectRopeEnd(VerletRopeGenerator rope, VerletRopeGenerator targetRope, int targetNodeIndex, Vector3 connectionOffset = default)
        {
            if(!rope || !targetRope) return;
            rope.UnpinNode(-1);
            rope.ConnectEndToOtherRope(targetRope, targetNodeIndex, connectionOffset);
        }
        
        /// <summary>
        /// Connects the end of an existing rope to a transform.
        /// </summary>
        /// <param name="rope"></param>
        /// <param name="endTransform"></param>
        /// <param name="localPos"></param>
        public static void ConnectRopeEnd(VerletRopeGenerator rope, Transform endTransform, Vector3 localPos = default)
        {
            if (!rope) return;
            rope.PinNode(-1, endTransform, localPos);
        }
        
        /// <summary>
        /// Connects the end of an existing rope to a rigidbody.
        /// </summary>
        /// <param name="rope"></param>
        /// <param name="endRigidbody"></param>
        /// <param name="localPos"></param>
        public static void ConnectRopeEnd(VerletRopeGenerator rope, Rigidbody endRigidbody, Vector3 localPos = default)
        {
            if (!rope) return;
            rope.ConnectEndRigidbody(endRigidbody, localPos);
        }
        
        
        /// <summary>
        /// Disconnects rope based on the specified disconnect type.
        /// </summary>
        /// <param name="rope"></param>
        /// <param name="disconnectType"> Start, End, Both, Destroy</param>
        /// <returns> Disconnected Rope: null if rope destroyed</returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public VerletRopeGenerator DisconnectRope(VerletRopeGenerator rope, RopeDisconnectType disconnectType)
        {
            switch (disconnectType)
            {
                case RopeDisconnectType.Start:
                    DisconnectRopeStart(rope);
                    return rope;
                case RopeDisconnectType.End:
                    DisconnectRopeEnd(rope);
                    return rope;
                case RopeDisconnectType.Both:
                    DisconnectRopeStart(rope);
                    DisconnectRopeEnd(rope);
                    return rope;
                case RopeDisconnectType.Destroy:
                    DisconnectRopeStart(rope);
                    DisconnectRopeEnd(rope);
                    Destroy(rope.gameObject);
                    return null;
                default:
                    throw new ArgumentOutOfRangeException(nameof(disconnectType), disconnectType, null);
            }
        }
        
        private static void DisconnectRopeStart(VerletRopeGenerator rope)
        {
            rope.DisconnectStartRigidbody();
            rope.UnpinNode(0);
            rope.DisconnectFromOtherRope(0);
        }

        private static void DisconnectRopeEnd(VerletRopeGenerator rope)
        {
            rope.DisconnectEndRigidbody();
            rope.UnpinNode(-1);
            rope.DisconnectFromOtherRope(-1);
        }
    }
}