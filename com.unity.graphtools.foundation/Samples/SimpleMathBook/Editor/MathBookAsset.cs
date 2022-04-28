using System;
using UnityEditor.Callbacks;
using UnityEditor.GraphToolsFoundation.Overdrive.BasicModel;
using UnityEngine.GraphToolsFoundation.CommandStateObserver;

namespace UnityEditor.GraphToolsFoundation.Overdrive.Samples.MathBook
{
    [Serializable]
    public class MathBookAsset : GraphAsset
    {
        protected override Type GraphModelType => typeof(MathBook);

        [MenuItem("Assets/Create/GTF Samples/Math Book/Math Book")]
        public static void CreateGraph()
        {
            const string path = "Assets";
            var template = new GraphTemplate<MathBookStencil>(MathBookStencil.GraphName);

            GraphAssetCreationHelpers.CreateInProjectWindow<MathBookAsset>(template, null, path,
                () => GraphViewEditorWindow.FindOrCreateGraphWindow<SimpleGraphViewWindow>());
        }

        [OnOpenAsset(1)]
        public static bool OpenGraphAsset(int instanceId, int line)
        {
            var obj = EditorUtility.InstanceIDToObject(instanceId);
            if (obj is MathBookAsset graphAsset)
            {
                var window = GraphViewEditorWindow.FindOrCreateGraphWindow<SimpleGraphViewWindow>(graphAsset.FilePath);
                graphAsset = window.GraphTool?.ToolState?.CurrentGraph.GetGraphAsset() as MathBookAsset ?? graphAsset;
                window.SetCurrentSelection(graphAsset, GraphViewEditorWindow.OpenMode.OpenAndFocus);
                return true;
            }

            return false;
        }
    }
}
