using System;
using UnityEditor;
using UnityEngine;

namespace FS.MeshProcessing
{
    [AttributeUsage(AttributeTargets.Field, Inherited = true)]
    public class GameObjectTagsAttribute : PropertyAttribute
    {}
    
    [AttributeUsage(AttributeTargets.Field, Inherited = true)]
    public class PhysicsLayerAttribute : PropertyAttribute
    {}
    
#if UNITY_EDITOR 

    // PropertyDrawer for strings that display a list of GameObject tags in a dropdown
    [CustomPropertyDrawer(typeof(GameObjectTagsAttribute))]
    public class GameObjectTagsPropertyDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType == SerializedPropertyType.String)
            {
                EditorGUI.BeginProperty(position, label, property);
                
                string[] tags = UnityEditorInternal.InternalEditorUtility.tags;
                int selectedIndex = Array.IndexOf(tags, property.stringValue);
                
                EditorGUI.BeginChangeCheck();
                
                // Create a dropdown for the tags
                selectedIndex = EditorGUI.Popup(position, label.text, selectedIndex, tags);

                if (EditorGUI.EndChangeCheck())
                {
                    // Set the selected tag as the property value
                    if (selectedIndex >= 0 && selectedIndex < tags.Length)
                    {
                        property.serializedObject.Update();
                        property.stringValue = tags[selectedIndex];
                        property.serializedObject.ApplyModifiedProperties();
                    }
                }

                EditorGUI.EndProperty();
            }
            else
            {
                EditorGUI.LabelField(position, label.text, "Use GameObjectTags with string.");
            }
        }
    }
    
    // PropertyDrawer for strings that display a list of Physics Layers in a dropdown
    [CustomPropertyDrawer(typeof(PhysicsLayerAttribute))]
    public class PhysicsLayerAttributePropertyDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType == SerializedPropertyType.String || property.propertyType == SerializedPropertyType.Integer)
            {
                EditorGUI.BeginProperty(position, label, property);
                
                string[] layers = UnityEditorInternal.InternalEditorUtility.layers;
                int selectedIndex = property.propertyType == SerializedPropertyType.Integer ? property.intValue : Array.IndexOf(layers, property.stringValue);
                
                EditorGUI.BeginChangeCheck();
                
                // Create a dropdown for the tags
                selectedIndex = EditorGUI.Popup(position, label.text, selectedIndex, layers);

                if (EditorGUI.EndChangeCheck())
                {
                    // Set the selected tag as the property value
                    if (selectedIndex >= 0 && selectedIndex < layers.Length)
                    {
                        property.serializedObject.Update();
                        if (property.propertyType == SerializedPropertyType.String) property.stringValue = layers[selectedIndex];
                        if (property.propertyType == SerializedPropertyType.Integer) property.intValue = selectedIndex;
                        property.serializedObject.ApplyModifiedProperties();
                    }
                }

                EditorGUI.EndProperty();
            }
            else
            {
                EditorGUI.LabelField(position, label.text, "Use PhysicsLayers Attribute with string.");
            }
        }
    }

#endif     
}