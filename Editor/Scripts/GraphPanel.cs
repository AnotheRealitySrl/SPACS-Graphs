using System;
using System.Collections.Generic;
using System.Linq;

using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;

using UnityEngine;
using UnityEngine.UIElements;

using GraphView = UnityEditor.Experimental.GraphView;
using GraphViewEdge = UnityEditor.Experimental.GraphView.Edge;
using GraphViewPort = UnityEditor.Experimental.GraphView.Port;
using Object = UnityEngine.Object;


namespace SPACS.Graphs.Editor
{
    ///////////////////////////////////////////////////////////////////////////
    /// <summary>
    /// The panel that will contain the graph and all its elements
    /// </summary>
    public class GraphPanel : GraphView.GraphView
    {
        /// The current window
        public GraphWindow Window { get; set; }

        /// The graph container (scene component or project asset)
        public IContainer<IGraph> GraphContainer { get; set; }

        /// The actual core graph element
        public IGraph Graph => GraphContainer.Value;

        /// Dictionary that maps an object to its graph visual element and vice versa
        private readonly BiDictionary<object, GraphElement> graphElementsDict = new BiDictionary<object, GraphElement>();


        #region Setup
        ///////////////////////////////////////////////////////////////////////////
        /// Constructor
        public GraphPanel(GraphWindow window, IContainer<IGraph> graphContainer)
        {
            Window = window;
            GraphContainer = graphContainer;

            // Add all graph nodes
            foreach (Node node in Graph.Nodes)
                AddNode(node, false);

            // Add all graph edges
            foreach (Node node in Graph.Nodes)
                foreach (Port port in node.OutputPorts)
                    foreach (Port port2 in port.LinkedPorts)
                        AddEdge(port, port2, false);

            // Add all graph groups
            foreach (IGroup group in Graph.Groups)
                AddGroup(group, false);


            // Setup graph interactions
            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            // Setup the node searcher and creator
            GraphNodeSearcher searcher = ScriptableObject.CreateInstance<GraphNodeSearcher>();
            searcher.Configure(window, this);
            nodeCreationRequest = context => SearchWindow.Open(new SearchWindowContext(context.screenMousePosition), searcher);

            // Setup the graph callbacks and handlers
            graphViewChanged = OnGraphViewChanged;
            serializeGraphElements = SerializeGraphElements;
            canPasteSerializedData = CanPasteSerializedData;
            unserializeAndPaste = UnserializeAndPasteOperation;

            // Setup the graph stylesheet
            styleSheets.Add(GraphSystemResources.GetStylesheet("GraphPanel"));

            // Setup grid background
            var grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();

            // Setup the toolbar
            ToolbarSetup();
        }

        ///////////////////////////////////////////////////////////////////////////
        private void ToolbarSetup()
        {
            // Create the toolbar
            Toolbar toolbar = new Toolbar();
            {
                // Create the container selector
                ObjectField containerField = new ObjectField("Graph container: ")
                {
                    objectType = typeof(Object),
                    allowSceneObjects = true
                };
                containerField.SetValueWithoutNotify(GraphContainer as Object);
                containerField.RegisterCallback<ChangeEvent<Object>>(evt =>
                {
                    if (evt.newValue is IContainer<IGraph> container)
                        Window.OpenGraph(container);
                });
                toolbar.Add(containerField);

                // Create the "Reset view" button
                Button resetViewButton = new Button { text = "Reset view" };
                resetViewButton.RegisterCallback<MouseUpEvent>(evt => UpdateViewTransform(Vector3.zero, Vector3.one));
                toolbar.Add(resetViewButton);
            }

            // Add the toolbar to the panel
            Add(toolbar);
        }
        #endregion

        #region Graph Manipulation
        ///////////////////////////////////////////////////////////////////////////
        public void AddNode(Node node, bool addAlsoToGraph = true)
        {
            NodeElement nodeElement = new NodeElement(this, node);
            AddElement(node, nodeElement);

            foreach (Port port in node.Ports)
                AddElement(port, nodeElement.PortToElement[port], false);

            if (addAlsoToGraph)
            {
                if (Graph.AddNode(node))
                    SaveGraphAsset();
            }
        }

        ///////////////////////////////////////////////////////////////////////////
        public void RemoveNode(Node node, bool removeAlsoFromGraph = true)
        {
            foreach (Port port1 in node.Ports)
                foreach (Port port2 in port1.LinkedPorts)
                {
                    if (port1 is IInputPort)
                        RemoveEdge(port1, port2, removeAlsoFromGraph);
                    else
                        RemoveEdge(port2, port1, removeAlsoFromGraph);
                }

            if (TryGetElement(node, out NodeElement nodeElement))
                RemoveElement(nodeElement);

            if (removeAlsoFromGraph)
            {
                if (Graph.RemoveNode(node))
                    SaveGraphAsset();
            }
        }

        ///////////////////////////////////////////////////////////////////////////
        public void AddEdge(Port port1, Port port2, bool addAlsoToGraph = true)
        {
            if (TryGetElement(port1, out GraphViewPort port1Element) && TryGetElement(port2, out GraphViewPort port2Element))
            {
                GraphViewEdge edgeElement = new GraphViewEdge()
                {
                    output = port1Element,
                    input = port2Element
                };

                AddEdge(port1, port2, edgeElement, addAlsoToGraph);
            }
        }

        ///////////////////////////////////////////////////////////////////////////
        public void AddEdge(Port port1, Port port2, GraphViewEdge edgeElement, bool addAlsoToGraph = true)
        {
            if (addAlsoToGraph)
            {
                Graph.AddLinkBetween(port1, port2);
                Graph.AddLinkBetween(port2, port1);
                SaveGraphAsset();
            }

            edgeElement?.input?.Connect(edgeElement);
            edgeElement?.output?.Connect(edgeElement);
            AddElement((port1, port2), edgeElement);
        }

        ///////////////////////////////////////////////////////////////////////////
        public void RemoveEdge(Port port1, Port port2, bool removeAlsoFromGraph = true)
        {
            if (TryGetElement((port1, port2), out GraphViewEdge edgeElement))
            {
                edgeElement?.input?.Disconnect(edgeElement);
                edgeElement?.output?.Disconnect(edgeElement);
                RemoveElement(edgeElement);
            }

            if (removeAlsoFromGraph)
            {
                port1.RemoveLinkTo(port2);
                port2.RemoveLinkTo(port1);
                SaveGraphAsset();
            }
        }

        ///////////////////////////////////////////////////////////////////////////
        public void AddNewGroup(DropdownMenuEventInfo eventInfo)
        {
            IGroup group = Graph.CreateGroup();
            group.GraphContainer = GraphContainer;
            group.Title = "New Group";
            group.Position = eventInfo.localMousePosition;

            foreach (NodeElement selectedNode in selection.OfType<NodeElement>())
                if (TryGetObject(selectedNode, out Node node))
                    group.AddNode(node);

            AddGroup(group);

        }

        ///////////////////////////////////////////////////////////////////////////
        public void AddGroup(IGroup group, bool addAlsoToGraph = true)
        {
            if (addAlsoToGraph)
            {
                Graph.AddGroup(group);
                SaveGraphAsset();
            }

            GroupElement groupElement = new GroupElement(this, group);

            foreach (Node node in group.InnerNodes)
                if (TryGetElement(node, out NodeElement nodeElement))
                    groupElement.AddElement(nodeElement);

            AddElement(group, groupElement);
        }

        ///////////////////////////////////////////////////////////////////////////
        public void RemoveGroup(IGroup group, bool removeAlsoFromGraph = true)
        {
            if (TryGetElement(group, out GroupElement groupElement))
                RemoveElement(groupElement);

            if (removeAlsoFromGraph)
            {
                Graph.RemoveGroup(group);
                SaveGraphAsset();
            }
        }

        ///////////////////////////////////////////////////////////////////////////
        public void RemoveNodesFromGroup(Node[] nodes)
        {
            foreach (Node node in nodes)
            {
                if (TryGetElement(node, out NodeElement nodeElement))
                {
                    IGroup group = Graph.FindContainingGroup(node);
                    if (group != null && TryGetElement(group, out GroupElement groupElement))
                        groupElement.RemoveElement(nodeElement);
                }
            }
        }
        #endregion

        #region Selection
        ///////////////////////////////////////////////////////////////////////////
        public void SelectNode(Node nodeToSelect)
        {
            foreach (Node node in GraphContainer.Value.Nodes)
            {
                Object nodeContainer = GetContainer(node);
                SelectNodeAndContainer(node, nodeContainer, node == nodeToSelect);
            }
        }

        ///////////////////////////////////////////////////////////////////////////
        public void SelectNodeFromContainer(Object selectedObject)
        {
            foreach (Node node in GraphContainer.Value.Nodes)
            {
                Object nodeContainer = GetContainer(node);
                SelectNodeAndContainer(node, nodeContainer, nodeContainer == selectedObject);
            }
        }

        ///////////////////////////////////////////////////////////////////////////
        public Node[] GetSelectedNodes() => selection.OfType<NodeElement>()
                .Select(e => e.Node)
                .ToArray();

        ///////////////////////////////////////////////////////////////////////////
        private void SelectNodeAndContainer(Node node, Object nodeContainer, bool select = true)
        {
            if (TryGetElement(node, out NodeElement nodeElement))
            {
                nodeElement.selected = select;
                if (select)
                    Selection.activeObject = nodeContainer;
            }
        }
        #endregion

        #region Handlers
        ///////////////////////////////////////////////////////////////////////////
        public override List<GraphViewPort> GetCompatiblePorts(GraphViewPort startPort, NodeAdapter nodeAdapter)
        {
            var compatiblePorts = new List<GraphViewPort>();

            ports.ForEach(port =>
            {
                if (startPort != port && startPort.node != port.node && startPort.direction != port.direction)
                    compatiblePorts.Add(port);
            });

            return compatiblePorts;
        }

        ///////////////////////////////////////////////////////////////////////////
        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            base.BuildContextualMenu(evt);
            evt.menu.InsertAction(1, "Create Group", e => AddNewGroup(e.eventInfo), DropdownMenuAction.Status.Normal);
        }

        ///////////////////////////////////////////////////////////////////////////
        private GraphViewChange OnGraphViewChanged(GraphViewChange changes)
        {
            if (changes.elementsToRemove != null)
                foreach (var removedElement in changes.elementsToRemove)
                {
                    if (removedElement is GraphView.Node removedNode && TryGetObject(removedNode, out Node node))
                        RemoveNode(node);
                    else if (removedElement is GraphViewEdge removedEdge && TryGetObject(removedEdge, out (Port, Port) edge))
                        RemoveEdge(edge.Item1, edge.Item2);
                    else if (removedElement is GraphView.Group removedGroup && TryGetObject(removedGroup, out IGroup group))
                        RemoveGroup(group);
                }

            if (changes.movedElements != null)
                foreach (var movedElement in changes.movedElements)
                {
                    Vector2 position = movedElement.GetPosition().position;
                    if (movedElement is NodeElement movedNode)
                    {
                        movedNode.Node.Position = position;
                        IGroup group = Graph.FindContainingGroup(movedNode.Node);
                        if (group != null && TryGetElement(group, out GroupElement groupElement))
                            group.Position = groupElement.GetPosition().position;
                    }
                    else if (movedElement is GroupElement movedGroup)
                    {
                        IGroup group = movedGroup.Group;
                        group.Position = position;
                        foreach (Node node in group.InnerNodes)
                            if (TryGetElement(node, out NodeElement nodeElement))
                                node.Position = nodeElement.GetPosition().position;
                    }
                }

            if (changes.edgesToCreate != null)
                foreach (var edgeToCreate in changes.edgesToCreate)
                    if (edgeToCreate.input.node is NodeElement inputNode && edgeToCreate.output.node is NodeElement outputNode)
                        if (TryGetObject(edgeToCreate.input, out Port port1) && TryGetObject(edgeToCreate.output, out Port port2))
                            AddEdge(port1, port2, edgeToCreate);

            SaveGraphAsset();
            return changes;
        }

        ///////////////////////////////////////////////////////////////////////////
        public void Update()
        {
            if (GraphContainer == null || (GraphContainer is Object containerObject && containerObject == null))
                return;

            foreach (Node node in Graph.Nodes)
            {
                bool nodeElementExists = TryGetElement(node, out NodeElement nodeElement);
                bool nodeContainerExists = Graph.TryGetContainer(node, out IContainer<Node> nodeContainer);
                if (nodeElementExists && nodeContainerExists)
                {
                    nodeElement.UpdateElement();

                    // Ensure that the node name is the same as its container
                    if (nodeContainer is Object objectContainer)
                    {
                        string objectName = objectContainer.name;
                        if (objectName != node.Name)
                        {
                            node.Name = objectName;
                            nodeElement.title = objectName;
                        }
                    }
                }
                else
                    RemoveNode(node, true);
            }
        }

        ///////////////////////////////////////////////////////////////////////////
        public void SaveGraphAsset()
        {
            // Called on every node move, link and field edit. It used to refresh the asset
            // database and save every asset each time, which made the panel stutter: now the
            // container is marked dirty at once (a scene container dirties its scene), and an
            // asset container is written to disk once, on the next editor tick.
            if (GraphContainer is Object containerObject && containerObject != null)
                EditorUtility.SetDirty(containerObject);

            if (saveScheduled)
                return;
            saveScheduled = true;
            EditorApplication.delayCall += FlushGraphAssetSave;
        }

        private bool saveScheduled;

        private void FlushGraphAssetSave()
        {
            saveScheduled = false;
            if (GraphContainer is Object containerObject && containerObject != null && EditorUtility.IsPersistent(containerObject))
                AssetDatabase.SaveAssetIfDirty(containerObject);
        }
        #endregion

        #region Copy and paste
        ///////////////////////////////////////////////////////////////////////////
        private new string SerializeGraphElements(IEnumerable<GraphElement> elements)
        {
            CopyPasteData copyPasteData = new CopyPasteData
            {
                nodesData = elements
                    .Select(e => TryGetObject(e, out Node node) ? node : null)
                    .Where(n => n != null)
                    .Select(n => new CopyPasteData.NodeData()
                    {
                        serializedNode = EditorJsonUtility.ToJson(n),
                        serializedNodeType = n.GetType().AssemblyQualifiedName
                    })
                    .ToArray()
            };
            return copyPasteData.ToString();
        }

        ///////////////////////////////////////////////////////////////////////////
        protected new bool CanPasteSerializedData(string data)
        {
            return CopyPasteData.TryParse(data, out CopyPasteData copyPasteData)
                && copyPasteData.nodesData != null
                && copyPasteData.nodesData.Length > 0;
        }

        ///////////////////////////////////////////////////////////////////////////
        private new void UnserializeAndPasteOperation(string operationName, string data)
        {
            CopyPasteData.TryParse(data, out CopyPasteData copyPasteData);
            foreach (CopyPasteData.NodeData nodeData in copyPasteData.nodesData)
            {
                Type nodeType = Type.GetType(nodeData.serializedNodeType);
                Node node = Node.GenerateNode(nodeType);

                if (node != null)
                {
                    string keepGuid = node.Guid;
                    EditorJsonUtility.FromJsonOverwrite(nodeData.serializedNode, node);
                    node.Guid = keepGuid;

                    node.Position += new Vector2(20, 20);
                    AddNode(node, true);
                }
            }
        }

        ///////////////////////////////////////////////////////////////////////////
        /// Stores data copied and pasted inside the Graph Panel
        [Serializable]
        public class CopyPasteData : EditorSerializable<CopyPasteData>
        {
            ///////////////////////////////////////////////////////////////////////////
            /// Data relative to a copied node
            [Serializable]
            public class NodeData
            {
                [SerializeField]
                public string serializedNode;

                [SerializeField]
                public string serializedNodeType;
            }

            ///////////////////////////////////////////////////////////////////////////
            /// The list of copied nodes
            [SerializeField]
            public NodeData[] nodesData;
        }
        #endregion

        #region Elements and Containers
        ///////////////////////////////////////////////////////////////////////////
        private void AddElement(object coreObject, GraphElement element, bool addToGraphView = true)
        {
            if (addToGraphView)
                AddElement(element);
            graphElementsDict[coreObject] = element;
        }

        ///////////////////////////////////////////////////////////////////////////
        public bool TryGetObject<T>(GraphElement element, out T obj)
        {
            if (element != null && graphElementsDict.Reverse.TryGetValue(element, out object o))
            {
                if (o is T casted)
                {
                    obj = casted;
                    return true;
                }
            }

            obj = default;
            return false;
        }

        ///////////////////////////////////////////////////////////////////////////
        public bool TryGetElement<T>(object obj, out T element) where T : GraphElement
        {
            if (obj != null && graphElementsDict.TryGetValue(obj, out GraphElement ge))
            {
                if (ge is T casted)
                {
                    element = casted;
                    return true;
                }
            }

            element = default;
            return false;
        }

        ///////////////////////////////////////////////////////////////////////////
        private Object GetContainer(Node node)
        {
            Graph.TryGetContainer(node, out IContainer<Node> nodeContainer);
            Object containerObject = nodeContainer as Object;
            if (containerObject != null && containerObject is Component containerComponent)
                containerObject = containerComponent.gameObject;
            return containerObject;
        }
        #endregion
    }
}