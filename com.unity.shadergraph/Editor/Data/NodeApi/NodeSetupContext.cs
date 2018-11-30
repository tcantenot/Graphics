using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityEditor.ShaderGraph
{
    public struct NodeSetupContext
    {
        readonly AbstractMaterialGraph m_Graph;
        readonly int m_CurrentSetupContextId;
        readonly NodeTypeState m_TypeState;

        bool m_NodeTypeCreated;

        internal bool nodeTypeCreated => m_NodeTypeCreated;

        internal NodeSetupContext(AbstractMaterialGraph graph, int currentSetupContextId, NodeTypeState typeState)
        {
            m_Graph = graph;
            m_CurrentSetupContextId = currentSetupContextId;
            m_TypeState = typeState;
            m_NodeTypeCreated = false;
        }

        public void CreateType(NodeTypeDescriptor typeDescriptor)
        {
            Validate();

            // Before doing anything, we perform validation on the provided NodeTypeDescriptor.

            // We might allow multiple types later on, or maybe it will go via another API point. For now, we only allow
            // a single node type to be provided.
            if (m_NodeTypeCreated)
            {
                throw new InvalidOperationException($"An {nameof(ShaderNodeType)} can only have 1 type.");
            }

            var i = 0;
            foreach (var portRef in typeDescriptor.inputs)
            {
                // PortRef can be 0 if the user created an instance themselves. We cannot remove the default constructor
                // in C#, so instead we let the default value represent an invalid state.
                if (!portRef.isValid || portRef.index >= m_TypeState.inputPorts.Count)
                {
                    throw new InvalidOperationException($"{nameof(NodeTypeDescriptor)}.{nameof(NodeTypeDescriptor.inputs)} contains an invalid port at index {i}.");
                }

                i++;
            }

            i = 0;
            foreach (var portRef in typeDescriptor.outputs)
            {
                if (!portRef.isValid || portRef.index >= m_TypeState.outputPorts.Count)
                {
                    throw new InvalidOperationException($"{nameof(NodeTypeDescriptor)}.{nameof(NodeTypeDescriptor.inputs)} contains an invalid port at index {i}.");
                }

                i++;
            }

            m_TypeState.type.name = typeDescriptor.name;
            // Provide auto-generated name if one is not provided.
            if (string.IsNullOrWhiteSpace(m_TypeState.type.name))
            {
                m_TypeState.type.name = m_TypeState.GetType().Name;

                // Strip "Node" from the end of the name. We also make sure that we don't strip it to an empty string,
                // in case someone decided that `Node` was a good name for a class.
                const string nodeSuffix = "Node";
                if (m_TypeState.type.name.Length > nodeSuffix.Length && m_TypeState.type.name.EndsWith(nodeSuffix))
                {
                    m_TypeState.type.name = m_TypeState.type.name.Substring(0, m_TypeState.type.name.Length - nodeSuffix.Length);
                }
            }

            m_TypeState.type.path = typeDescriptor.path;
            // Don't want nodes showing up at the root and cluttering everything.
            if (string.IsNullOrWhiteSpace(m_TypeState.type.path))
            {
                m_TypeState.type.path = "Uncategorized";
            }

            m_TypeState.type.inputs = new List<InputPortRef>(typeDescriptor.inputs);
            m_TypeState.type.outputs = new List<OutputPortRef>(typeDescriptor.outputs);

            m_NodeTypeCreated = true;
        }

        public InputPortRef CreateInputPort(int id, string displayName, PortValue value)
        {
            if (m_TypeState.inputPorts.Any(x => x.id == id) || m_TypeState.outputPorts.Any(x => x.id == id))
            {
                throw new ArgumentException($"A port with id {id} already exists.", nameof(id));
            }

            m_TypeState.inputPorts.Add(new InputPortDescriptor { id = id, displayName = displayName, value = value });
            return new InputPortRef(m_TypeState.inputPorts.Count);
        }

        public OutputPortRef CreateOutputPort(int id, string displayName, PortValueType type)
        {
            if (m_TypeState.inputPorts.Any(x => x.id == id) || m_TypeState.outputPorts.Any(x => x.id == id))
            {
                throw new ArgumentException($"A port with id {id} already exists.", nameof(id));
            }

            m_TypeState.outputPorts.Add(new OutputPortDescriptor { id = id, displayName = displayName, type = type });
            return new OutputPortRef(m_TypeState.outputPorts.Count);
        }

        void Validate()
        {
            if (m_CurrentSetupContextId != m_Graph.currentContextId)
            {
                throw new InvalidOperationException($"{nameof(NodeSetupContext)} is only valid during the call to {nameof(ShaderNodeType)}.{nameof(ShaderNodeType.Setup)} it was provided for.");
            }
        }
    }
}
