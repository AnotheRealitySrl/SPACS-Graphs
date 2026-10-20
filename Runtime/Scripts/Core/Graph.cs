
using System;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;


using SPACS.Utilities;

namespace SPACS.Graphs
{
    ///////////////////////////////////////////////////////////////////////////
    /// <summary>
    /// Abstract class used as base implementation of the IGraph interface
    /// </summary>
    [Serializable]
    public abstract class Graph<TNodeContainer, TGroup, TPortReference> : IGraph
        where TNodeContainer : IContainer<Node>
        where TGroup : IGroup
        where TPortReference : IPortReference
    {

        [SerializeReference, Tooltip("The list of nodes containers in this graph")]
        internal List<TNodeContainer> nodes;

        [SerializeReference, HideInInspector]
        internal List<TGroup> groups;


        ///////////////////////////////////////////////////////////////////////////
        /// <summary>The collection of nodes that this graph contains</summary>
        public IReadOnlyCollection<Node> Nodes
        {
            get
            {
                // Read on every panel update and every lookup: drop the null entries in place
                // (no new list unless there are some) and copy the values in one pass.
                if (nodes == null)
                    nodes = new List<TNodeContainer>();
                else
                    nodes.RemoveAll(r => r == null);

                List<Node> result = new List<Node>(nodes.Count);
                for (int i = 0; i < nodes.Count; i++)
                    result.Add(nodes[i].Value);
                return result;
            }
        }

        ///////////////////////////////////////////////////////////////////////////
        /// <summary>The collection of groups that this graph contains</summary>
        public IReadOnlyCollection<IGroup> Groups
        {
            get
            {
                if (groups == null)
                    groups = new List<TGroup>();
                return groups.Where(g => g != null).Cast<IGroup>().ToList();
            }
        }

        ///////////////////////////////////////////////////////////////////////////
        /// <summary>Creates a container for a specific node</summary>
        public abstract TNodeContainer CreateContainer(Node node);

        ///////////////////////////////////////////////////////////////////////////
        /// <summary>Adds a node</summary>
        /// <param name="node">The node to add</param>
        /// <returns>The successfulness of the operation</returns>
        public virtual bool AddNode(Node node)
        {
            TNodeContainer nodeContainer = CreateContainer(node);
            bool validContainer = nodeContainer != null;
            if (validContainer)
                nodes.Add(nodeContainer);
            return validContainer;
        }

        ///////////////////////////////////////////////////////////////////////////
        /// <summary>Renames a node</summary>
        /// <param name="node">The node to be renamed</param>
        /// <param name="newName">The new name to assign</param>
        /// <returns>True if the renaming was successful. False if the specified
        /// node is invalid or not contained in this graph</returns>
        public virtual bool RenameNode(Node node, string newName)
        {
            bool validContainer = TryGetContainer(node, out TNodeContainer _);
            if (validContainer)
                node.Name = newName;
            return validContainer;
        }

        ///////////////////////////////////////////////////////////////////////////
        /// <summary>Removes a node from the graph</summary>
        /// <param name="node">The node to remove</param>
        /// <returns>True if the removal was successful. False if the specified
        /// node is not valid or not contained in this graph</returns>
        public virtual bool RemoveNode(Node node)
        {
            if (TryGetContainer(node, out TNodeContainer nodeContainer))
                return nodes.Remove(nodeContainer);
            return false;
        }

        ///////////////////////////////////////////////////////////////////////////
        /// <summary>Retrieves the nodes of a specified type</summary>
        /// <typeparam name="T">A node subclass type</typeparam>
        /// <returns>A collection of nodes</returns>
        public IReadOnlyCollection<T> GetNodes<T>() where T : Node => Nodes
            .Where(node => node is T)
            .Cast<T>()
            .ToList();


        ///////////////////////////////////////////////////////////////////////////
        /// <summary>Links two ports together</summary>
        /// <param name="port1">The output port</param>
        /// <param name="port2">The input port</param>
        /// <returns>True if the link was successful. False if the specified
        /// ports are not valid or not contained in this graph</returns>
        public virtual bool AddLinkBetween(Port port1, Port port2)
        {
            bool validContainer = TryGetContainer(port2.OwnerNode, out TNodeContainer nodeContainer);
            if (validContainer)
            {
                TPortReference portReference = Activator.CreateInstance<TPortReference>();
                portReference.NodeContainer = nodeContainer;
                portReference.Port = port2;
                port1.AddLinkTo(portReference);
            }
            return validContainer;
        }


        ///////////////////////////////////////////////////////////////////////////
        /// <summary>Creates a group for this graph</summary>
        /// <returns>The created group</returns>
        public IGroup CreateGroup()
        {
            TGroup group = (TGroup)Activator.CreateInstance(typeof(TGroup));
            return group;
        }

        ///////////////////////////////////////////////////////////////////////////
        /// <summary>Adds a group to this graph</summary>
        /// <param name="group">The group to add</param>
        public void AddGroup(IGroup group)
        {
            if (group is TGroup gi)
                groups.Add(gi);
        }

        ///////////////////////////////////////////////////////////////////////////
        /// <summary>Removes a group from this graph</summary>
        /// <param name="group">The group to remove</param>
        public void RemoveGroup(IGroup group)
        {
            if (group is TGroup gi)
                groups.Remove(gi);
        }

        ///////////////////////////////////////////////////////////////////////////
        /// <summary>Finds the group that contains a specified node</summary>
        /// <param name="node">The node to search for</param>
        /// <returns>The containing group</returns>
        public IGroup FindContainingGroup(Node node) => Groups
            .Where(group => group.InnerNodes.Contains(node))
            .FirstOrDefault();

        ///////////////////////////////////////////////////////////////////////////
        /// <summary>Tries to find the container for a specified node</summary>
        /// <param name="node">The node to search for</param>
        /// <param name="container">The output container</param>
        /// <returns>The successfulness of the search</returns>
        public bool TryGetContainer(Node node, out TNodeContainer container)
        {
            container = default;
            if (nodes != null)
            {
                for (int i = 0; i < nodes.Count; i++)
                {
                    TNodeContainer candidate = nodes[i];
                    if (candidate != null && candidate.Value == node)
                    {
                        container = candidate;
                        break;
                    }
                }
            }
            if (container is UnityEngine.Object containerObject)
                return containerObject != null;
            return container != null;
        }

        ///////////////////////////////////////////////////////////////////////////
        /// <summary>Tries to find the container for a specified node</summary>
        /// <param name="node">The node to search for</param>
        /// <param name="container">The output container</param>
        /// <returns>The successfulness of the search</returns>
        public bool TryGetContainer(Node node, out IContainer<Node> container)
        {
            container = null;
            if (TryGetContainer(node, out TNodeContainer castReference))
                container = castReference;
            return container != null;
        }

        ///////////////////////////////////////////////////////////////////////////
        /// <summary>Returns the type of the container of the node</summary>
        /// <param name="node">A node</param>
        /// <returns>The container type</returns>
        public Type GetContainerType(Node node)
        {
            Type nodeType = node.GetType();
            Type containerInterface = typeof(IContainer<>).MakeGenericType(nodeType);

            Type[] availableContainerTypes = ReflectionUtilities.GetAssignableClasses(typeof(TNodeContainer), containerInterface).ToArray();
            int containersCount = availableContainerTypes.Length;

            if (containersCount == 0)
            {
                Debug.LogWarning($"Cannot find a suitable container for type {nodeType.Name}");
                return null;
            }

            Type containerType = availableContainerTypes[0];


            if (containersCount > 1)
            {
                Debug.LogWarning($"{containersCount} containers have been found for type {nodeType.Name}." +
                    $"Using the first one: {containerType.Name}");

                containerType = availableContainerTypes[0];

            }

            return containerType;
        }
    }
}