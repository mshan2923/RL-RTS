using System;
using UnityEditor;
using UnityEngine;

[AttributeUsage(AttributeTargets.Field)]
public class ObjectFieldConstraintAttribute : PropertyAttribute
{
    public Type Type;
    public bool AllowSceneObjects;

    public ObjectFieldConstraintAttribute(Type type, bool allowSceneObjects = true)
    {
        Type = type;
        AllowSceneObjects = allowSceneObjects;
    }
}


[CustomPropertyDrawer(typeof(ObjectFieldConstraintAttribute))]
public class ObjectFieldConstraintDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var attr = (ObjectFieldConstraintAttribute)attribute;

        if (property.propertyType != SerializedPropertyType.ObjectReference)
        {
            EditorGUI.PropertyField(position, property, label, true);
            return;
        }

        EditorGUI.BeginProperty(position, label, property);
        EditorGUI.BeginChangeCheck();

        var newObj = EditorGUI.ObjectField(
            position, label, property.objectReferenceValue,
            attr.Type, attr.AllowSceneObjects);

        if (EditorGUI.EndChangeCheck())
            property.objectReferenceValue = newObj;

        EditorGUI.EndProperty();
    }
}