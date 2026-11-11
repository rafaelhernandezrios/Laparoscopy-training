using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AdvancedRope
{
    /// <summary>
    /// Base class for generating and simulating a Verlet integration based rope.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class VerletRopeGenerator : MonoBehaviour
    {
        #region --- Data Structures ---

        /// <summary>
        /// Node representing a point mass in the rope simulation.
        /// </summary>
        [System.Serializable]
        public class RopeNode
        {
            public Vector3 position;
            public Vector3 prevPosition;
            public bool isPinned;

            public RopeNode connectedNode;
            public Vector3 connectionOffset;
            
            public Transform pinnedTransform;
            public Vector3 pinLocalPos;

            public RopeNode(Vector3 pos)
            {
                position = pos;
                prevPosition = pos;
                isPinned = false;
            }

            /// <summary>
            /// Pin this node to a transform with optional local offset.
            /// </summary>
            /// <param name="t">Target Transform</param>
            /// <param name="offset">Local offset</param>
            public void PinTo(Transform t, Vector3 offset)
            {
                isPinned = true;
                pinnedTransform = t;
                pinLocalPos = offset;
            }
            
            /// <summary>
            /// Connect this node to another rope node with optional offset.
            /// </summary>
            /// <param name="targetNode"> Target Rope Node </param>
            /// <param name="offset">Local offset</param>
            public void ConnectToNode(RopeNode targetNode, Vector3 offset = default)
            {
                connectedNode = targetNode;
                connectionOffset = offset;
                isPinned = false;
                pinnedTransform = null;
            }
            
            /// <summary>
            /// Unpin this node.
            /// </summary>
            public void Unpin()
            {
                isPinned = false;
                pinnedTransform = null;
            }

            /// <summary>
            /// Apply an external force to this node.
            /// </summary>
            /// <param name="force"></param>
            public void ApplyForce(Vector3 force)
            {
                position += force;
            }
        }

        [System.Serializable]
        public struct PinConfiguration
        {
            [Tooltip("Index of the node to pin (0 = initial, -1 = last)")]
            public int nodeIndex;
            public Transform targetTransform;
            public Vector3 localPos;
        }

        #endregion

        #region --- Configuration ---

        //[Header("General Settings")] 
        public bool initializeOnStart = false;
        public bool enabled = true;
        [Tooltip("Start point of the rope")]
        public Vector3 ropeStartPoint;
        [Tooltip("End point of the rope (Optional, if empty rope hangs freely)")]
        public Vector3 ropeEndPoint;

        //[Header("Resolution & Dimensions")]
        [Range(0.01f, 1f)]
        [Tooltip("0.1 = Sparse, 1.0 = Maximum density according to radius")]
        public float ropeResolution = 0.5f;
        public float ropeRadius = 0.1f;
        
        //[Header("Physics Parameters")]
        public Vector3 gravity = new Vector3(0, -9.81f, 0);
        [Range(0f, 1f)] public float airFriction = 0.1f;
        [Range(0f, 1f)] public float stiffness = 1.0f;
        [Tooltip("Constraint solver iteration count. Higher value = more stiff but expensive")]
        public int constraintIterations = 8;
        [Tooltip("The force that rope applies to rigidbodies it collides with")]
        public float collisionPushPower = 5f;
        [Range(0f, 1f)] public float contactFriction = 0.5f;
        

        //[Header("Coupling Settings")] 
        public Rigidbody connectedBodyStart;
        public Rigidbody connectedBodyEnd;
        public float rigidbodyInteractionForce = 10f;
        
        //[Header("Collision Settings")]
        public bool enableCollisions = true;
        public LayerMask collisionLayer;
        public bool enableSelfCollision = false;

        //[Header("Initial Setup")] 
        public List<PinConfiguration> initialPinPoints;
        
        //[Header("Mesh Settings")]
        public int meshRadialSegments = 8;
        public float meshTextureTiling = 1f;

        #endregion

        #region  --- Public Properties ---

        public List<RopeNode> Nodes { get; private set; } = new List<RopeNode>();

        #endregion

        #region --- Private Variables ---
        
        private float _segmentLength;

        /// <summary>Longitud de reposo de cada segmento (añadido para SutureTrainer).</summary>
        public float SegmentLength => _segmentLength;
        private Collider[] _collisionBuffer = new Collider[10];
        private List<Collider> _ignoreColliders = new List<Collider>();
        
        private RopeMesh _ropeMesh;
        
        #endregion

        #region --- Initialization ---

        private void OnEnable()
        {
            if(enabled)
                VerletPhysics.RegisterRopeGenerator(this);
        }
        private void OnDisable()
        {
            VerletPhysics.UnregisterRopeGenerator(this);
        }
        

        private void Start()
        {
            if(initializeOnStart && enabled)
                InitializeRope();
        }

        /// <summary>
        /// Creates the rope from scratch according to the parameters.
        /// </summary>
        public void InitializeRope()
        {
            _ropeMesh?.ClearMesh();
            _ropeMesh = new RopeMesh(GetComponent<MeshFilter>(), this, meshRadialSegments, meshTextureTiling);
            Nodes.Clear();
            _ignoreColliders.Clear();
            
            var startPos = ropeStartPoint;
            var endPos = ropeEndPoint;
            
            if(Vector3.Distance(startPos, endPos) < 0.01f)
                endPos = startPos + Vector3.forward * 5f;
            
            var totalDistance = Vector3.Distance(startPos, endPos);

            var diameter = ropeRadius * 2f;
            var maxNodes = Mathf.FloorToInt(totalDistance / Mathf.Max(0.001f, diameter));
            var minNodes = 2;

            var nodeCount = Mathf.RoundToInt(Mathf.Lerp(minNodes, maxNodes, ropeResolution));
            
            _segmentLength = totalDistance / (nodeCount - 1);
            
            var direction = (endPos - startPos).normalized;
            for (var i = 0; i < nodeCount; i++)
            {
                var pos = startPos + direction * (_segmentLength * i);
                Nodes.Add(new RopeNode(pos));
            }
            
            // Apply initial pin configurations
            ApplyInitialPins();
            
            if(connectedBodyStart)
                _ignoreColliders.AddRange(connectedBodyStart.GetComponentsInChildren<Collider>());
            if(connectedBodyEnd)
                _ignoreColliders.AddRange(connectedBodyEnd.GetComponentsInChildren<Collider>());
            
            _ropeMesh.SetupRopeMesh();
        }

        private void ApplyInitialPins()
        {
            if(initialPinPoints == null) return;

            foreach (var pin in initialPinPoints)
            {
                var index = pin.nodeIndex;
                if (index < 0)
                    index = Nodes.Count + index;
                
                if(index >= 0 && index < Nodes.Count && pin.targetTransform)
                    PinNode(index, pin.targetTransform, pin.localPos);
            }
        }

        #endregion

        #region --- Core Physics Loop ---

        private void LateUpdate()
        {
            if(!enabled) return;
            _ropeMesh.UpdateMesh();
        }

        private void FixedUpdate()
        {
            if(!enabled) return;
            if(Nodes.Count < 2) return;
            
            SimulateVerlet();


            for (var i = 0; i < constraintIterations; i++)
            {
                ApplyConstraints();

                ApplyInterRopeConnection();
                //SyncConnectedNodes();
                
                // Check collisions every second iteration (Optimization)
                // ---- MODIFICADO (SutureTrainer): el comentario lo decía pero el
                // código colisionaba en TODAS las iteraciones; con el hilo largo
                // eran miles de consultas de física por paso y caían los fps.
                if(enableCollisions && ((i & 1) == 1 || i == constraintIterations - 1)) 
                    ResolveCollisions();
            }

            ApplyCouplingForces();
        }

        /// <summary>
        /// Follow connected node and add mass;
        /// </summary>
        private void ApplyInterRopeConnection()
        {
            for (var i = 0; i < Nodes.Count; i++)
            {
                var myNode = Nodes[i];
                if(myNode.connectedNode == null) continue;
                
                var otherNode = myNode.connectedNode;
                
                var currentVector = myNode.position - otherNode.position;
                var targetVector = myNode.connectionOffset;
                
                var diff = currentVector - targetVector;
                
                var moveFactorA = 0.5f;
                var moveFactorB = 0.5f;

                if (myNode.isPinned)
                {
                    moveFactorA = 0f;
                    moveFactorB = 1f;
                }
                else if (otherNode.isPinned)
                {
                    moveFactorA = 1f;
                    moveFactorB = 0f;
                }
                
                if(moveFactorA > 0f)
                    myNode.position -= diff * moveFactorA;
                if(moveFactorB > 0f)
                    otherNode.position += diff * moveFactorB;
            }
        }
        
        /// <summary>
        /// Prevent connected nodes from drifting apart while constraint iterations.
        /// </summary>
        private void SyncConnectedNodes()
        {
            foreach (var node in Nodes)
                if (node.connectedNode != null)
                    node.position = node.connectedNode.position + node.connectionOffset;
        }

        
        /// <summary>
        /// Basic Verlet Integration: x(t+dt) = 2x(t) - x(t-dt)) + a*dt*dt
        /// </summary>
        private void SimulateVerlet()
        {
            var dt = Time.fixedDeltaTime;
            var gravityStep = gravity * (dt * dt);

            foreach (var node in Nodes)
            {
                if (node.isPinned)
                {
                    if (node.pinnedTransform)
                    {
                        node.position = node.pinnedTransform.TransformPoint(node.pinLocalPos);
                        node.prevPosition = node.position;
                    }
                    continue;
                }
                
                var velocity = (node.position - node.prevPosition) * (1f - airFriction);
                var newPos = node.position + velocity + gravityStep;
                
                node.prevPosition = node.position;
                node.position = newPos;
            }
        }

        private void ApplyConstraints()
        {
            for (var i = 0; i < Nodes.Count - 1; i++)
            {
                var nodeA = Nodes[i];
                var nodeB = Nodes[i + 1];
                
                var diff = nodeA.position - nodeB.position;
                var dist = diff.magnitude;
                
                var differance = (dist - _segmentLength) / dist;
                var translate = diff * (0.5f * differance * stiffness);
                
                if (!nodeA.isPinned)
                    nodeA.position -= translate;
                
                if (!nodeB.isPinned)
                    nodeB.position += translate;
            }
        }

        #endregion

        #region --- Two-Way Coupling ---

        /// <summary>
        /// Rope pulls rigidbodies at the ends and vice versa.
        /// </summary>
        private void ApplyCouplingForces()
        {
            if(connectedBodyStart && Nodes.Count > 0)
            {
                var firstNode = Nodes[0];

                if (Nodes.Count > 1)
                {
                    var pullDir = (Nodes[1].position - firstNode.position).normalized;
                    var dist = Vector3.Distance(Nodes[1].position, firstNode.position);
                    if (dist > _segmentLength)
                    {
                        var force = pullDir * ((dist - _segmentLength) * rigidbodyInteractionForce);
                        connectedBodyStart.AddForceAtPosition(force, firstNode.position);
                    }
                }
            }
            
            if(connectedBodyEnd && Nodes.Count > 0)
            {
                var lastNode = Nodes[^1];
                var prevNode = Nodes[^2];
                
                var pullDir = (prevNode.position - lastNode.position).normalized;
                var dist = Vector3.Distance(prevNode.position, lastNode.position);

                if (dist > _segmentLength)
                {
                    var force = pullDir * ((dist - _segmentLength) * rigidbodyInteractionForce);
                    connectedBodyEnd.AddForceAtPosition(force, lastNode.position);
                }
            }
        }

        #endregion

        #region --- Collisions ---

        /// <summary>
        /// Manages all collision types
        /// </summary>
        private void ResolveCollisions()
        {
            for (var i = 0; i < Nodes.Count; i++)
            {
                if(Nodes[i].isPinned) continue;

                HandleTerrainCollision(Nodes[i]);

                HandleStandardCollisions(Nodes[i]);

                if (enableSelfCollision)
                    HandleSelfCollision(i);
            }
        }

        private void HandleTerrainCollision(RopeNode node)
        {
            var terrain = UnityEngine.Terrain.activeTerrain;
            if (!terrain) return;
            
            var terrainHeight = terrain.SampleHeight(node.position) + terrain.transform.position.y;
            if (node.position.y < terrainHeight + ropeRadius)
            {
                node.position = new Vector3(node.position.x, terrainHeight + ropeRadius, node.position.z);
                
                // Ground friction
                var vel = node.position - node.prevPosition;
                node.prevPosition = node.position - (vel * 0.5f);
            }
        }
        private void HandleStandardCollisions(RopeNode node)
        {
            var count = Physics.OverlapSphereNonAlloc(node.position, ropeRadius, _collisionBuffer, collisionLayer);

            for (var i = 0; i < count; i++)
            {
                var col = _collisionBuffer[i];
                if(col is TerrainCollider) continue;
                if (_ignoreColliders.Contains(col)) continue;

                if (col is MeshCollider { convex: false } meshCol)
                {
                    HandleNonConvexCollision(node, meshCol);
                    continue;
                }
                
                var closestPoint = col.ClosestPoint(node.position);
                var distToSurface = Vector3.Distance(node.position, closestPoint);
                
                if (distToSurface < ropeRadius)
                {
                    var pushDir = (node.position - closestPoint).normalized;
                    
                    if(pushDir == Vector3.zero)
                        pushDir = (node.position - node.prevPosition).normalized;
                    
                    var penetrationDepth = ropeRadius - distToSurface;
                    var pushVector = pushDir * penetrationDepth;
                    
                    if(!node.isPinned)
                        node.position += pushVector;
                    
                    // Apply force to rigidbody
                    var targetRb = col.attachedRigidbody;
                    if (targetRb && !targetRb.isKinematic)
                    {
                        var scale = collisionPushPower / constraintIterations;
                        var reactionForce = -pushDir * (penetrationDepth * scale);
                        
                        targetRb.AddForceAtPosition(reactionForce, node.position, ForceMode.Impulse);
                        
                        // Friction effect
                        
                        var nodeVel = (node.position - node.prevPosition) / Time.fixedDeltaTime;
                        var rbVel = targetRb.GetPointVelocity(node.position);
                        var relativeVel = rbVel - nodeVel;
                        
                        var tangentVel = Vector3.ProjectOnPlane(relativeVel, pushDir);
                        
                        var normalForceMag = reactionForce.magnitude;
                        var frictionMag = normalForceMag * contactFriction;

                        if (tangentVel.sqrMagnitude > 0.001f)
                        {
                            var frictionDir = -tangentVel.normalized;
                            var frictionForce = frictionDir * frictionMag;
                            
                            targetRb.AddForceAtPosition(frictionForce, node.position, ForceMode.Impulse);

                            if (!node.isPinned)
                            {
                                var dragEffect = -frictionForce * (Time.fixedDeltaTime * 0.001f);
                                node.position += dragEffect;
                            }
                        }
                    }
                }
            }
        }
        private void HandleNonConvexCollision(RopeNode node, MeshCollider meshCol)
        {
            var travelDir = node.position - node.prevPosition;
            var travelDist = travelDir.magnitude;
            
            if (travelDist < 0.0001f) return;

            if (Physics.SphereCast(node.prevPosition, ropeRadius, travelDir.normalized, out var hit,
                    travelDist + ropeRadius, collisionLayer))
            {
                if(hit.collider == meshCol)
                    node.position = hit.point + hit.normal * (ropeRadius * 1.1f);
            }
        }
        private void HandleSelfCollision(int currentIndex)
        {
            var currentNode = Nodes[currentIndex];

            for (var i = 0; i < Nodes.Count; i++)
            {
                if(Mathf.Abs(currentIndex - i) <= 1) continue;
                
                var otherNode = Nodes[i];
                var distSqr = (currentNode.position - otherNode.position).sqrMagnitude;
                var minSpacing = ropeRadius * 2f;

                if (distSqr < minSpacing * minSpacing)
                {
                    var dist = Mathf.Sqrt(distSqr);
                    var pushDir = (currentNode.position - otherNode.position).normalized;
                    var pushAmount = (minSpacing - dist) * 0.5f;
                    
                    if(!currentNode.isPinned)
                        currentNode.position += pushDir * pushAmount;
                    if(!otherNode.isPinned)
                        otherNode.position -= pushDir * pushAmount;
                }
            }
        }

        #endregion

        #region --- Public API ---

        public void Enable()
        {
            if(enabled) return;
            if(gameObject.activeInHierarchy)
                VerletPhysics.RegisterRopeGenerator(this);
            
            InitializeRope();
            enabled = true;
        }

        public void Disable()
        {
            VerletPhysics.UnregisterRopeGenerator(this);
            _ropeMesh?.ClearMesh();
            enabled = false;
        }

        /// <summary>
        /// Increases length 1 node
        /// </summary>
        public void IncreaseLength()
        {
            if(Nodes.Count < 2) return;
            var pos = (Nodes[0].position + Nodes[1].position) * 0.5f;
            Nodes.Insert(1, new RopeNode(pos));
            _ropeMesh.SetupRopeMesh();
        }

        /// <summary>
        /// Decreases length 1 node
        /// </summary>
        public void DecreaseLength()
        {
            if(Nodes.Count <= 2) return;
            for (var i = 1; i < Nodes.Count - 1; i++)
            {
                if(Nodes[i].isPinned || Nodes[i].connectedNode != null) continue;
                Nodes.RemoveAt(i);
                break;
            }
            _ropeMesh.SetupRopeMesh();
        }
        
        /// <summary>
        /// Pin the node at index to the target transform with optional local offset.
        /// </summary>
        /// <param name="index"> 0 for initial, -1 for last element</param>
        /// <param name="target"></param>
        /// <param name="localOffset"></param>
        public void PinNode(int index, Transform target, Vector3 localOffset = default)
        {
            if(index < 0)
                index = Nodes.Count + index;
            if (index >= 0 && index < Nodes.Count)
            {
                Nodes[index].PinTo(target, localOffset);
                if (index == 0)
                    ropeStartPoint = target.position;
                if(index == Nodes.Count -1)
                    ropeEndPoint = target.position;
            }
        }
        
        /// <summary>
        /// Releases the pin on the node at index.
        /// </summary>
        /// <param name="index"> 0 for initial, -1 for last element</param>
        public void UnpinNode(int index)
        {
            if(index < 0)
                index = Nodes.Count + index;
            if (index >= 0 && index < Nodes.Count)
            {
                Nodes[index].Unpin();
                if(index == 0)
                    ropeStartPoint = transform.position;
                if(index == Nodes.Count -1)
                    ropeEndPoint = transform.position + Vector3.forward * 5f;
            }
        }

        /// <summary>
        /// Connects the start of the rope to the given rigidbody over time.
        /// </summary>
        /// <param name="connectedRb"></param>
        /// <param name="localPos"></param>
        /// <param name="time"></param>
        public void ConnectStartRigidbody(Rigidbody connectedRb, Vector3 localPos, float time)
        {
            StopCoroutine(RigibBodyConnectionRoute(connectedRb,localPos, time));
            if(connectedBodyStart)
            {
                _ignoreColliders.RemoveAll(c =>
                    c && (c.gameObject == connectedBodyStart.gameObject) || 
                    c.transform.IsChildOf(connectedBodyStart.transform));
            }

            UnpinNode(0);
            connectedBodyStart = null;
            
            if(Nodes.Count == 0) return;

            Nodes[0].isPinned = true;
            
            StartCoroutine(RigibBodyConnectionRoute(connectedRb, localPos, time));
        }

        private IEnumerator RigibBodyConnectionRoute(Rigidbody connectedRb, Vector3 localPos, float time)
        {
            var dist = Vector3.Distance(Nodes[0].position, connectedRb.position + connectedRb.transform.TransformVector(localPos));
            var deltaTime = Time.fixedDeltaTime;
            var deltaDist = dist;
            if(time > 0.01f)
                deltaDist = deltaTime * (dist / time);
            var wait = new WaitForFixedUpdate();
            while (dist > 0.01f)
            {
                var dir = (connectedRb.position + connectedRb.transform.TransformVector(localPos) - Nodes[0].position)
                    .normalized;
                Nodes[0].position += dir * deltaDist;
                dist = Vector3.Distance(Nodes[0].position,
                    connectedRb.position + connectedRb.transform.TransformVector(localPos));
                yield return wait;
            }
            connectedBodyStart = connectedRb;
            _ignoreColliders.AddRange(connectedRb.GetComponentsInChildren<Collider>());
            PinNode(0, connectedBodyStart.transform, localPos);
        }

        /// <summary>
        /// Connects the start of the rope to the given rigidbody.
        /// </summary>
        /// <param name="connectedRb"></param>
        /// <param name="localPos"></param>
        public void ConnectStartRigidbody(Rigidbody connectedRb, Vector3 localPos)
        {
            if (connectedBodyStart)
            {
                _ignoreColliders.RemoveAll(c =>
                    c&& (c.gameObject == connectedBodyStart.gameObject) || 
                    c.transform.IsChildOf(connectedBodyStart.transform));
            }
            connectedBodyStart = connectedRb;
            _ignoreColliders.AddRange(connectedRb.GetComponentsInChildren<Collider>());
            PinNode(0, connectedBodyStart.transform, localPos);
        }
        
        /// <summary>
        /// Connects the end of the rope to the given rigidbody.
        /// </summary>
        /// <param name="connectedRb"></param>
        /// <param name="localPos"></param>
        public void ConnectEndRigidbody(Rigidbody connectedRb, Vector3 localPos)
        {
            if (connectedBodyEnd)
            {
                _ignoreColliders.RemoveAll(c =>
                    c && (c.gameObject == connectedBodyEnd.gameObject) || 
                    c.transform.IsChildOf(connectedBodyEnd.transform));
            }
            connectedBodyEnd = connectedRb;
            _ignoreColliders.AddRange(connectedRb.GetComponentsInChildren<Collider>());
            PinNode(-1, connectedBodyEnd.transform, localPos);
        }
        
        /// <summary>
        /// Disconnects the start rigidbody from the rope.
        /// </summary>
        public void DisconnectStartRigidbody()
        {
            if (!connectedBodyStart) return;
            
            _ignoreColliders.RemoveAll(c =>
                c != null && (c.gameObject == connectedBodyStart.gameObject) || 
                c.transform.IsChildOf(connectedBodyStart.transform));
            
            connectedBodyStart = null;
            UnpinNode(0);
        }
        
        /// <summary>
        /// Disconnects the end rigidbody from the rope.
        /// </summary>
        public void DisconnectEndRigidbody()
        {
            if (!connectedBodyEnd) return;
            
            _ignoreColliders.RemoveAll(c =>
                c != null && (c.gameObject == connectedBodyEnd.gameObject) || 
                c.transform.IsChildOf(connectedBodyEnd.transform));
            
            connectedBodyEnd = null;
            UnpinNode(-1);
        }
        
        
        /// <summary>
        /// Connect the end of this rope to a node of target rope.
        /// </summary>
        /// <param name="targetRope"></param>
        /// <param name="targetNodeIndex"></param>
        /// <param name="connectionOffset"></param>
        public void ConnectEndToOtherRope(VerletRopeGenerator targetRope, int targetNodeIndex, Vector3 connectionOffset = default)
        {
            ConnectToOtherRope(-1, targetRope, targetNodeIndex, connectionOffset);
        }
        
        /// <summary>
        /// Connect the start of this rope to a node of target rope.
        /// </summary>
        /// <param name="targetRope"></param>
        /// <param name="targetNodeIndex"></param>
        /// <param name="connectionOffset"></param>
        public void ConnectStartToOtherRope(VerletRopeGenerator targetRope, int targetNodeIndex, Vector3 connectionOffset = default)
        {
            ConnectToOtherRope(0, targetRope, targetNodeIndex, connectionOffset);
        }
        
        /// <summary>
        /// Connect a node of this node to a node of target rope.
        /// </summary>
        /// <param name="myNodeIndex"> 0 for initial, -1 for last element</param>
        /// <param name="targetRope"></param>
        /// <param name="targetNodeIndex"> 0 for initial, -1 for last element</param>
        public void ConnectToOtherRope(int myNodeIndex, VerletRopeGenerator targetRope, int targetNodeIndex, Vector3 connectionOffset = default)
        {
            if(myNodeIndex < 0) 
                myNodeIndex = Nodes.Count + myNodeIndex;
            if(targetNodeIndex < 0) 
                targetNodeIndex = targetRope.Nodes.Count + targetNodeIndex;
            if(myNodeIndex < 0 || myNodeIndex >= Nodes.Count) return;
            if(!targetRope || targetNodeIndex < 0 || targetNodeIndex >= targetRope.Nodes.Count) return;
            
            var myNode = Nodes[myNodeIndex];
            var targetNode = targetRope.Nodes[targetNodeIndex];
            
            if(targetNodeIndex == 0)
                targetRope.DisconnectStartRigidbody();
            if(targetNodeIndex == targetRope.Nodes.Count - 1)
                targetRope.DisconnectEndRigidbody();
            
            myNode.ConnectToNode(targetNode, connectionOffset);
            targetNode.ConnectToNode(myNode, -connectionOffset);
        }
        /// <summary>
        /// Disconnects a node from any other rope node.
        /// </summary>
        /// <param name="myNodeIndex"> 0 for initial, -1 for last element</param>
        public void DisconnectFromOtherRope(int myNodeIndex)
        {
            if(myNodeIndex < 0) 
                myNodeIndex = Nodes.Count + myNodeIndex;
            if(myNodeIndex < 0 || myNodeIndex >= Nodes.Count) return;
            
            var myNode = Nodes[myNodeIndex];
            var targetNode = myNode.connectedNode;
            myNode.ConnectToNode(null);
            targetNode?.ConnectToNode(null);
        }

        /// <summary>
        /// Make a raycast against the rope and return hit information.
        /// </summary>
        /// <param name="ray"> The ray coming from a point</param>
        /// <param name="hitInfo"> Hit result</param>
        /// <param name="maxDistance"> Max search distance</param>
        /// <returns> True if ray hits rope</returns>
        public bool Raycast(Ray ray, out RopeHitInfo hitInfo, float maxDistance = 100f)
        {
            hitInfo = new RopeHitInfo();
            var closestDistSqr = float.MaxValue;
            var hasHit = false;

            for (var i = 0; i < Nodes.Count - 1; i++)
            {
                var p1 = Nodes[i].position;
                var p2 = Nodes[i + 1].position;
                
                var distSqr = SqrDistanceRaySegment(ray, p1, p2, out var rayParam, out var segmentParam);

                if (distSqr <= (ropeRadius * ropeRadius) && rayParam < maxDistance && rayParam > 0)
                {
                    if (rayParam < closestDistSqr)
                    {
                        closestDistSqr = rayParam;
                        
                        hitInfo.DidHit = true;
                        hitInfo.Rope = this;
                        hitInfo.NodeIndex = segmentParam < 0.5f ? i : i + 1;
                        hitInfo.NodePoint = segmentParam < 0.5f ? p1 : p2;
                        hitInfo.Distance = rayParam;
                        hitInfo.SegmentRatio = segmentParam;
                        hitInfo.Point = ray.GetPoint(rayParam);
                        
                        hasHit = true;
                    }
                }
            }
            
            return hasHit;
        }

        private static float SqrDistanceRaySegment(Ray ray, Vector3 s1, Vector3 s2, out float rayParam, out float segmentParam)
        {
            var u = ray.direction;
            var v = s2 - s1;
            var w = ray.origin - s1;
            
            var a = Vector3.Dot(u, u);
            var b = Vector3.Dot(u, v);
            var c = Vector3.Dot(v, v);
            var d = Vector3.Dot(u, w);
            var e = Vector3.Dot(v, w);
            
            var denom = a * c - b * b;
            float sc, tc;

            if (denom < 1e-6f)
            {
                sc = 0f;
                tc = (b > c ? d / b : e / c);
            }
            else
            {
                sc = (b * e - c * d) / denom;
                tc = (a * e - b * d) / denom;
            }
            rayParam = sc;
            segmentParam = Mathf.Clamp01(tc);
            
            var closestPointOnSegment = s1 + v * segmentParam;

            var delta = closestPointOnSegment - ray.origin;
            rayParam = Vector3.Dot(delta, ray.direction);
            
            if(rayParam < 0f)
                rayParam = 0f;

            var closestPointOnRay = ray.GetPoint(rayParam);
            
            return (closestPointOnSegment - closestPointOnRay).sqrMagnitude;
        }
        
        #endregion

        #region --- DEBUG / GIZMOS ---

        private void OnDrawGizmos()
        {
            if(Nodes == null || Nodes.Count == 0) return;
            
            Gizmos.color = Color.green;
            for (var i = 0; i < Nodes.Count - 1; i++)
                Gizmos.DrawLine(Nodes[i].position, Nodes[i + 1].position);
            
            Gizmos.color = Color.red;
            foreach (var node in Nodes)
                if(node.isPinned)
                    Gizmos.DrawWireSphere(node.position, ropeRadius * 1.2f);
        }

        private void OnDrawGizmosSelected()
        {
            if(Application.isPlaying) return;
            var startPos = ropeStartPoint;
            var endPos = ropeEndPoint;

            if (Vector3.Distance(startPos, endPos) < 0.01f)
                endPos = startPos + Vector3.forward * 5f;

            var totalDistance = Vector3.Distance(startPos, endPos);
            var diameter = ropeRadius * 2f;
            var maxNodes = Mathf.FloorToInt(totalDistance / Mathf.Max(0.001f, diameter));
            var minNodes = 2;

            var nodeCount = Mathf.RoundToInt(Mathf.Lerp(minNodes, maxNodes, ropeResolution));
            if (nodeCount < 2) nodeCount = 2;

            var previewNodes = new Vector3[nodeCount];
            var direction = (endPos - startPos).normalized;
            var segmentLength = totalDistance / (nodeCount - 1);

            for (var i = 0; i < nodeCount; i++)
            {
                previewNodes[i] = startPos + direction * (segmentLength * i);
            }

            if (initialPinPoints != null)
            {
                foreach (var pin in initialPinPoints)
                {
                    if (pin.targetTransform == null) continue;
                    
                    var idx = pin.nodeIndex;
                    if (idx < 0) idx = nodeCount + idx;

                    if (idx >= 0 && idx < nodeCount)
                    {
                        previewNodes[idx] = pin.targetTransform.TransformPoint(pin.localPos);
                    }
                }
            }

            for (var i = 0; i < nodeCount; i++)
            {
                var isPinned = false;
                if (initialPinPoints != null)
                {
                    foreach (var p in initialPinPoints)
                    {
                        if (p.targetTransform == null) continue;
                        var idx = p.nodeIndex < 0 ? nodeCount + p.nodeIndex : p.nodeIndex;
                        if (idx == i) isPinned = true;
                    }
                }

                if (isPinned)
                {
                    Gizmos.color = Color.red;
                    Gizmos.DrawSphere(previewNodes[i], ropeRadius * 1.2f);
                }
                else
                {
                    Gizmos.color = new Color(0f, 1f, 0f, 0.4f);
                    Gizmos.DrawWireSphere(previewNodes[i], ropeRadius);
                }

                if (i < nodeCount - 1)
                {
                    Gizmos.color = Color.green;
                    Gizmos.DrawLine(previewNodes[i], previewNodes[i + 1]);
                }
            }
        }
        
        #endregion
    }

    public struct RopeHitInfo
    {
        public bool DidHit;
        public VerletRopeGenerator Rope;
        public Vector3 Point;
        public int NodeIndex;
        public Vector3 NodePoint;
        public float Distance;
        public float SegmentRatio;
    }
}