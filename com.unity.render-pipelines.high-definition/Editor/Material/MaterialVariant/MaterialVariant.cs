using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEditor.ShaderGraph;

namespace Unity.Assets.MaterialVariant.Editor
{
    public class MaterialVariant : ScriptableObject
    {
        public string rootGUID;
        public bool isShader;

        public List<MaterialPropertyModification> overrides = new List<MaterialPropertyModification>();
        
        public void TrimPreviousOverridesAndAdd(IEnumerable<MaterialPropertyModification> modifications)
        {
            foreach(var modification in modifications)
            {
                int pos = overrides.FindIndex(o => o.propertyPath == modification.propertyPath);
                if (pos > -1)
                    overrides[pos] = modification;
                else
                    overrides.Add(modification);
            }
        }

        private static bool IsValidRoot(Object root)
        {
            return EditorUtility.IsPersistent(root) && ((root is Material) || (root is Shader));
        }

        public static void CreateVariant(Object target)
        {
            var rootMaterial = target as Material;
            var rootShader = target as Shader;

            if (EditorUtility.IsPersistent(target) && (rootMaterial || rootShader))
            {
                var matVariant = ScriptableObject.CreateInstance<MaterialVariant>();

                if (rootShader)
                {
                    matVariant.isShader = true;

                    var path = AssetDatabase.GetAssetPath(rootShader);
                    var importer = AssetImporter.GetAtPath(path);

                    if (importer is ShaderGraphImporter)
                        matVariant.rootGUID = AssetDatabase.AssetPathToGUID(importer.assetPath);
                    else
                        matVariant.rootGUID = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(rootShader));
                }
                else
                {
                    matVariant.isShader = false;

                    var path = AssetDatabase.GetAssetPath(rootMaterial);
                    var importer = AssetImporter.GetAtPath(path);

                    if (importer is MaterialVariantImporter)
                        matVariant.rootGUID = AssetDatabase.AssetPathToGUID(importer.assetPath);
                    else
                        matVariant.rootGUID = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(rootMaterial));
                }

                matVariant.overrides = new List<MaterialPropertyModification>();

                var targetPath = AssetDatabase.GetAssetPath(target);
                targetPath = Path.Combine(Path.GetDirectoryName(targetPath),
                    Path.GetFileNameWithoutExtension(targetPath) + " Variant.matVariant");
                targetPath = AssetDatabase.GenerateUniqueAssetPath(targetPath);

                InternalEditorUtility.SaveToSerializedFileAndForget(new[] { matVariant }, targetPath, true);
                AssetDatabase.ImportAsset(targetPath);
            }
        }
        
        private const string MENU_ITEM_PATH = "Assets/Create/Variants/Material Variant";

        [MenuItem(MENU_ITEM_PATH, false)]
        private static void CreateMaterialVariantMenu()
        {
            CreateVariant(Selection.activeObject);
        }

        [MenuItem(MENU_ITEM_PATH, true)]
        private static bool ValidateMaterialVariantMenu()
        {
            return IsValidRoot(Selection.activeObject);
        }
    }
}
