using UnityEngine;

namespace AdvancedRope
{
    public class RopeMesh
    {
        private readonly int _radialSegments;
        private readonly float _textureTiling;
        
        private VerletRopeGenerator _verletRopeGenerator;
        private MeshFilter _meshFilter;
        private Mesh _mesh;
        private Vector3[] _vertices;
        private int[] _triangles;
        private Vector2[] _uvs;
        private float _radius;

        public RopeMesh(MeshFilter meshFilter, VerletRopeGenerator verletRopeGenerator, int radialSegments = 8, float textureTiling = 1f)
        {
            _verletRopeGenerator = verletRopeGenerator;
            _meshFilter = meshFilter;
            _radialSegments = radialSegments;
            _textureTiling = textureTiling;
        }

        public void SetupRopeMesh()
        {
            _mesh = new Mesh
            {
                name = "ProceduralRope"
            };
            _mesh.MarkDynamic();

            _meshFilter.sharedMesh = _mesh;
            
            _radius = _verletRopeGenerator.ropeRadius;
            var nodes = _verletRopeGenerator.Nodes;
            var vertexCount = nodes.Count * (_radialSegments + 1) + 2;
            var triCount = (nodes.Count - 1) * _radialSegments * 6;
            var capTrisCount = (_radialSegments * 3) * 2;
            
            _vertices = new Vector3[vertexCount];
            _uvs = new Vector2[vertexCount];
            _triangles = new int[triCount + capTrisCount];
            
            var triIndex = 0;
            
            for (var i = 0; i < nodes.Count - 1; i++)
            {
                for (var j = 0; j < _radialSegments; j++)
                {
                    var current = i * (_radialSegments + 1) + j;
                    var next = current + _radialSegments + 1;

                    _triangles[triIndex++] = current;
                    _triangles[triIndex++] = current + 1;
                    _triangles[triIndex++] = next;
                    
                    _triangles[triIndex++] = next;
                    _triangles[triIndex++] = current + 1;
                    _triangles[triIndex++] = next + 1;
                }
            }

            var startCenterIndex = vertexCount - 2;
            var endCenterIndex = vertexCount - 1;
            
            // Start Cap
            for (var j = 0; j < _radialSegments; j++)
            {
                var current = j; 
                var next = j + 1;

                _triangles[triIndex++] = startCenterIndex;
                _triangles[triIndex++] = next;          
                _triangles[triIndex++] = current;
            }
            
            // End Cap
            var lastRingStart = (nodes.Count - 1) * (_radialSegments + 1);
            for (var j = 0; j < _radialSegments; j++)
            {
                var current = lastRingStart + j;
                var next = lastRingStart + j + 1;

                _triangles[triIndex++] = endCenterIndex;
                _triangles[triIndex++] = current;
                _triangles[triIndex++] = next;
            }

            _mesh.vertices = _vertices;
            _mesh.triangles = _triangles;
        }
        
        public void UpdateMesh()
        {
            UpdateMeshVertices();
        }
        public void ClearMesh()
        {
            _meshFilter.sharedMesh = null;
        }
        
        private void UpdateMeshVertices()
        {
            var verletTransform = _verletRopeGenerator.transform;
            var nodes = _verletRopeGenerator.Nodes;
            if(nodes.Count < 2) return;

            var lastRotation = Quaternion.identity;
            var lastForward = Vector3.zero;

            for (var i = 0; i < nodes.Count; i++)
            {
                Vector3 forward;
                if (i < nodes.Count - 1)
                    forward = (nodes[i + 1].position - nodes[i].position).normalized;
                else
                    forward = (nodes[i].position - nodes[i - 1].position).normalized;
                
                if (forward == Vector3.zero) forward = Vector3.up;
                
                Quaternion rotation;

                if (i == 0)
                    rotation = Quaternion.LookRotation(forward, Vector3.forward);
                else
                {
                    var swing = Quaternion.FromToRotation(lastForward, forward);
            
                    rotation = swing * lastRotation;
                }
                
                lastRotation = rotation;
                lastForward = forward;
                
                for (var j = 0; j <= _radialSegments; j++)
                {
                    var u = (float)j / _radialSegments;
                    var angle = u * Mathf.PI * 2;

                    var x = Mathf.Cos(angle) * _radius;
                    var y = Mathf.Sin(angle) * _radius;

                    var localPos = new Vector3(x, y, 0);
                    var worldPos = nodes[i].position + (rotation * localPos);

                    var meshLocalPos = verletTransform.InverseTransformPoint(worldPos);
                    
                    var vertIndex = i * (_radialSegments + 1) + j;
                    _vertices[vertIndex] = meshLocalPos;

                    var v = (float)i / (nodes.Count - 1) * _textureTiling;
                    _uvs[vertIndex] = new Vector2(u, v);
                }
            }
            
            var startCenterIndex = _vertices.Length - 2;
            var endCenterIndex = _vertices.Length - 1;

            _vertices[startCenterIndex] = verletTransform.InverseTransformPoint(nodes[0].position);
            _uvs[startCenterIndex] = new Vector2(0.5f, 0f);
            
            _vertices[endCenterIndex] = verletTransform.InverseTransformPoint(nodes[^1].position);
            _uvs[endCenterIndex] = new Vector2(0.5f, 1f * _textureTiling);
            
            _mesh.vertices = _vertices;
            _mesh.uv = _uvs;

            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }
    }
}