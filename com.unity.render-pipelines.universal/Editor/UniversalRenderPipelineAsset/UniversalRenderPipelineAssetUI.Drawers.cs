using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    using CED = CoreEditorDrawer<SerializedUniversalRenderPipelineAsset>;

    internal partial class UniversalRenderPipelineAssetUI
    {
        enum Expandable
        {
            Quality = 1 << 1,
            Rendering = 1 << 2,
#if ADAPTIVE_PERFORMANCE_2_0_0_OR_NEWER
            AdaptivePerformance = 1 << 3,
#endif
        }

        enum ExpandableAdditional
        {
            Quality = 1 << 1,
        }

        internal static void RegisterEditor(UniversalRenderPipelineAssetEditor editor)
        {
            k_AdditionalPropertiesState.RegisterEditor(editor);
            RenderersFoldoutStates.GetAdditionalRenderersShowState().RegisterEditor(editor);
        }

        internal static void UnregisterEditor(UniversalRenderPipelineAssetEditor editor)
        {
            k_AdditionalPropertiesState.UnregisterEditor(editor);
            RenderersFoldoutStates.GetAdditionalRenderersShowState().UnregisterEditor(editor);
        }

        [SetAdditionalPropertiesVisibility]
        internal static void SetAdditionalPropertiesVisibility(bool value)
        {
            if (value)
                k_AdditionalPropertiesState.ShowAll();
            else
                k_AdditionalPropertiesState.HideAll();
        }



        static readonly ExpandedState<Expandable, UniversalRenderPipelineAsset> k_ExpandedState = new(Expandable.Rendering, "URP");
        static readonly AdditionalPropertiesState<ExpandableAdditional, Light> k_AdditionalPropertiesState = new(0, "URP");

        public static readonly CED.IDrawer Inspector = CED.Group(
            CED.AdditionalPropertiesFoldoutGroup(Styles.qualitySettingsText, Expandable.Quality, k_ExpandedState, ExpandableAdditional.Quality, k_AdditionalPropertiesState, DrawQuality, DrawQualityAdditional),
            CED.FoldoutGroup(Styles.renderersSettingsText, Expandable.Rendering, k_ExpandedState, FoldoutOption.NoSpaceAtEnd, DrawRenderers)
#if ADAPTIVE_PERFORMANCE_2_0_0_OR_NEWER
            , CED.FoldoutGroup(Styles.adaptivePerformanceText, Expandable.AdaptivePerformance, k_ExpandedState, CED.Group(DrawAdaptivePerformance))
#endif
        );


        static void DrawRenderers(SerializedUniversalRenderPipelineAsset serialized, Editor ownerEditor)
        {
            if (ownerEditor is UniversalRenderPipelineAssetEditor urpAssetEditor)
            {
                //Default renderer
                var rendererNames = new GUIContent[serialized.rendererDataProp.arraySize];
                for (int i = 0; i < serialized.rendererDataProp.arraySize; i++)
                {
                    var rendererProp = serialized.rendererDataProp.GetArrayElementAtIndex(i);
                    rendererNames[i] = new GUIContent($"{i} - {rendererProp.FindPropertyRelative(nameof(ScriptableRendererData.name)).stringValue}");
                }
                EditorGUILayout.Space();
                serialized.defaultRendererProp.intValue = EditorGUILayout.Popup(Styles.rendererDefaultText, serialized.defaultRendererProp.intValue, rendererNames);
                EditorGUILayout.Space();

                //Draw Renderers
                for (int i = 0; i < serialized.rendererDataProp.arraySize; i++)
                {
                    ScriptableRendererDataEditor.CurrentIndex = i;
                    EditorGUILayout.PropertyField(serialized.rendererDataProp.GetArrayElementAtIndex(i));
                }

                if (!serialized.asset.ValidateRendererData(-1))
                    EditorGUILayout.HelpBox(Styles.rendererMissingDefaultMessage.text, MessageType.Error, true);
                else if (!serialized.asset.ValidateRendererDataList(true))
                    EditorGUILayout.HelpBox(Styles.rendererMissingMessage.text, MessageType.Warning, true);
                else if (!ValidateRendererGraphicsAPIs(serialized.asset, out var unsupportedGraphicsApisMessage))
                    EditorGUILayout.HelpBox(Styles.rendererUnsupportedAPIMessage.text + unsupportedGraphicsApisMessage, MessageType.Warning, true);

                EditorGUILayout.Space();
                if (GUILayout.Button(Styles.rendererAddMessage))
                {
                    new ScriptableRendererDataDropdown(serialized.rendererDataProp).Show(new Rect(Event.current.mousePosition, Vector2.zero));
                }
            }
        }

        static void DrawQuality(SerializedUniversalRenderPipelineAsset serialized, Editor ownerEditor)
        {
            EditorGUILayout.PropertyField(serialized.hdr, Styles.hdrText);
            bool isHdrOn = serialized.hdr.boolValue;
            if (UniversalRenderPipelineGlobalSettings.instance.postProcessData != null)
            {
                if (!isHdrOn && UniversalRenderPipelineGlobalSettings.instance.colorGradingMode == ColorGradingMode.HighDynamicRange)
                    EditorGUILayout.HelpBox(Styles.colorGradingModeWarning, MessageType.Warning);
                else if (isHdrOn && UniversalRenderPipelineGlobalSettings.instance.colorGradingMode == ColorGradingMode.HighDynamicRange)
                    EditorGUILayout.HelpBox(Styles.colorGradingModeSpecInfo, MessageType.Info);
                if (isHdrOn && UniversalRenderPipelineGlobalSettings.instance.colorGradingMode == ColorGradingMode.HighDynamicRange && UniversalRenderPipelineGlobalSettings.instance.colorGradingLutSize < 32)
                    EditorGUILayout.HelpBox(Styles.colorGradingLutSizeWarning, MessageType.Warning);
            }
            EditorGUILayout.PropertyField(serialized.msaa, Styles.msaaText);
            serialized.renderScale.floatValue = EditorGUILayout.Slider(Styles.renderScaleText, serialized.renderScale.floatValue, UniversalRenderPipeline.minRenderScale, UniversalRenderPipeline.maxRenderScale);
        }
        static void DrawQualityAdditional(SerializedUniversalRenderPipelineAsset serialized, Editor ownerEditor)
        {
            EditorGUILayout.PropertyField(serialized.srpBatcher, Styles.srpBatcher);
            EditorGUILayout.PropertyField(serialized.supportsDynamicBatching, Styles.dynamicBatching);
        }

        static bool ValidateRendererGraphicsAPIs(UniversalRenderPipelineAsset pipelineAsset, out string unsupportedGraphicsApisMessage)
        {
            // Check the list of Renderers against all Graphics APIs the player is built with.
            unsupportedGraphicsApisMessage = null;

            BuildTarget platform = EditorUserBuildSettings.activeBuildTarget;
            GraphicsDeviceType[] graphicsAPIs = PlayerSettings.GetGraphicsAPIs(platform);
            int rendererCount = pipelineAsset.m_RendererDataReferenceList.Length;

            for (int i = 0; i < rendererCount; i++)
            {
                ScriptableRenderer renderer = pipelineAsset.GetRenderer(i);
                if (renderer == null)
                    continue;

                GraphicsDeviceType[] unsupportedAPIs = renderer.unsupportedGraphicsDeviceTypes;

                for (int apiIndex = 0; apiIndex < unsupportedAPIs.Length; apiIndex++)
                {
                    if (System.Array.FindIndex(graphicsAPIs, element => element == unsupportedAPIs[apiIndex]) >= 0)
                        unsupportedGraphicsApisMessage += System.String.Format("{0} at index {1} does not support {2}.\n", renderer, i, unsupportedAPIs[apiIndex]);
                }
            }

            return unsupportedGraphicsApisMessage == null;
        }


#if ADAPTIVE_PERFORMANCE_2_0_0_OR_NEWER
        static void DrawAdaptivePerformance(SerializedUniversalRenderPipelineAsset serialized, Editor ownerEditor)
        {
            EditorGUILayout.PropertyField(serialized.useAdaptivePerformance, Styles.useAdaptivePerformance);
        }
#endif
    }
}
