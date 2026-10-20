using System;

using UnityEditor;
using UnityEditor.SceneManagement;

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

using Object = UnityEngine.Object;

namespace SPACS.Graphs.Editor
{
    ///////////////////////////////////////////////////////////////////////////
    /// <summary>
    /// The editor window used to renders and manipulate a graph
    /// </summary>
    public class GraphWindow : EditorWindow
    {
        ///////////////////////////////////////////////////////////////////////////
        /// The panel that renders the graph
        public GraphPanel Panel { get; set; } = null;

        ///////////////////////////////////////////////////////////////////////////
        /// The scene component or project asset that contains the graph data
        public IContainer<IGraph> GraphContainer { get; set; }

        ///////////////////////////////////////////////////////////////////////////
        /// The key used in the editor prefs for this window. EditorPrefs are shared by
        /// every project on the machine and a window's instance ID changes every session,
        /// so the key names the project instead: the last session is found again, and no
        /// new key is left behind each time.
        public string EditorPrefWindowDataKey => GetType().Name + "." + PlayerSettings.productGUID;


        ///////////////////////////////////////////////////////////////////////////
        /// Opens the window and loads a graph
        public static GraphWindow OpenGraphViewWindow(IContainer<IGraph> graph)
        {
            GraphWindow window = GetWindow<GraphWindow>("Graph");
            window.OpenGraph(graph);
            window.Show();
            return window;
        }

        ///////////////////////////////////////////////////////////////////////////
        /// Opens a graph through its container (scene component or project asset)
        public void OpenGraph(IContainer<IGraph> graphAsset)
        {
            if (graphAsset == null)
            {
                CloseGraph();
                return;
            }

            if (Panel != null && rootVisualElement.Contains(Panel))
                rootVisualElement.Remove(Panel);

            GraphContainer = graphAsset;
            Panel = new GraphPanel(this, GraphContainer)
            {
                name = "Graph",
            };

            Panel.StretchToParentSize();
            rootVisualElement.Add(Panel);
        }

        ///////////////////////////////////////////////////////////////////////////
        /// Closes the current graph view
        private void CloseGraph()
        {
            if (Panel != null && rootVisualElement.Contains(Panel))
                rootVisualElement.Remove(Panel);
            GraphContainer = null;
            Panel = null;
        }

        ///////////////////////////////////////////////////////////////////////////
        /// Called when any scene object or project asset is selected
        private void OnSelectionChange()
        {
            Object selectedObject = Selection.activeObject;
            Panel?.SelectNodeFromContainer(selectedObject);
        }

        ///////////////////////////////////////////////////////////////////////////
        /// Called each 10 frames in the editor
        private void OnInspectorUpdate()
        {
            if (EditorApplication.isPlaying && GraphContainer != null)
            {
                Panel?.Update();
            }
        }

        ///////////////////////////////////////////////////////////////////////////
        /// Called after code recompiling or after first opening
        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            EditorSceneManager.activeSceneChangedInEditMode += OnSceneChanged;
            PrefabStage.prefabStageClosing += OnPrefabStageClosing;

            titleContent = new GUIContent("Graph", GraphSystemResources.GetTexture("icon.png"), "Graph window");
            TryRestoreLastSession();
        }

        ///////////////////////////////////////////////////////////////////////////
        /// Called when the code is recompiled
        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            EditorSceneManager.activeSceneChangedInEditMode -= OnSceneChanged;
            PrefabStage.prefabStageClosing -= OnPrefabStageClosing;

            SerializeSession();
        }

        ///////////////////////////////////////////////////////////////////////////
        /// Called when the editor's play mode state changes. An instance handler: the
        /// static one called GetWindow, which opened a graph window on every play mode
        /// change once one had been opened in the session.
        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode)
                SerializeSession();
            else if (state == PlayModeStateChange.EnteredPlayMode || state == PlayModeStateChange.EnteredEditMode)
                TryRestoreLastSession();
        }

        ///////////////////////////////////////////////////////////////////////////
        /// Called when something changes in the hierarchy
        private void OnHierarchyChanged()
        {
            Panel?.Update();
        }

        ///////////////////////////////////////////////////////////////////////////
        /// Called when a different scene is opened in the editor
        private void OnSceneChanged(Scene from, Scene to)
        {
            if (GraphContainer is MonoBehaviour)
                CloseGraph();
        }

        ///////////////////////////////////////////////////////////////////////////
        /// Called when the user is exiting from the prefab mode
        private void OnPrefabStageClosing(PrefabStage obj)
        {
            if (GraphContainer is MonoBehaviour behaviour)
                if (obj.prefabContentsRoot == behaviour.gameObject)
                    CloseGraph();
        }

        ///////////////////////////////////////////////////////////////////////////
        /// Serializes the current session (panel scale and position, current container etc.)
        private void SerializeSession()
        {
            if (GraphContainer is Object containerObject && containerObject != null && Panel != null)
            {
                // Store in the EditorPrefs the current session data
                SessionData data = new SessionData
                {
                    graphContainerID = GlobalObjectId.GetGlobalObjectIdSlow(GraphContainer as Object).ToString(),
                    position = Panel.viewTransform.position,
                    scale = Panel.viewTransform.scale
                };
                EditorPrefs.SetString(EditorPrefWindowDataKey, data.ToString());
            }
        }

        ///////////////////////////////////////////////////////////////////////////
        /// Restores last working session
        private void TryRestoreLastSession()
        {
            // Is there data to restore in the EditorPrefs?
            if (!EditorPrefs.HasKey(EditorPrefWindowDataKey))
                return;

            // Is that data valid?
            if (!SessionData.TryParse(EditorPrefs.GetString(EditorPrefWindowDataKey), out SessionData data))
                return;

            // Does the data contain a valid graph container ID?
            if (!GlobalObjectId.TryParse(data.graphContainerID, out GlobalObjectId id))
                return;

            // Restore the old view transform. A container that no longer resolves (deleted,
            // or in a scene that is not open) leaves the window as it is.
            if (!(GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) is IContainer<IGraph> container))
                return;
            OpenGraph(container);
            Panel?.UpdateViewTransform(data.position, data.scale);
        }


        ///////////////////////////////////////////////////////////////////////////
        /// Stores data relative to the actual session state
        [Serializable]
        private class SessionData : EditorSerializable<SessionData>
        {
            [SerializeField]
            public string graphContainerID;

            [SerializeField]
            public Vector3 position;

            [SerializeField]
            public Vector3 scale;
        }
    }
}