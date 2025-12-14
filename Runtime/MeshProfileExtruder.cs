using System.Collections.Generic;
using FS.MeshProcessing.Utility;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;
using UnityEngine.ProBuilder;
using UnityEngine.Splines;

#if UNITY_EDITOR
using UnityEditor.ProBuilder;
#endif

namespace FS.MeshProcessing
{
    [SelectionBase]
    [ExecuteAlways]
    public abstract class MeshProfileExtruder : MonoBehaviour
    {
        private const string k_meshGenName = "_GENERATED_";
        
        [SerializeField, Required] 
        protected MeshProfileConfig m_meshProfile;
        public MeshProfileConfig MeshProfile
        {
            get => m_meshProfile;
            set
            {
                if (m_meshProfile == value) return;
                m_meshProfile = value;
                if (m_meshProfile != null)
                {
                    m_generatedMeshPhysicsLayer = m_meshProfile.m_defaultPhysicsLayer;
                    m_generatedMeshTag = m_meshProfile.m_defaultTag;
                    m_generateVertexPath = m_meshProfile.m_generateVertexPath;
                    m_generateCollision = m_meshProfile.m_generateCollision;
                }
                m_needsRegenerationFull = true;
            }
        }
        
        [SerializeField] 
        private bool m_realTimeUpdate = true;
        
        [TabGroup("Game Object Settings")]
        [SerializeField, GameObjectTags] 
        private string m_generatedMeshTag = "Untagged";
        [TabGroup("Game Object Settings")]
        [SerializeField, PhysicsLayer]
        private string m_generatedMeshPhysicsLayer = "Default";
        [TabGroup("Game Object Settings")]
        [SerializeField, PreviewField] private Material[] m_overrideMaterials;
        
        [TabGroup("Generated Mesh Settings")]
        [SerializeField] private bool m_flipNormals;
        [TabGroup("Generated Mesh Settings")]
        [SerializeField] private bool m_hideStartCap;
        [TabGroup("Generated Mesh Settings")]
        [SerializeField] private bool m_hideEndCap;
        [TabGroup("Generated Mesh Settings")]
        [SerializeField] private bool m_snapToGround;

        [TabGroup("Additional Features")]
        [SerializeField] private bool m_generateVertexPath;
        [TabGroup("Additional Features")]
        [SerializeField] private bool m_generateCollision;
        [TabGroup("Additional Features")]
        [SerializeField, EnableIf("m_generateCollision")]
        private bool m_isTrigger;

        [SerializeField] protected GameObject m_generatedMesh;
        [SerializeField] protected GameObject m_editableProfileObject;
        
        [SerializeField, HideInInspector] private Mesh m_mesh;
        public Mesh GeneratedMesh => m_mesh;
        
        protected List<Matrix4x4> m_extrusionSegments = new();
        
        private static Material m_previewMaterialInst;
        protected static Material m_previewMaterial
        {
            get 
            {
                if (m_previewMaterialInst == null || m_previewMaterialInst.shader == null)
                {
                    if (m_previewMaterialInst != null) m_previewMaterialInst.shader = Shader.Find("Hidden/MeshProfilePreview");
                    else m_previewMaterialInst = new Material(Shader.Find("Hidden/MeshProfilePreview"));
                    m_previewMaterialInst.hideFlags = HideFlags.HideAndDontSave;
                }
                return m_previewMaterialInst;
            }
        }

        // Recalculates extrusion matrices
        protected bool m_needsRegenerationFull;
        // Doesnt recalculate extrusion matrices
        protected bool m_needsRegenerationPartial;

        protected bool m_needsProfileReset = false;
        
        // https://docs.unity3d.com/6000.1/Documentation/Manual/configurable-enter-play-mode-details.html
        
        #region MONOBEHAVIOR
        
        private void Awake()
        {
            if (Application.isPlaying) return;

            m_mesh = null;
            EnsureMeshAssetCreation();
            //if (m_mesh == null)
            {
                m_mesh = new Mesh();
                m_mesh.name = $"PCG_{gameObject.name}";
                m_needsRegenerationFull = true;
            }
        }

        private void EnsureMeshAssetCreation()
        {
            if (m_mesh == null)
            {
                m_mesh = new Mesh();
                m_mesh.name = $"PCG_{gameObject.name}";
                m_needsRegenerationFull = true;
            }
        }
        
        private void OnDestroy()
        {
            DestroyImmediate(m_mesh);
            
            if (m_generatedMesh != null)
                DestroyImmediate(m_generatedMesh);
            
            if (m_editableProfileObject != null)
                DestroyImmediate(m_editableProfileObject);
        }
        
        private void OnValidate()
        {
#if UNITY_EDITOR            
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
#endif

            m_needsRegenerationFull = true;
        }

        protected virtual void Update()
        {
            // TODO: This keeps updating in playmode for some reason, 
            if (Application.isPlaying) return;
            
            EnsureMeshAssetCreation();
            
            if (transform.hasChanged)
            {
                m_needsRegenerationPartial = true;
                transform.hasChanged = false;
            }
            
            if (m_needsProfileReset)
            {
                ResetProfile();
                m_needsProfileReset = false;
            }

            if (m_needsRegenerationFull)
            {
                GenerateMesh(true);
                m_needsRegenerationFull = false;
                m_needsRegenerationPartial = false;
            }
            else if (m_needsRegenerationPartial)
            {
                GenerateMesh(false);
                m_needsRegenerationPartial = false;
            }
        }

        protected virtual void OnEnable()
        {
            MeshProfileConfig.OnModified += OnMeshProfileModified;
            TransformChangedDetector.OnTransformChanged += OnProfileTransformModified;
#if UNITY_EDITOR            
            ProBuilderEditor.afterMeshModification += OnProfileMeshModified;
#endif            
        }

        protected virtual void OnDisable()
        {
            MeshProfileConfig.OnModified -= OnMeshProfileModified;
            TransformChangedDetector.OnTransformChanged -= OnProfileTransformModified;
#if UNITY_EDITOR            
            ProBuilderEditor.afterMeshModification -= OnProfileMeshModified;
#endif            
        }

        #endregion
        
        #region EVENT LISTENERS
        private void OnMeshProfileModified(MeshProfileConfig profile)
        {
            if (profile != m_meshProfile) return;

            m_needsRegenerationPartial = true;
        }
        
        private void OnProfileTransformModified(GameObject profileObject)
        {
            if (profileObject != m_editableProfileObject) return;

            ValidateProfilesTransform();
            m_needsRegenerationPartial = true;
        }
        
        private void OnProfileMeshModified(IEnumerable<ProBuilderMesh> profileMesh)
        {
            foreach (var mesh in profileMesh)
            {
                if (mesh.gameObject != m_editableProfileObject) continue;
                
                m_needsRegenerationPartial = true;
            }
        }
        #endregion
        
        public void GenerateMesh(bool regenMatrices)
        {
            if (m_meshProfile == null || m_meshProfile.ProfileMesh == null) return;
            
            if (regenMatrices) GenerateExtrusionMatrices();

            if (m_extrusionSegments.Count == 0) return;
            
            EnsureProfilePreviewIsValid();
            EnsureGeneratedMeshIsValid();
            
            ValidateOverrideMaterials();

            SetupVertexPath();
            
            SetupCollisionMesh();
            
            ExtrudeProfile();
        }
        
        #region VALIDATION FUNCTIONS
        
        private void EnsureGeneratedMeshIsValid()
        {
            // If it doesnt exist, create it
            if (m_generatedMesh == null)
            {
                m_generatedMesh = new GameObject($"{k_meshGenName}{m_meshProfile.name}");
                m_generatedMesh.transform.SetParent(transform, false);
                m_generatedMesh.AddComponent<MeshFilter>().sharedMesh = m_mesh;
                m_generatedMesh.AddComponent<MeshRenderer>();
            }

            // In case it was removed, lets just ensure they exist again
            if (!m_generatedMesh.GetOrAddComponent<MeshFilter>(out var meshFilter))
                meshFilter.sharedMesh = GeneratedMesh;
            else if (meshFilter.sharedMesh != GeneratedMesh) meshFilter.sharedMesh = GeneratedMesh;
            m_generatedMesh.GetOrAddComponent<MeshRenderer>();
            
            // Ensure proper Tag & Physics Layer
            m_generatedMesh.tag = m_generatedMeshTag;
            m_generatedMesh.layer = LayerMask.NameToLayer(m_generatedMeshPhysicsLayer);
            
            // Transform must be zero as well as identity
            m_generatedMesh.transform.localPosition = Vector3.zero;
            m_generatedMesh.transform.localRotation = Quaternion.identity;
            m_generatedMesh.transform.localScale = Vector3.one;
        }

        public void ResetProfile()
        {
            Debug.LogError("RESETTING PROFILE!");
            if (m_editableProfileObject != null)
            {
                if (Application.isPlaying) Destroy(m_editableProfileObject);
                else DestroyImmediate(m_editableProfileObject);
                m_editableProfileObject = null;
                
            }

            if (m_generatedMesh != null)
            {
                if (Application.isPlaying) Destroy(m_generatedMesh);
                else DestroyImmediate(m_generatedMesh);

                m_generatedMesh = null;
            }

            m_needsRegenerationPartial = true;
        }

        private void EnsureProfilePreviewIsValid()
        {
            // Create Object if needed
            if (m_editableProfileObject == null)
            {
                m_editableProfileObject = new GameObject();

                m_editableProfileObject.name = $"_PROFILE_{m_meshProfile.name}";
                m_editableProfileObject.tag = "EditorOnly";
            }

            if (m_editableProfileObject.transform.parent != transform)
            {
                m_editableProfileObject.transform.SetParent(transform, false);
                ValidateProfilesTransform();
            }
            
            // In case it was removed, lets just ensure they exist again
            var meshFilter = m_editableProfileObject.GetOrAddComponent<MeshFilter>();
            var meshRenderer = m_editableProfileObject.GetOrAddComponent<MeshRenderer>();
            m_editableProfileObject.GetOrAddComponent<TransformChangedDetector>();
            
            if (m_meshProfile != null && meshFilter.sharedMesh == null)
                meshFilter.sharedMesh = m_meshProfile.ProfileMesh;

            meshRenderer.sharedMaterial = m_previewMaterial;
        }

        // TODO: This needs to be fixed up, allowing for planar transforms and the like
        protected void ValidateProfilesTransform()
        {
            if (m_extrusionSegments.Count == 0 || !m_editableProfileObject) return;

            var firstMatrix = m_extrusionSegments[0];
            m_editableProfileObject.transform.localRotation = firstMatrix.rotation * m_meshProfile.m_rotation;

            var tangent = firstMatrix.MultiplyVector(Vector3.forward);
            var localPosOnTangentPlane = Vector3.ProjectOnPlane(m_editableProfileObject.transform.localPosition, tangent);
            m_editableProfileObject.transform.localPosition = -tangent * 0.1f;
            //profileObject.transform.localPosition += localPosOnTangentPlane;
            m_editableProfileObject.transform.localPosition = firstMatrix.MultiplyPoint(m_editableProfileObject.transform.localPosition);
        }
        
        private void ValidateOverrideMaterials()
        {
            if (m_meshProfile == null) return;
            
            // Validate materials
            if (m_overrideMaterials == null || m_overrideMaterials.Length != m_meshProfile.m_defaultMaterials.Length)
            {
                m_overrideMaterials = new Material[m_meshProfile.m_defaultMaterials.Length];
            }
            for (int m = 0; m < m_meshProfile.m_defaultMaterials.Length; m++)
            {
                if (m_overrideMaterials[m] == null)
                    m_overrideMaterials[m] = m_meshProfile.m_defaultMaterials[m];
            }
        }

        private bool ValidateComponentOnGenMesh<T>(bool shouldHave) where T : Component
        {
            // If we shouldnt generate this component, remove the component if it exists
            if (!shouldHave)
            {
                if (m_generatedMesh.TryGetComponent<T>(out var comp))
                {
                    if (Application.isPlaying) Destroy(comp);
                    else DestroyImmediate(comp);
                }
                return false; // Comp shouldnt exist
            }
            
            // Otherwise ensure it exists
            m_generatedMesh.GetOrAddComponent<T>();
            return true; // Comp exists
        }

        private void SetupVertexPath() => ValidateComponentOnGenMesh<MeshVertexPath>(m_generateVertexPath);
        private void SetupCollisionMesh()
        {
            if (ValidateComponentOnGenMesh<MeshCollider>(m_generateCollision))
            {
                // Ensure collider mesh is properly setup
                var meshCollider = m_generatedMesh.GetComponent<MeshCollider>();
                if (meshCollider.sharedMesh != GeneratedMesh)
                    meshCollider.sharedMesh = GeneratedMesh;

                if (m_isTrigger)
                {
                    meshCollider.isTrigger = m_isTrigger;
                    meshCollider.convex = true;
                }
                else
                {
                    meshCollider.isTrigger = false;
                    meshCollider.convex = false;
                }

                m_generatedMesh.isStatic = true;
            }
        }
        #endregion

        #region MESH GENERATION        
        protected abstract void GenerateExtrusionMatrices();
        protected virtual void EvaluateVertexPath(MeshVertexPath vertexPath) {}
        
        /// <summary>
        /// Allows you to modify the extrusion settings before the extrusion is performed. For example, for a spline extrusion,
        /// you might want to disable end caps if the spline is closed
        /// </summary>
        protected virtual void ModifyExtrusionSettings(ref MeshProcessing.ExtrusionSettings extrusionSettings) {}

        private RaycastHit[] m_groundHits = new RaycastHit[10];
        protected Matrix4x4 ApplyGroundSnappingToMatrix(Matrix4x4 matrix)
        {
            if (!m_snapToGround) return matrix;

            var worldMatrix = transform.localToWorldMatrix * matrix;
            
            var mesh = m_editableProfileObject.GetComponent<MeshFilter>().sharedMesh;
            
            // First we consider local space to account for the configs rotation offset
            var localCenter = mesh.bounds.center;
            var localExtents = mesh.bounds.extents;
            var localSnappingPoint = mesh.vertices[m_meshProfile.m_groundSnappingPivotIdx];
            var localTopPivot = new Vector3(localSnappingPoint.x, localCenter.y + localExtents.y, localSnappingPoint.z);

            var snapTranslation = Matrix4x4.Translate(new Vector3(0, -localTopPivot.y, 0));
            
            // Then we raycast from world space
            var snapPointWS = worldMatrix.MultiplyPoint(localTopPivot);
            var raycastStart = worldMatrix.MultiplyPoint(localTopPivot);
            var raycastDir = worldMatrix.MultiplyVector(Vector3.down);
            
            float extentsWS = 2f * Vector3.Dot(worldMatrix.MultiplyVector(localExtents), raycastDir);
            
            Matrix4x4 scaleMatrix = Matrix4x4.identity;
            
            Ray ray = new Ray(raycastStart, raycastDir);

            // Ignore self collision
            float closestHitDist = Mathf.Infinity;
            if (Physics.RaycastNonAlloc(ray, m_groundHits, Mathf.Infinity) > 0)
            {
                foreach (var hit in m_groundHits)
                {
                    if (hit.collider != null && m_generatedMesh != null && hit.collider.gameObject == m_generatedMesh) continue;

                    var hitDist = Vector3.Distance(hit.point, snapPointWS);
                    if (hitDist < closestHitDist)
                    {
                        // We need to apply the translation to the matrix
                        float dist = Vector3.Dot(snapPointWS - hit.point, raycastDir);
                        float scaleFactor = dist / extentsWS;

                        scaleMatrix = Matrix4x4.Scale(new Vector3(1, scaleFactor, 1));
                        closestHitDist = hitDist;
                    }
                }
            }
            
            // Need to have a selectable vertex as well for which one to apply ground snapping to (or better yet, one of the corners of the bounds?)
            return matrix * (snapTranslation.inverse * scaleMatrix * snapTranslation);
        }

        private Matrix4x4[] GetFinalExtrusionMatrices()
        {
            // Apply ground snapping & any local transforms here for the given profile
            // Modify extrusion matrices to take into account local transforms
            Matrix4x4[] extrusionMatrices = m_extrusionSegments.ToArray();
            for (int i = 0; i < extrusionMatrices.Length; i++)
            {
                var localOffset = Vector3.zero;//m_editableProfileObject.transform.localPosition;
                localOffset.z = 0;
                Matrix4x4 localTransforms = Matrix4x4.TRS(localOffset, m_meshProfile.m_rotation, m_editableProfileObject.transform.localScale);
                extrusionMatrices[i] *= localTransforms;
                extrusionMatrices[i] = ApplyGroundSnappingToMatrix(extrusionMatrices[i]);
            }

            return extrusionMatrices;
        }
        
        private void ExtrudeProfile()
        {
            if (!isActiveAndEnabled) return;
            if (m_extrusionSegments.Count == 0) return;
            
            // Modify extrusion matrices to take into account local transforms
            Matrix4x4[] extrusionMatrices = GetFinalExtrusionMatrices();
            
            
#if UNITY_EDITOR
            // We only want to dirty the object if the extrusion was triggered by an actual modification. Otherwise the scene always gets dirty on playmode changes we we trigger OnValidate and extrude the profile, always setting it to dirty.
            bool isDirty = UnityEditor.EditorUtility.IsDirty(this); 
            if (!Application.isPlaying && isDirty) Undo.RecordObject(m_editableProfileObject, $"Extruded Profile Mesh {m_editableProfileObject.name}");
#endif 
            
            // Perform extrusion TODO: UVS!
            var extrusionSettings = new MeshProcessing.ExtrusionSettings(!m_hideStartCap, !m_hideEndCap, m_flipNormals, Vector2.one, Vector2.zero);
            ModifyExtrusionSettings(ref extrusionSettings);
            MeshProcessing.ExtrudeMesh(m_editableProfileObject.GetComponent<MeshFilter>().sharedMesh, m_mesh, extrusionMatrices, extrusionSettings);
            
            // Generate vertex path
            var vertexPath = m_generatedMesh.GetComponent<MeshVertexPath>();
            if (m_generateVertexPath)
            {
                vertexPath ??= m_generatedMesh.AddComponent<MeshVertexPath>();
                BezierKnot[] vertexKnots = new BezierKnot[extrusionMatrices.Length];
                var pathPoint = m_editableProfileObject.GetComponent<MeshFilter>().sharedMesh.vertices[m_meshProfile.m_vertexPathIdx];
                for (int v = 0; v < extrusionMatrices.Length; v++)
                {
                    vertexKnots[v] = new BezierKnot(extrusionMatrices[v].MultiplyPoint(pathPoint));
                    vertexKnots[v].Rotation = extrusionMatrices[v].rotation * Quaternion.Inverse(m_meshProfile.m_rotation);
                }
                vertexPath.SetLocalSpaceVertexPath(vertexKnots);
                EvaluateVertexPath(vertexPath);
            }
            
            // Set Materials
            m_generatedMesh.GetComponent<MeshRenderer>().materials = m_overrideMaterials;

#if UNITY_EDITOR
            if (!Application.isPlaying && isDirty)
            {
                UnityEditor.EditorUtility.SetDirty(m_editableProfileObject);
            }
#endif
        }
        
        #endregion
        
    }
}